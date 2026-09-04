using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The rendered terrain is watertight once the heightmap is applied: every column two chunks share
/// gets the same height from both, after the level-of-detail fade the shader applies.
/// </summary>
/// <remarks>
/// This reproduces the shader's vertex stage on the CPU — the ramp on the morph target, the Chebyshev
/// distance from the camera, the same offset and slope <c>sub_50FE60</c> computes — for a view from the
/// middle of level 1's second heightmap cell looking across its boundary with the third. That boundary
/// is where the far edge of one grid meets the near edge of the next, and where a sampler that treats
/// the far edge as outside its grid opens a crack of the full height difference.
/// </remarks>
public sealed class TerrainHeightSeamTests
{
    [RequiresGameDataFact]
    public void NoCrackOpensWhereTwoHeightmapCellsMeet()
    {
        (TerrainData terrain, TerrainSurface surface) = Load(1);
        Assert.False(surface.Heights.IsEmpty);

        Camera3D camera = new(
            new Vector3d(5504.0 * WorldScene.UnitsPerMetre, -13696.0 * WorldScene.UnitsPerMetre, 42640.0 * WorldScene.UnitsPerMetre),
            Camera3D.LookRotation(0.8f, -0.3f));
        TerrainView view = TerrainView.FromPerspective(camera, 1.2f, 16f / 9f);
        List<TerrainChunk> chunks = [];
        TerrainTraversal.Collect(terrain.Octree, view, chunks, surface.Discards);

        // sub_50FE60: the offset is truncated to an integer there.
        double fadeOffset = (long)((view.ViewDistance + 64) * 0.375);
        double fadeSlope = 2.6666667 / (view.ViewDistance - 64);

        Dictionary<(long, long), List<(int E, double Z)>> columns = [];
        int bound = 0;
        foreach (TerrainChunk chunk in chunks)
        {
            if (!terrain.TryGetBlock(chunk, out TerrainCommandBlock block))
            {
                continue;
            }

            TerrainHeightBinding heights = surface.Heights.Resolve(chunk);
            if (heights.IsBound)
            {
                bound++;
            }

            if (TerrainChunkMesh.Build(chunk, block, surface, 1f, heights) is not { } geometry)
            {
                continue;
            }

            double scale = 0.375 / (1 << (25 - chunk.LodExponent));
            for (int i = 0; i < block.VertexCount; i++)
            {
                Vertex3D v = geometry.Vertices[i];
                double wx = chunk.Position.X + v.Position.X;
                double wy = chunk.Position.Y + v.Position.Y;
                double wz = chunk.Position.Z + v.Position.Z;

                double tx = wx + v.Normal.X - camera.Position.X;
                double ty = wy + v.Normal.Y - camera.Position.Y;
                double tz = wz + v.Normal.Z - camera.Position.Z;
                double reach = Math.Max(Math.Abs(tx), Math.Max(Math.Abs(ty), Math.Abs(tz)));
                double t = Math.Clamp((reach * scale - fadeOffset) * fadeSlope, 0.0, 1.0);

                var key = ((long)Math.Round(wx / 64.0), (long)Math.Round(wy / 64.0));
                if (!columns.TryGetValue(key, out List<(int, double)>? list))
                {
                    columns[key] = list = [];
                }

                list.Add((chunk.LodExponent, wz + (v.Normal.Z * t)));
            }
        }

        Assert.True(bound > 100, $"only {bound} chunks bound");

        // The boundary between the second and third cells, x = 23068672, must be in view and shared.
        const long BoundaryX = 23068672 / 64;
        int onBoundary = columns.Count(c => c.Key.Item1 == BoundaryX && c.Value.Count > 1);
        Assert.True(onBoundary > 5, $"only {onBoundary} shared columns on the cell boundary");

        List<string> cracks = [];
        int shared = 0;
        foreach (((long x, long y), List<(int E, double Z)> list) in columns)
        {
            if (list.Count < 2)
            {
                continue;
            }

            shared++;
            double gap = list.Max(v => v.Z) - list.Min(v => v.Z);
            if (gap > 0.05 * WorldScene.UnitsPerMetre)
            {
                cracks.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"({x * 64},{y * 64}) gap {gap / WorldScene.UnitsPerMetre:F2} m between {string.Join(", ", list.Select(v => $"e{v.E}"))}"));
            }
        }

        Assert.True(shared > 1000, $"only {shared} shared columns");
        Assert.True(cracks.Count == 0, $"{cracks.Count} cracks of {shared} shared columns:\n{string.Join("\n", cracks.Take(10))}");
    }

    private static (TerrainData, TerrainSurface) Load(int id)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        PlayerTaskTypes.RegisterAll(loop.Types);
        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader, null, terrainLoader).Load(id);
        Assert.NotNull(mission.Terrain);
        return (mission.Terrain, TerrainSurface.Build(mission.Terrain, levelRoot));
    }
}
