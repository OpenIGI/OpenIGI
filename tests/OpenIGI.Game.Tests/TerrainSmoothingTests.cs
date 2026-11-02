using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The finest terrain refined onto <see cref="TerrainSmoothField"/>: it stays closed, stays near the
/// ground the player stands on, keeps the material weights summing to one, and is actually smoother.
/// </summary>
/// <remarks>
/// Level 1 at the Ultra view distance, from the camera <see cref="TerrainHeightSeamTests"/> uses, with
/// the vertex stage's level-of-detail fade reproduced on the CPU as that test does.
/// </remarks>
public sealed class TerrainSmoothingTests(ITestOutputHelper output)
{
    private const double Limit = 0.3 * WorldScene.UnitsPerMetre;

    [RequiresGameDataFact]
    public void RefinedGroundIsWatertightAfterTheFade()
    {
        Scene scene = Scene.Build(smooth: true);
        double fadeOffset = (long)((scene.View.ViewDistance + 64) * 0.375);
        double fadeSlope = 2.6666667 / (scene.View.ViewDistance - 64);

        Dictionary<(long, long), List<(int Chunk, bool Refined, double Z)>> columns = [];
        for (int c = 0; c < scene.Built.Count; c++)
        {
            (TerrainChunk chunk, TerrainChunkGeometry geometry, bool refined) = scene.Built[c];
            double scale = 0.375 / (1 << (25 - chunk.LodExponent));
            for (int i = 0; i < geometry.VerticesPerPass; i++)
            {
                Vertex3D v = geometry.Vertices[i];
                double wx = chunk.Position.X + v.Position.X;
                double wy = chunk.Position.Y + v.Position.Y;
                double wz = chunk.Position.Z + v.Position.Z;
                double tx = wx + v.Normal.X - scene.Camera.Position.X;
                double ty = wy + v.Normal.Y - scene.Camera.Position.Y;
                double tz = wz + v.Normal.Z - scene.Camera.Position.Z;
                double reach = Math.Max(Math.Abs(tx), Math.Max(Math.Abs(ty), Math.Abs(tz)));
                double t = Math.Clamp((reach * scale - fadeOffset) * fadeSlope, 0.0, 1.0);

                var key = ((long)Math.Round((wx + (v.Normal.X * t)) / 16.0), (long)Math.Round((wy + (v.Normal.Y * t)) / 16.0));
                if (!columns.TryGetValue(key, out List<(int, bool, double)>? list))
                {
                    columns[key] = list = [];
                }

                list.Add((c, refined, wz + (v.Normal.Z * t)));
            }
        }

        int shared = 0;
        int refinedShared = 0;
        List<string> cracks = [];
        foreach (((long x, long y), List<(int Chunk, bool Refined, double Z)> list) in columns)
        {
            if (list.Select(e => e.Chunk).Distinct().Count() < 2)
            {
                continue;
            }

            shared++;
            bool anyRefined = list.Any(e => e.Refined);
            if (anyRefined)
            {
                refinedShared++;
            }

            double gap = list.Max(e => e.Z) - list.Min(e => e.Z);
            if (gap > 0.01 * WorldScene.UnitsPerMetre)
            {
                cracks.Add(string.Create(CultureInfo.InvariantCulture, $"({x * 16},{y * 16}) gap {gap / WorldScene.UnitsPerMetre:F3} m, refined {anyRefined}"));
            }
        }

        output.WriteLine($"{shared} shared columns, {refinedShared} touching refined ground, {cracks.Count} cracks");
        Assert.True(refinedShared > 1000, $"only {refinedShared} shared columns on refined ground");
        Assert.True(cracks.Count == 0, $"{cracks.Count} cracks:\n{string.Join("\n", cracks.Take(10))}");
    }

    [RequiresGameDataFact]
    public void RefinedGroundStaysNearTheGroundUnderfoot()
    {
        Scene scene = Scene.Build(smooth: true);
        int checkedPoints = 0;
        int moved = 0;
        double largest = 0.0;
        List<string> strays = [];
        foreach ((TerrainChunk chunk, TerrainChunkGeometry geometry, bool refined) in scene.Built)
        {
            // The ground underfoot is the finest level's; the coarser ones have limits of their own.
            if (!refined || chunk.LodExponent != TerrainSmoothField.FinestExponent)
            {
                continue;
            }

            for (int i = 0; i < geometry.VerticesPerPass; i++)
            {
                Vertex3D v = geometry.Vertices[i];
                double wx = chunk.Position.X + v.Position.X;
                double wy = chunk.Position.Y + v.Position.Y;
                double wz = chunk.Position.Z + v.Position.Z;
                if (!scene.Terrain.TryDrop(wx, wy, wz, WorldScene.UnitsPerMetre, upward: false, out TerrainGround ground))
                {
                    continue;
                }

                checkedPoints++;
                double offset = Math.Abs(wz - ground.Height);
                largest = Math.Max(largest, offset);
                if (offset > 0.02 * WorldScene.UnitsPerMetre)
                {
                    moved++;
                }

                if (offset > Limit + (0.01 * WorldScene.UnitsPerMetre))
                {
                    strays.Add(string.Create(CultureInfo.InvariantCulture, $"({wx:F0},{wy:F0}) {offset / WorldScene.UnitsPerMetre:F3} m off the ground"));
                }
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{checkedPoints} refined points, {moved} moved over 2 cm, largest {largest / WorldScene.UnitsPerMetre:F3} m"));
        Assert.True(checkedPoints > 10000, $"only {checkedPoints} refined points found ground");
        Assert.True(moved > checkedPoints / 50, $"only {moved} of {checkedPoints} points were smoothed at all");

        // The ground query takes the first surface down from just above the point, so where two
        // surfaces overlap it can read the other one. Those are rare; anything more is a real stray.
        Assert.True(strays.Count <= checkedPoints / 1000, $"{strays.Count} points past the limit:\n{string.Join("\n", strays.Take(10))}");
    }

    [RequiresGameDataFact]
    public void MaterialWeightsStillSumToOne()
    {
        Scene scene = Scene.Build(smooth: true);
        int vertices = 0;
        foreach ((TerrainChunk _, TerrainChunkGeometry geometry, bool refined) in scene.Built)
        {
            if (!refined)
            {
                continue;
            }

            int perPass = geometry.VerticesPerPass;
            for (int i = 0; i < perPass; i++)
            {
                // The draw adds every pass: each one's weight times the fade on its first scale and the
                // fade's complement on its second, so per material the two scales together carry the
                // weight once. Summed over the base scale of every material it must be one at any fade.
                double c0 = 0.0;
                double c1 = 0.0;
                // Layer k's vertices are the k-th whole copy of the chunk.
                for (int k = 0; k < geometry.Layers.Length; k++)
                {
                    if (geometry.Layers[k].IsDetail)
                    {
                        continue;
                    }

                    Bgra32 colour = geometry.Vertices[(k * perPass) + i].Colour;
                    c0 += colour.R / 255.0;
                    c1 += (colour.G - colour.B) / 255.0;
                }

                Assert.InRange(c0, 0.98, 1.02);
                Assert.InRange(c1, -0.02, 0.02);
                vertices++;
            }
        }

        Assert.True(vertices > 10000, $"only {vertices} refined vertices");
    }

    [RequiresGameDataFact]
    public void RefinedGroundCreasesLess()
    {
        double before = SteepestCreases(Scene.Build(smooth: false));
        double after = SteepestCreases(Scene.Build(smooth: true));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"99th-percentile crease between neighbouring triangles: {before:F1}° stored, {after:F1}° smoothed"));
        Assert.True(after < before * 0.6, $"smoothed creases {after:F1}° against {before:F1}° stored");

        static double SteepestCreases(Scene scene)
        {
            var angles = new List<double>();
            foreach ((TerrainChunk chunk, TerrainChunkGeometry geometry, bool _) in scene.Built)
            {
                if (chunk.LodExponent != TerrainSmoothField.FinestExponent)
                {
                    continue;
                }

                TerrainChunkLayer layer = geometry.Layers[0];
                var faces = new Dictionary<(int, int), Vector3d>();
                for (int t = 0; t < layer.IndexCount; t += 3)
                {
                    int a = geometry.Indices[layer.IndexOffset + t];
                    int b = geometry.Indices[layer.IndexOffset + t + 1];
                    int c = geometry.Indices[layer.IndexOffset + t + 2];
                    Vector3d pa = P(geometry.Vertices[a].Position);
                    Vector3d pb = P(geometry.Vertices[b].Position);
                    Vector3d pc = P(geometry.Vertices[c].Position);
                    Vector3d normal = Vector3d.Cross(pb - pa, pc - pa);
                    if (normal.Length < 1e-9)
                    {
                        continue;
                    }

                    normal = normal.Normalized();
                    if (normal.Z < 0)
                    {
                        normal = -normal;
                    }

                    foreach ((int u, int v) in new[] { (a, b), (b, c), (c, a) })
                    {
                        var key = (Math.Min(u, v), Math.Max(u, v));
                        if (faces.Remove(key, out Vector3d other))
                        {
                            angles.Add(Math.Acos(Math.Clamp(Vector3d.Dot(normal, other), -1.0, 1.0)) * 180.0 / Math.PI);
                        }
                        else
                        {
                            faces[key] = normal;
                        }
                    }
                }
            }

            angles.Sort();
            return angles[(int)((angles.Count - 1) * 0.99)];
        }

        static Vector3d P(Vector3f v) => new(v.X, v.Y, v.Z);
    }

    /// <summary>The chunks one Ultra frame draws on level 1, built with or without smoothing.</summary>
    private sealed record Scene(
        TerrainData Terrain,
        Camera3D Camera,
        TerrainView View,
        List<(TerrainChunk Chunk, TerrainChunkGeometry Geometry, bool Refined)> Built)
    {
        private static (TerrainData, TerrainSurface)? _level;

        public static Scene Build(bool smooth)
        {
            (TerrainData terrain, TerrainSurface surface) = _level ??= TerrainHeightSeamTests.Load(1);
            Camera3D camera = new(
                new Vector3d(5504.0 * WorldScene.UnitsPerMetre, -13696.0 * WorldScene.UnitsPerMetre, 42640.0 * WorldScene.UnitsPerMetre),
                Camera3D.LookRotation(0.8f, -0.3f));
            TerrainView view = TerrainView.FromPerspective(camera, 1.2f, 16f / 9f, 450);
            List<TerrainChunk> chunks = [];
            TerrainTraversal.Collect(terrain.Octree, view, chunks, surface.Discards, 65536);

            TerrainSmoothing? field = smooth ? new TerrainSmoothing(terrain, 4, 0.0, Limit) : null;
            List<(TerrainChunk, TerrainChunkGeometry, bool)> built = [];
            foreach (TerrainChunk chunk in chunks)
            {
                if (!terrain.TryGetBlock(chunk, out TerrainCommandBlock block)
                    || TerrainChunkMesh.Build(chunk, block, surface, 1f, surface.Heights.Resolve(chunk), null, default, field) is not { } geometry)
                {
                    continue;
                }

                built.Add((chunk, geometry, field is not null && geometry.VerticesPerPass > block.VertexCount));
            }

            return new Scene(terrain, camera, view, built);
        }
    }
}
