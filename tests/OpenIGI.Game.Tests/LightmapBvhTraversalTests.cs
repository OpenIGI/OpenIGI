using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks <see cref="LightmapBaker.LightmapBvh"/> against testing every triangle.
/// </summary>
/// <remarks>
/// <para>
/// A hierarchy is an optimisation over the obvious answer, so the obvious answer is the oracle: the
/// nearest triangle a ray meets is whatever a loop over all of them finds, and the hierarchy must agree
/// with that for every ray or it is skipping geometry. This matters more than it looks — the traversal
/// prunes aggressively, visiting nine nodes out of a couple of thousand for a typical gather ray, and
/// every one of those prunes is a chance to discard the node holding the answer.
/// </para>
/// <para>
/// A bake failure of that kind does not throw. It darkens one texel on one surface, in a level that
/// takes minutes to bake and is then cached, which is close to the least observable way for this code
/// to be wrong.
/// </para>
/// <para>
/// Rays are placed at the scale the game actually uses, tens of kilometres from the origin, because
/// that is the whole reason the traversal is in double precision and the reason its bounds are widened
/// outward rather than merely rounded.
/// </para>
/// </remarks>
public sealed class LightmapBvhTraversalTests
{
    private const double WorldOrigin = 24_000.0;

    /// <summary>Every ray must find exactly what a search over all triangles finds.</summary>
    [Fact]
    public void TraversalAgreesWithTestingEveryTriangle()
    {
        LightmapBaker.LightmapTriangle[] triangles = Scene();
        LightmapBaker.LightmapBvh bvh = LightmapBaker.LightmapBvh.Build(triangles);

        int compared = 0;
        int hits = 0;

        foreach ((Vector3d origin, Vector3d direction, double reach) in Rays())
        {
            bool found = bvh.ClosestHit(origin, direction, reach, out int triangle, out _, out _);
            double best = Brute(triangles, origin, direction, reach);

            compared++;

            if (!found)
            {
                Assert.True(
                    double.IsPositiveInfinity(best),
                    $"the hierarchy missed a triangle at {best} that a full search found.");
                continue;
            }

            hits++;

            double actual = PlaneDistance(origin, direction, triangles[triangle]);

            // Compared by distance rather than by index: a ray landing exactly on the edge two triangles
            // share meets both at the same distance, and either is the right answer.
            Assert.True(
                Math.Abs(actual - best) <= 1e-9 * Math.Max(Math.Abs(best), 1.0),
                $"the hierarchy returned a hit at {actual}; the nearest is at {best}.");
        }

        // The scene and the rays are only worth anything if they actually reach geometry.
        Assert.True(compared > 2000, $"expected a substantial ray set, compared {compared}.");
        Assert.True(hits > compared / 10, $"expected many of the rays to hit, {hits} of {compared} did.");
    }

    /// <summary>A hierarchy over no triangles must miss rather than throw.</summary>
    [Fact]
    public void EmptyHierarchyMissesEverything()
    {
        LightmapBaker.LightmapBvh bvh = LightmapBaker.LightmapBvh.Build([]);

        Assert.False(bvh.ClosestHit(
            new Vector3d(WorldOrigin, WorldOrigin, 1.0), new Vector3d(0, 0, 1), 100.0, out int triangle, out _, out _));

        Assert.Equal(-1, triangle);
    }

    /// <summary>The nearest hit a full search over every triangle finds, or infinity.</summary>
    private static double Brute(
        LightmapBaker.LightmapTriangle[] triangles, Vector3d origin, Vector3d direction, double reach)
    {
        double best = double.PositiveInfinity;

        foreach (LightmapBaker.LightmapTriangle triangle in triangles)
        {
            if (Moeller(origin, direction, triangle, out double t) && t > 1e-5 && t < reach && t < best)
            {
                best = t;
            }
        }

        return best;
    }

    /// <summary>Moeller-Trumbore, written out independently of the one under test.</summary>
    private static bool Moeller(
        Vector3d origin, Vector3d direction, LightmapBaker.LightmapTriangle tri, out double t)
    {
        t = 0;

        Vector3d e1 = Sub(tri.B, tri.A);
        Vector3d e2 = Sub(tri.C, tri.A);
        Vector3d p = Cross(direction, e2);

        double determinant = Dot(e1, p);

        if (determinant is > -1e-12 and < 1e-12)
        {
            return false;
        }

        double inverse = 1.0 / determinant;
        Vector3d s = Sub(origin, tri.A);
        double u = Dot(s, p) * inverse;

        if (u is < 0.0 or > 1.0)
        {
            return false;
        }

        Vector3d q = Cross(s, e1);
        double v = Dot(direction, q) * inverse;

        if (v < 0.0 || u + v > 1.0)
        {
            return false;
        }

        t = Dot(e2, q) * inverse;
        return t > 0.0;
    }

    private static double PlaneDistance(Vector3d origin, Vector3d direction, LightmapBaker.LightmapTriangle tri)
    {
        Vector3d normal = Cross(Sub(tri.B, tri.A), Sub(tri.C, tri.A));
        return Dot(normal, Sub(tri.A, origin)) / Dot(normal, direction);
    }

    private static Vector3d Sub(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Dot(Vector3d a, Vector3d b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector3d Cross(Vector3d a, Vector3d b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    /// <summary>A ground plane with boxes on it — enough geometry for the hierarchy to have to prune.</summary>
    private static LightmapBaker.LightmapTriangle[] Scene()
    {
        List<LightmapBaker.LightmapTriangle> triangles = [];
        Vector3f albedo = new(0.5f, 0.5f, 0.5f);

        void Quad(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            triangles.Add(new LightmapBaker.LightmapTriangle(a, b, c, 0, 0f, 0f, 1f, 0f, 1f, 1f, albedo));
            triangles.Add(new LightmapBaker.LightmapTriangle(a, c, d, 0, 0f, 0f, 1f, 1f, 0f, 1f, albedo));
        }

        const int cells = 12;
        const double size = 60.0;
        const double cell = size / cells;

        for (int y = 0; y < cells; y++)
        {
            for (int x = 0; x < cells; x++)
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

        for (int i = 0; i < 24; i++)
        {
            double bx = WorldOrigin + ((i % 6) * 9.0) + 1.5;
            double by = WorldOrigin + ((i / 6) * 9.0) + 1.5;
            double height = 2.0 + (i % 4);
            const double width = 2.5;

            Quad(
                new Vector3d(bx, by, 0.0), new Vector3d(bx + width, by, 0.0),
                new Vector3d(bx + width, by, height), new Vector3d(bx, by, height));
            Quad(
                new Vector3d(bx, by + width, 0.0), new Vector3d(bx + width, by + width, 0.0),
                new Vector3d(bx + width, by + width, height), new Vector3d(bx, by + width, height));
            Quad(
                new Vector3d(bx, by, 0.0), new Vector3d(bx, by + width, 0.0),
                new Vector3d(bx, by + width, height), new Vector3d(bx, by, height));
            Quad(
                new Vector3d(bx + width, by, 0.0), new Vector3d(bx + width, by + width, 0.0),
                new Vector3d(bx + width, by + width, height), new Vector3d(bx + width, by, height));
        }

        return [.. triangles];
    }

    /// <summary>Gather-shaped rays, plus the axis-aligned ones that drive the slab test through NaN.</summary>
    private static IEnumerable<(Vector3d Origin, Vector3d Direction, double Reach)> Rays()
    {
        Vector3d[] axes =
        [
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
            new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        ];

        for (int i = 0; i < 10; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                Vector3d origin = new(
                    WorldOrigin + (i * 6.0) + 0.37,
                    WorldOrigin + (j * 6.0) + 0.11,
                    ((i + j) % 4) * 1.5);

                foreach (Vector3d axis in axes)
                {
                    yield return (origin, axis, 120.0);
                }
            }
        }

        uint state = 0x0BADC0DEu;

        double Next()
        {
            state = (state * 1664525u) + 1013904223u;
            return ((state >> 8) & 0xFFFF) / 65535.0;
        }

        for (int i = 0; i < 3000; i++)
        {
            Vector3d origin = new(
                WorldOrigin + (Next() * 60.0), WorldOrigin + (Next() * 60.0), Next() * 5.0);

            double z = (Next() * 2.0) - 1.0;
            double angle = Math.Tau * Next();
            double radius = Math.Sqrt(Math.Max(1.0 - (z * z), 0.0));

            yield return (
                origin,
                new Vector3d(radius * Math.Cos(angle), radius * Math.Sin(angle), z),
                1.0 + (Next() * 150.0));
        }
    }
}
