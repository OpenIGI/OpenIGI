using BenchmarkDotNet.Attributes;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Benchmarks;

/// <summary>What a lightmap bake's ray tracing costs.</summary>
/// <remarks>
/// <para>
/// The bake fires a hemisphere of rays per texel across every lightmapped page in a level, so
/// <see cref="LightmapBaker.LightmapBvh.ClosestHit"/> is called tens of millions of times per bake and
/// is essentially the whole of its cost. It is already spread over every core by
/// <c>Parallel.For</c>, which shortens the wall clock but does not make the traversal itself cheaper.
/// </para>
/// <para>
/// The scene is synthetic but shaped like the real thing: a ground plane with boxes standing on it,
/// which gives the hierarchy something to actually partition and gives the rays a realistic mix of
/// near hits, far hits and escapes to the sky. Rays are drawn once into an array so the benchmark
/// measures traversal rather than the sampler that generated them.
/// </para>
/// <para>
/// World coordinates are placed tens of kilometres from the origin deliberately: that is where the
/// game's levels sit, and it is the reason the traversal is in double precision at all.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LightmapBvhBenchmarks
{
    private const int RayCount = 4096;

    private const double WorldOrigin = 24_000.0;

    private LightmapBaker.LightmapBvh? _bvh;
    private LightmapBaker.LightmapTriangle[] _triangles = [];
    private Vector3d[] _origins = [];
    private Vector3d[] _directions = [];

    /// <summary>Gets or sets how many boxes stand on the ground plane.</summary>
    [Params(64, 512)]
    public int Boxes { get; set; }

    /// <summary>Builds the scene, the hierarchy and the ray set.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _triangles = BuildScene(Boxes);
        _bvh = LightmapBaker.LightmapBvh.Build(_triangles);

        _origins = new Vector3d[RayCount];
        _directions = new Vector3d[RayCount];

        // A cosine-ish hemisphere sample from a point just above the ground, which is what a texel's
        // gather actually looks like. Deterministic so every run traces the same rays.
        uint state = 0x1234_5678u;

        for (int i = 0; i < RayCount; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            double u = ((state >> 8) & 0xFFFF) / 65535.0;
            state = (state * 1664525u) + 1013904223u;
            double v = ((state >> 8) & 0xFFFF) / 65535.0;

            double radius = Math.Sqrt(u);
            double angle = Math.Tau * v;

            _origins[i] = new Vector3d(
                WorldOrigin + (u * 100.0),
                WorldOrigin + (v * 100.0),
                0.05);

            _directions[i] = new Vector3d(
                radius * Math.Cos(angle),
                radius * Math.Sin(angle),
                Math.Sqrt(Math.Max(1.0 - u, 0.0)));
        }
    }

    /// <summary>Building the hierarchy, which the bake pays once per level.</summary>
    /// <returns>The built hierarchy.</returns>
    [Benchmark(Description = "LightmapBvh.Build")]
    public LightmapBaker.LightmapBvh Build() =>
        LightmapBaker.LightmapBvh.Build(_triangles);

    /// <summary>Tracing the rays, which is where a bake spends essentially all of its time.</summary>
    /// <returns>How many rays hit something, so nothing is dead code.</returns>
    [Benchmark(Description = "LightmapBvh.ClosestHit x4096")]
    public int ClosestHit()
    {
        int hits = 0;

        for (int i = 0; i < RayCount; i++)
        {
            if (_bvh!.ClosestHit(_origins[i], _directions[i], 200.0, out _, out _, out _))
            {
                hits++;
            }
        }

        return hits;
    }

    /// <summary>The same rays cut off before they reach anything, to price the call itself.</summary>
    /// <remarks>
    /// A zero reach makes the root's slab test fail immediately, so what this measures is everything a
    /// query costs before it traverses anything: the three reciprocals, the stack the method carves out
    /// of its frame, and the call itself. Subtracting it from the full query says how much of the time is
    /// actually spent walking the hierarchy.
    /// </remarks>
    /// <returns>Zero, since nothing can be hit.</returns>
    [Benchmark(Description = "ClosestHit x4096 (no reach)")]
    public int ClosestHitWithoutReach()
    {
        int hits = 0;

        for (int i = 0; i < RayCount; i++)
        {
            if (_bvh!.ClosestHit(_origins[i], _directions[i], 0.0, out _, out _, out _))
            {
                hits++;
            }
        }

        return hits;
    }

    private static LightmapBaker.LightmapTriangle[] BuildScene(int boxes)
    {
        List<LightmapBaker.LightmapTriangle> triangles = [];
        Vector3f albedo = new(0.5f, 0.5f, 0.5f);

        void Quad(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            triangles.Add(new LightmapBaker.LightmapTriangle(a, b, c, 0, 0f, 0f, 1f, 0f, 1f, 1f, albedo));
            triangles.Add(new LightmapBaker.LightmapTriangle(a, c, d, 0, 0f, 0f, 1f, 1f, 0f, 1f, albedo));
        }

        // The ground, split into a grid so the hierarchy has something to partition rather than two
        // enormous triangles that every node's box has to contain.
        const int groundCells = 32;
        const double groundSize = 200.0;
        double cell = groundSize / groundCells;

        for (int y = 0; y < groundCells; y++)
        {
            for (int x = 0; x < groundCells; x++)
            {
                double x0 = WorldOrigin + (x * cell);
                double y0 = WorldOrigin + (y * cell);

                Quad(
                    new Vector3d(x0, y0, 0.0),
                    new Vector3d(x0 + cell, y0, 0.0),
                    new Vector3d(x0 + cell, y0 + cell, 0.0),
                    new Vector3d(x0, y0 + cell, 0.0));
            }
        }

        int side = (int)Math.Ceiling(Math.Sqrt(boxes));

        for (int i = 0; i < boxes; i++)
        {
            double bx = WorldOrigin + ((i % side) * (groundSize / side)) + 1.0;
            double by = WorldOrigin + ((i / side) * (groundSize / side)) + 1.0;
            double height = 2.0 + (i % 5);
            const double width = 2.0;

            // Four walls. No lid and no floor: a bake traces against what is drawn, and neither is.
            Quad(
                new Vector3d(bx, by, 0.0),
                new Vector3d(bx + width, by, 0.0),
                new Vector3d(bx + width, by, height),
                new Vector3d(bx, by, height));
            Quad(
                new Vector3d(bx, by + width, 0.0),
                new Vector3d(bx + width, by + width, 0.0),
                new Vector3d(bx + width, by + width, height),
                new Vector3d(bx, by + width, height));
            Quad(
                new Vector3d(bx, by, 0.0),
                new Vector3d(bx, by + width, 0.0),
                new Vector3d(bx, by + width, height),
                new Vector3d(bx, by, height));
            Quad(
                new Vector3d(bx + width, by, 0.0),
                new Vector3d(bx + width, by + width, 0.0),
                new Vector3d(bx + width, by + width, height),
                new Vector3d(bx + width, by, height));
        }

        return [.. triangles];
    }
}
