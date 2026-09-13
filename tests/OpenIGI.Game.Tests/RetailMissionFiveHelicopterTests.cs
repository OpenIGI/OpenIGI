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

namespace OpenIGI.Game.Tests;

/// <summary>Regression coverage for Mission 5's authored extraction Bell.</summary>
public sealed class RetailMissionFiveHelicopterTests
{
    [RequiresGameDataFact]
    public void ExtractionBellTouchesDownAtTheAuthoredHelipadAndRemainsThere()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry vehiclePhysics = new();
        vehiclePhysics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(5);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(
            root, loop.Variables, terrain: mission.Terrain, vehiclePhysics: vehiclePhysics)
        {
            CollisionIndex = ObjectCollisionIndex.Build(scene.Placements),
        };

        GameTask extractionBell = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(3322));
        // LEVEL5/OBJECTS.QVM replaces the incoming Bell with this parked Bell when
        // AnimTask 3333 passes tick 1530, making its authored position the retail touchdown pose.
        DeclaredTask parkedBell = Assert.IsType<DeclaredTask>(root.Tree.FromId(1234));
        Assert.True(WorldScene.TryGetPosition(parkedBell, out Vector3d authoredTouchdown));
        runtime.EnableTerminalTerrainLanding(extractionBell, parkedBell);
        Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(1002)).Condition = "1";
        runtime.Tick();
        runtime.FinishExpressionTick();

        bool landed = false;
        Vector3d touchdown = default;
        int diagnosticsSeen = runtime.Diagnostics.Count;
        for (int tick = 0; tick < 8_000; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
            if (!runtime.TryGetDrawTransform(extractionBell, out Vector3d position, out _))
            {
                continue;
            }

            bool landingThisTick = runtime.Diagnostics
                .Skip(diagnosticsSeen)
                .Any(line => line.Contains("heli-terminal-latch id=3322", StringComparison.Ordinal));
            diagnosticsSeen = runtime.Diagnostics.Count;
            if (landingThisTick)
            {
                landed = true;
                touchdown = position;
                break;
            }
        }

        Assert.True(landed, "extraction Bell 3322 never reached its authored terminal landing");
        double horizontalMiss = Math.Sqrt(
            ((touchdown.X - authoredTouchdown.X) * (touchdown.X - authoredTouchdown.X))
            + ((touchdown.Y - authoredTouchdown.Y) * (touchdown.Y - authoredTouchdown.Y)));
        Assert.Equal(0d, horizontalMiss);
        for (int tick = 0; tick < 500; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
        }

        Assert.True(runtime.TryGetDrawTransform(extractionBell, out Vector3d settled, out _));
        double horizontalDrift = Math.Sqrt(
            ((settled.X - touchdown.X) * (settled.X - touchdown.X))
            + ((settled.Y - touchdown.Y) * (settled.Y - touchdown.Y)));
        Assert.True(horizontalDrift < 2.0,
            $"extraction Bell drifted {horizontalDrift:F2}m after landing: {touchdown} -> {settled}");
        Assert.True(Math.Abs(settled.Z - touchdown.Z) < 1.0,
            $"extraction Bell did not remain at touchdown: {touchdown} -> {settled}");
    }
}
