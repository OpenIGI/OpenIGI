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
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 8's opening cutscene trucks, 1501 and 1507, against the original's physics bit for bit.
/// </summary>
/// <remarks>
/// <para>
/// Captured with the probe's <c>cutscene</c> hook set (<c>IGI_PROBE_CAR</c>, intro not skipped): each row is the
/// truck's <c>+32</c> position as <c>Car_Tick</c> (<c>0x42ECA0</c>) is entered while the scene's live <c>nTick</c>
/// reads the tick given -- the state the previous step left, which the port holds once that tick's walk is done.
/// </para>
/// <para>
/// Two defects kept these trucks off the original. Truck 1501's AnimTask runs from the level's first walk, so its
/// set-up packet arrived while <see cref="CutsceneRuntime"/> was being constructed, before the host supplied the
/// car world: it never got a retail body and drove the planar model into the scenery. And every AnimTask's first
/// running walk sends counter 0 again (<c>0x4F3EF0</c> steps, <c>0x4F4060</c> sends the counter, then it counts),
/// so the original steps 1501 at nTick 1..749 and 1507 from 947; the port had spent that walk in its constructor
/// and stepped both a walk early, 1501 once past its <c>nTick &lt; 750</c> cut-off.
/// </para>
/// </remarks>
public sealed class MissionEightIntroTruckTests(ITestOutputHelper output)
{
    /// <summary>Truck 1501 in the original: the scene tick, then its position's bits.</summary>
    private static readonly (int Tick, ulong X, ulong Y, ulong Z)[] RetailTruck1501 =
    [
        (1, 0x4185BCDA60000000UL, 0x41A1545E00000000UL, 0x41A4E55540000000UL),
        (3, 0x4185BCDA60000000UL, 0x41A1545E00000000UL, 0x41A4E55540000000UL),
        (150, 0x4185BCDA0B7DAAB1UL, 0x41A1545DB2E5A873UL, 0x41A4E556A55AAF80UL),
        (375, 0x4185BD30447F34F1UL, 0x41A1549005CE7BF3UL, 0x41A4E556B464D0BFUL),
        (600, 0x4185C1FE632464F1UL, 0x41A154FC34302D82UL, 0x41A4E556AABF89B9UL),
        (749, 0x4185C5F5EC2A5CF1UL, 0x41A155113848884AUL, 0x41A4E556AA69F5F5UL),
    ];

    /// <summary>Truck 1507 in the original, from the walk its AnimTask first runs.</summary>
    private static readonly (int Tick, ulong X, ulong Y, ulong Z)[] RetailTruck1507 =
    [
        (947, 0x4185E6C060000000UL, 0x41A1569700000000UL, 0x41A4E55540000000UL),
        (948, 0x4185E6C060000000UL, 0x41A1569700000000UL, 0x41A4E55540000000UL),
        (949, 0x4185E6C18B058600UL, 0x41A1569727002740UL, 0x41A4E55573095B40UL),
        (1100, 0x4185EAD11D59EA00UL, 0x41A15754198F8A00UL, 0x41A4E5572B6810ABUL),
        (1300, 0x4185EC253A8D3996UL, 0x41A1588E0047A600UL, 0x41A4E5578E286073UL),
        (1500, 0x4185EBEDAE1B0336UL, 0x41A159BE7356EE00UL, 0x41A4E55DB2E82207UL),
        (1590, 0x4185EBE4EE7E6E46UL, 0x41A159FDF0D88780UL, 0x41A4E55CF2247F4FUL),
    ];

    [RequiresGameDataFact]
    public void BothIntroTrucksDriveTheOriginalsPathBitForBit()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(8);
        WorldScene world = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        TerrainData terrain = mission.Terrain ?? throw new InvalidOperationException("mission 8 has no terrain");

        // As GameHost builds it: the runtime first, then the wheels, then the retail car world.
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics, terrain: terrain)
        {
            CollisionIndex = solids,
        };
        runtime.BindVehicleWheels(world);
        Func<string, MefModel?> models = name => mission.TryGetModel(name, out MefModel? model) ? model : null;
        runtime.ConfigureRetailCars(
            new RetailCarWorld(
                new RetailTerrainRay(
                    terrain.Octree,
                    terrain.Commands,
                    TerrainDiscardBinding.For(terrain).Regions,
                    TerrainHeightBindings.Bind(terrain, root)),
                RetailWorldObjects.Build(root, models, name => magic.TryGet(name, out MagicObjectDefinition attached) ? attached : null),
                runtime.RetailVehicleModels,
                static owner => owner is not GameTask task || ConditionalContainerGate.IsTaskActive(task)),
            models,
            name => magic.TryGet(name, out MagicObjectDefinition attached) ? attached : null);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        GameTask truck1501 = root.Tree.FromId(1501)!;
        GameTask truck1507 = root.Tree.FromId(1507)!;
        Dictionary<int, Vector3d> path1501 = [];
        Dictionary<int, Vector3d> path1507 = [];
        for (int walk = 0; walk < 1591; walk++)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
            Assert.True(loop.Variables.TryRead("CutScene_1502.nTick", out QvmValue nTick));
            int tick = nTick.ToInt32();
            if (runtime.TryGetDrawTransform(truck1501, out Vector3d at1501, out _))
            {
                path1501.TryAdd(tick, at1501 * WorldScene.UnitsPerMetre);
            }

            if (runtime.TryGetDrawTransform(truck1507, out Vector3d at1507, out _))
            {
                path1507.TryAdd(tick, at1507 * WorldScene.UnitsPerMetre);
            }
        }

        AssertBitExact("1501", RetailTruck1501, path1501);
        AssertBitExact("1507", RetailTruck1507, path1507);

        // 1501's last step runs at nTick 749; the walk that reads 750 sends nothing, so the truck stands from there.
        Assert.NotEqual(path1501[749], path1501[750]);
        for (int tick = 751; tick <= 800; tick++)
        {
            Assert.Equal(path1501[750], path1501[tick]);
        }
    }

    private void AssertBitExact(string name, (int Tick, ulong X, ulong Y, ulong Z)[] retail, Dictionary<int, Vector3d> path)
    {
        foreach ((int tick, ulong x, ulong y, ulong z) in retail)
        {
            Assert.True(path.TryGetValue(tick, out Vector3d ours), $"truck {name}: no pose at nTick {tick}");
            Vector3d expected = new(BitConverter.UInt64BitsToDouble(x), BitConverter.UInt64BitsToDouble(y), BitConverter.UInt64BitsToDouble(z));
            output.WriteLine($"truck {name} nTick {tick,4}: ours ({ours.X:R}, {ours.Y:R}, {ours.Z:R}) retail ({expected.X:R}, {expected.Y:R}, {expected.Z:R})");
            Assert.Equal(expected, ours);
        }
    }
}
