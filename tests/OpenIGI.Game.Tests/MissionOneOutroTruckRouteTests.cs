using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The Mission 1 outro truck against the route the retail game drives.
/// </summary>
/// <remarks>
/// The waypoints below were measured by hooking <c>Car::Tick</c> in the running original and
/// recording its position every tick through the whole outro a probe of the running original, IGI_PROBE_HOOKS=car).
/// They are observations of behaviour, not anything taken out of the original's code. The route is
/// a long S: south out of the yard, a swing west, then a sweep back east through the gate pair at
/// (6098.96, -13721.71) and away south. Nothing about it is authored as a path -- it falls out of
/// the authored steering and throttle running through the car force model -- so it is a strong
/// end-to-end check on the whole chain: the physics record, the channels, and the dynamics.
/// </remarks>
public sealed class MissionOneOutroTruckRouteTests(ITestOutputHelper output)
{
    /// <summary>Ticks into the outro, and where the retail truck is then, in metres.</summary>
    private static readonly (int Tick, double X, double Y)[] RetailRoute =
    [
        (0, 6092.94d, -13692.93d),
        (100, 6092.95d, -13694.66d),
        (200, 6092.51d, -13702.79d),
        (250, 6088.72d, -13706.88d),
        (300, 6086.59d, -13712.09d),
        (350, 6089.05d, -13717.23d),
        (400, 6093.76d, -13720.98d),
        (450, 6099.34d, -13723.68d),
        (500, 6104.71d, -13726.67d),
        (550, 6106.43d, -13732.00d),
        (600, 6106.42d, -13738.47d),
        (700, 6106.46d, -13756.64d),
    ];

    /// <summary>How far from the retail position any sample is allowed to sit.</summary>
    private const double ToleranceMetres = 4d;

    [RequiresGameDataFact]
    public void OutroTruckDrivesTheRetailRouteThroughTheGate()
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
        LoadedMission mission = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        WorldScene world = WorldScene.Build(mission, root, magic);
        DoorRegistry doors = DoorRegistry.Build(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        new ConditionalContainerGate(loop.Variables).Prepare(root);

        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(1099));
        Assert.Equal("Truck which Jones will steal", truck.Label);

        // 304_01_1 FENCE_GATES is a pair, and the level opens both leaves. Opening only one leaves
        // the other standing in the truck's path, which stops it dead on the gate.
        PlacedDoor[] gates = doors.Doors.Where(door => door.Task.Id is 400 or 401).ToArray();
        Assert.Equal(2, gates.Length);

        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            vehiclePhysics: physics,
            vehicleObstacleHit: (task, origin, end) =>
                solids.TryCastIgnoring(task, origin, end, out WorldRayHit hit, out GameTask? owner, out string label)
                && owner is not null
                    ? (owner, hit, label)
                    : null,
            vehicleObstacleReleased: obstacle => doors.Doors
                .FirstOrDefault(door => ReferenceEquals(door.Task, obstacle))?.State.IsFullyOpen == true);
        runtime.BindVehicleWheels(world);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        Refresh();

        bool opened = false;
        runtime.RequestBreak();
        Step();
        Assert.True(loop.Variables.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
        Step();
        Assert.Equal(1204, runtime.ActiveScene?.Id);

        Dictionary<int, Vector3d> samples = [];
        double closestToGate = double.MaxValue;
        Vector3d gatePosition = new(6098.96d, -13721.71d, 0d);
        int lastTick = RetailRoute[^1].Tick;
        for (int tick = 0; tick <= lastTick; tick++)
        {
            Step();
            if (!runtime.TryGetDrawTransform(truck, out Vector3d position, out _))
            {
                continue;
            }

            samples[tick] = position;
            closestToGate = Math.Min(
                closestToGate,
                Math.Sqrt(
                    ((position.X - gatePosition.X) * (position.X - gatePosition.X))
                    + ((position.Y - gatePosition.Y) * (position.Y - gatePosition.Y))));
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

        output.WriteLine($"worst {worst:F2} m; closest approach to the gate {closestToGate:F2} m");
        Assert.True(failures.Count == 0, string.Join("; ", failures));

        // The whole point of the outro: the truck leaves through the gate rather than stalling in
        // the yard or ploughing into the perimeter fence beside it.
        Assert.True(closestToGate < 3d, $"the truck never reached its gate: closest {closestToGate:F2} m");

        void Step()
        {
            objectives.TickLevelState();
            runtime.Tick();
            Refresh();
            if (runtime.ActiveScene?.Ticks >= 11 * CutsceneRuntime.Frequency && !opened)
            {
                foreach (PlacedDoor leaf in gates)
                {
                    leaf.State.CommandOpen();
                }

                opened = true;
            }

            doors.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
            Refresh();
        }

        void Refresh()
        {
            foreach (WorldPlacement placement in world.Placements)
            {
                if (runtime.TryGetPlacementDrawTransform(
                        placement.Owner,
                        placement.Position,
                        placement.Orientation,
                        out Vector3d position,
                        out Matrix3f orientation))
                {
                    solids.UpdateTransform(placement, position, orientation);
                }
            }

            foreach (PlacedDoor door in doors.Doors)
            {
                solids.SetPassable(door.Task, door.State.IsFullyOpen);
                solids.UpdateTransform(door.Task, door.DrawPosition, door.DrawOrientation);
            }
        }
    }
}
