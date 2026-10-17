using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>Checks that a round striking open ground names the level's own terrain material.</summary>
/// <remarks>
/// <c>0x4CB050</c> names a terrain hit's material from the <c>CreateTerrainMaterial</c> record, so it is
/// the level that decides — <c>Ground</c> in most missions and <c>Snow</c> in the four Arctic ones — not
/// a single row standing in for every map.
/// </remarks>
public sealed class RetailTerrainImpactMaterialTests
{
    [RequiresGameDataTheory]
    [InlineData(1, "Ground")]
    [InlineData(2, "Ground")]
    [InlineData(3, "Ground")]
    [InlineData(4, "Ground")]
    [InlineData(5, "Ground")]
    [InlineData(6, "Ground")]
    [InlineData(7, "Snow")]
    [InlineData(8, "Snow")]
    [InlineData(9, "Ground")]
    [InlineData(10, "Ground")]
    [InlineData(11, "Snow")]
    [InlineData(12, "Snow")]
    [InlineData(13, "Ground")]
    [InlineData(14, "Ground")]
    public void TerrainNamesTheLevelsOwnMaterialRow(int missionId, string expected)
    {
        (LoadedMission mission, GameMaterialRegistry materials, _) = Load(missionId);
        Assert.NotNull(mission.Terrain);

        Assert.True(materials.TryGet(TerrainRayTrace.GameMaterialOf(mission.Terrain), out GameMaterial row));
        Assert.Equal(expected, row.Name);
    }

    [RequiresGameDataFact]
    public void ASnowTerrainTraceNamesSnowAndLeavesNoMark()
    {
        (LoadedMission mission, GameMaterialRegistry materials, WorldScene scene) = Load(7);
        TerrainData terrain = mission.Terrain!;
        Assert.True(materials.TryGetByName("Snow", out GameMaterial snow));

        foreach (WorldPlacement placement in scene.Placements)
        {
            double x = placement.Position.X * WorldScene.UnitsPerMetre;
            double y = placement.Position.Y * WorldScene.UnitsPerMetre;
            double z = placement.Position.Z * WorldScene.UnitsPerMetre;
            if (!TerrainHeightQuery.TryGetGround(terrain, x, y, z + 8192.0, out TerrainGround ground))
            {
                continue;
            }

            Vector3d from = new(x, y, ground.Height + 4096.0);
            Vector3d to = new(x, y, ground.Height - 4096.0);
            if (!TerrainRayTrace.TryTrace(
                    terrain,
                    new WorldTraceRequest(from, to, WorldTraceMask.Terrain),
                    out WorldTraceHit hit))
            {
                continue;
            }

            Assert.Equal(snow.Index, hit.Material);

            // The row decides the sound and the dust, but nothing is stuck to the ground: the terrain
            // pass reports no object for the decal message to reach (0x4CC2A0), so a round into snow
            // leaves no hole however the Snow row is authored.
            Assert.Null(BulletMarks.SurfaceOf(hit));
            BulletMarks marks = new();
            WorldRayHit impact = new(hit.Position, hit.Normal, hit.Distance, hit.Material);
            Assert.False(marks.TryAdd(impact, BulletMarks.SurfaceOf(hit), materials));
            Assert.Empty(marks.Marks);

            return;
        }

        Assert.Fail("no placement in mission 7 stands over terrain a trace could reach");
    }

    private static (LoadedMission Mission, GameMaterialRegistry Materials, WorldScene Scene) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        resources.Register(
            GameMaterialRegistry.ScriptResource,
            fileSystem.ReadAllBytes(GameMaterialRegistry.ScriptResource));

        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader, null, terrainLoader)
            .Load(missionId);

        return (mission, materials, WorldScene.Build(mission, levelRoot, magic));
    }
}
