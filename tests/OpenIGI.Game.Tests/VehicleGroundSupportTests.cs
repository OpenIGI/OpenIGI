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
/// A car's suspension stands on placed geometry, not only on terrain.
/// </summary>
/// <remarks>
/// Mission 1's outro truck starts inside a garage whose floor (<c>410_02_1 GARAGE_INTERIOR</c>) sits
/// 0.169 m above the dirt underneath it. With only the terrain to probe, the springs settle the body
/// to the dirt and the wheels stand through the floor. The collision index used to be handed to the
/// cutscene runtime on Mission 4 alone, because setting it also re-seated the helicopters; those two
/// are now separate, so every mission gets object-aware ground.
/// </remarks>
public sealed class VehicleGroundSupportTests(ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void TheOutroTruckStandsOnTheGarageFloorRatherThanTheDirt()
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
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(1);
        WorldScene world = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(1099));

        // The two surfaces under the truck's starting position.
        WorldPlacement body = world.Placements.Single(
            placement => placement.Owner.Id == 1099 && placement.Instance.Name == "622_01_1");
        Vector3d probe = body.Position * WorldScene.UnitsPerMetre;
        Assert.True(
            TerrainHeightQuery.TryGetGround(
                mission.Terrain!,
                probe.X,
                probe.Y,
                probe.Z,
                TerrainHeightQuery.AboveRecoveryUnits,
                out TerrainGround ground),
            "no terrain under the truck");
        double dirt = ground.Height / WorldScene.UnitsPerMetre;

        Assert.True(
            solids.TryCastIgnoring(
                truck,
                new Vector3d(probe.X, probe.Y, probe.Z + 4096d),
                new Vector3d(probe.X, probe.Y, probe.Z - 262_144d),
                out WorldRayHit hit,
                out _,
                out string label),
            "no placed surface under the truck");
        double floor = hit.Position.Z / WorldScene.UnitsPerMetre;
        output.WriteLine($"terrain {dirt:F3} m, '{label}' {floor:F3} m, {floor - dirt:F3} m apart");
        Assert.True(floor > dirt + 0.1d, "this level no longer has a raised garage floor to stand on");

        double onFloor = Settle(withObjects: true);
        double onDirt = Settle(withObjects: false);
        output.WriteLine($"settled with objects {onFloor:F3} m, without {onDirt:F3} m");

        // Standing on the floor: nearer to it than to the dirt, and clear of the dirt by most of
        // the gap between them.
        Assert.True(
            onFloor - dirt > (floor - dirt) * 0.75d,
            $"the truck settled to the dirt at {onFloor:F3} m with the floor at {floor:F3} m");
        Assert.True(
            onFloor - onDirt > (floor - dirt) * 0.5d,
            $"the placed floor made no difference: {onFloor:F3} m against {onDirt:F3} m");

        double Settle(bool withObjects)
        {
            LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            _ = new MissionLoader(
                missions,
                fs,
                resources,
                new LevelLoader(levelRoot, loop.TaskCommands, loop.Natives, resources),
                terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(1);
            WorldScene scene = WorldScene.Build(mission, levelRoot, magic);
            new ConditionalContainerGate(loop.Variables).Prepare(levelRoot);
            DeclaredTask car = Assert.IsType<DeclaredTask>(levelRoot.Tree.FromId(1099));
            CutsceneRuntime runtime = new(
                levelRoot,
                loop.Variables,
                vehiclePhysics: physics,
                terrain: mission.Terrain);
            if (withObjects)
            {
                runtime.CollisionIndex = ObjectCollisionIndex.Build(scene.Placements);
            }

            runtime.BindVehicleWheels(
                scene,
                name => mission.TryGetModel(name, out MefModel? model) && model is not null
                    ? WorldScene.WheelRadius(model)
                    : 0f);
            MissionObjectiveRuntime objectives = new(levelRoot, loop.Variables, native: runtime.TryInvokeNative);

            runtime.RequestBreak();
            Step();
            Assert.True(loop.Variables.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
            Step();

            // Sixty ticks: settled on its springs, and still inside the garage.
            double height = double.NaN;
            for (int tick = 0; tick < 60; tick++)
            {
                Step();
                if (runtime.TryGetDrawTransform(car, out Vector3d position, out _))
                {
                    height = position.Z;
                }
            }

            Assert.False(double.IsNaN(height), "the truck never exposed a draw transform");
            return height;

            void Step()
            {
                objectives.TickLevelState();
                runtime.Tick();
                objectives.TickAfterLevelState();
                runtime.FinishExpressionTick();
            }
        }
    }
}
