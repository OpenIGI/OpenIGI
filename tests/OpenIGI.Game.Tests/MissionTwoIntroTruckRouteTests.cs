using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The Mission 2 intro truck against the route the retail game drives.
/// </summary>
/// <remarks>
/// Measured the same way as the Mission 1 outro: by hooking <c>Car::Tick</c> in the running original
/// and recording its position every tick. This one is the complement of that route -- the truck
/// enters already carrying its authored throttle of 0.6, coasts down as the authored channels latch
/// 0.25, then 0.125, then nothing, curves gently right, and PARKS. Where it comes to rest is the
/// point of the test: a car whose opening throttle is dropped never gets going, and one whose
/// throttle does not decay overshoots the spot the scene expects it to stop at.
/// </remarks>
public sealed class MissionTwoIntroTruckRouteTests(ITestOutputHelper output)
{
    /// <summary>Ticks into the intro, and where the retail truck is then, in metres.</summary>
    private static readonly (int Tick, double X, double Y)[] RetailRoute =
    [
        (0, 30802.65d, -9251.52d),
        (30, 30801.20d, -9248.75d),
        (60, 30799.53d, -9244.41d),
        (90, 30798.67d, -9240.16d),
        (120, 30798.39d, -9236.53d),
        (150, 30798.19d, -9234.11d),
        (180, 30798.14d, -9233.54d),
        (240, 30798.14d, -9233.54d),
        (297, 30798.14d, -9233.54d),
    ];

    private const double ToleranceMetres = 3d;

    [RequiresGameDataFact]
    public void IntroTruckDrivesTheRetailRouteAndParksWhereItShould()
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
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(2);
        WorldScene world = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        new ConditionalContainerGate(loop.Variables).Prepare(root);

        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(778));
        Assert.Equal("Car which Jones drives into base", truck.Label);

        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            vehiclePhysics: physics,
            terrain: mission.Terrain,
            vehicleObstacleHit: (task, origin, end) =>
                solids.TryCastIgnoring(task, origin, end, out WorldRayHit hit, out GameTask? owner, out string label)
                && owner is not null
                    ? (owner, hit, label)
                    : null,
            vehicleObstacleReleased: _ => false)
        {
            CollisionIndex = solids,
        };
        runtime.BindVehicleWheels(world);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        Dictionary<int, Vector3d> samples = [];
        int lastTick = RetailRoute[^1].Tick;
        for (int tick = 0; tick <= lastTick; tick++)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
            if (runtime.TryGetDrawTransform(truck, out Vector3d position, out _))
            {
                samples[tick] = position;
            }
        }

        double worst = 0d;
        List<string> failures = [];
        foreach ((int tick, double x, double y) in RetailRoute)
        {
            Assert.True(samples.TryGetValue(tick, out Vector3d position), $"no sample at tick {tick}");
            double error = Math.Sqrt(
                ((position.X - x) * (position.X - x)) + ((position.Y - y) * (position.Y - y)));
            worst = Math.Max(worst, error);
            output.WriteLine($"tick {tick,4}: ours ({position.X:F2}, {position.Y:F2}) "
                + $"retail ({x:F2}, {y:F2}) error {error:F2} m");
            if (error > ToleranceMetres)
            {
                failures.Add($"tick {tick}: {error:F2} m from the retail route");
            }
        }

        output.WriteLine($"worst {worst:F2} m");
        Assert.True(failures.Count == 0, string.Join("; ", failures));

        // Parked, and staying parked: the last three samples span two hundred ticks.
        double drift = Math.Sqrt(
            ((samples[297].X - samples[180].X) * (samples[297].X - samples[180].X))
            + ((samples[297].Y - samples[180].Y) * (samples[297].Y - samples[180].Y)));
        Assert.True(drift < 0.5d, $"the truck did not stay parked: it drifted {drift:F2} m");
    }
}
