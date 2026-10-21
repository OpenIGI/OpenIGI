using OpenIGI.Game.Physics;
using OpenIGI.Engine.Flow;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using OpenIGI.Formats.World;
using OpenIGI.Scripting.Runtime;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using static OpenIGI.Game.Tests.RetailMissionFixture;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The outro cutscenes and the trucks that drive in them: Mission 1's outro and its truck's route out
/// through the gate, and Mission 2's truck on the same model.
/// </summary>
public sealed class RetailCutsceneOutroTests
{
    [RequiresGameDataFact]
    public void MissionOneOutroTakesCameraControlAndCompletesLevelFlow()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(1);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        // Leave the intro through the same native edge the Escape key uses, then trigger the authored
        // mission-success variable that registers ConditionalContainer 4007.
        runtime.RequestBreak();
        Step(objectives, runtime);
        Step(objectives, runtime);
        Assert.False(runtime.IsPlaying);
        Assert.True(loop.Variables.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
        Step(objectives, runtime);

        Assert.Equal(1204, runtime.ActiveScene?.Id);
        Assert.NotNull(runtime.Frame);
        Assert.True(Walk(root).OfType<ConditionalContainerTask>().Single(c => c.Id == 4007).IsRun);
        Assert.False(Walk(root).OfType<ConditionalContainerTask>().Single(c => c.Id == 1495).IsRun);

        for (int tick = 0; tick < 20_000 && runtime.IsPlaying; tick++)
        {
            Step(objectives, runtime);
        }

        Assert.False(runtime.IsPlaying);
        Assert.True(Walk(root).OfType<CutSceneTask>().Single(scene => scene.Id == 1204).IsFinished);
        Assert.True(objectives.IsComplete);

        static void Step(MissionObjectiveRuntime objectives, CutsceneRuntime runtime)
        {
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }
    }

    [RequiresGameDataFact]
    public void MissionOneOutroTruckRouteProbe()
    {
        TruckRouteResult closedThenOpen = RunMissionOneTruckRoute(gateStartsOpen: false);
        TruckRouteResult alreadyOpen = RunMissionOneTruckRoute(gateStartsOpen: true);

        Vector3d retailStart = new(
            24_956_676.0 / WorldScene.UnitsPerMetre,
            -56_086_240.0 / WorldScene.UnitsPerMetre,
            174_414_610.0 / WorldScene.UnitsPerMetre);
        Assert.InRange((closedThenOpen.InitialPosition - retailStart).Length, 0d, 0.001d);
        Assert.InRange((alreadyOpen.InitialPosition - retailStart).Length, 0d, 0.001d);

        Assert.True(closedThenOpen.OpenCommandIssued,
            "the authored gate-open moment was not reached");
        Assert.True(closedThenOpen.Gate.State.IsFullyOpen,
            "the authored gate never reached the live fully-open state");
        Assert.Equal(closedThenOpen.Gate.Position, alreadyOpen.Gate.Position);

        AssertAuthoredRightExit(closedThenOpen);
        AssertAuthoredRightExit(alreadyOpen);
    }

    /// <summary>
    /// Mission 2 reuses the Mission 1 truck's model for a different inbound route. The obstacle
    /// probe is now taken from the car's own authored dimensions rather than from a table keyed on
    /// the Mission 1 task, so every car sweeps for obstacles and no level-specific route survives.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionTwoTruckSharingTheModelSweepsWithItsOwnDimensions()
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        _ = fixture.Load(2);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        int liveRouteProbes = 0;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            vehicleObstacleHit: (_, _, _) =>
            {
                liveRouteProbes++;
                return null;
            },
            vehicleObstacleReleased: _ => false);
        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(778));
        Assert.Equal("Car which Jones drives into base", truck.Label);
        Assert.True(truck.TryGetValue(WorldScene.ModelParameter, out object? model));
        Assert.Equal("622_01_1", model);
        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d before, out _));

        for (int tick = 0; tick < 60; tick++)
        {
            runtime.Tick();
        }

        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d after, out _));
        Assert.True((after - before).Length > 0.1d, "the Mission 2 truck did not run its authored cutscene movement");

        // The probe is generic: it runs for this truck too, driven by its own body length, and is
        // no longer gated on the Mission 1 label and model.
        Assert.True(liveRouteProbes > 0, "the Mission 2 truck never swept for obstacles");
    }

    private static TruckRouteResult RunMissionOneTruckRoute(bool gateStartsOpen)
    {
        RetailMissionFixture fixture = RetailMissionFixture.Open();
        GameLoop loop = fixture.Loop;
        LevelTask root = fixture.Root;
        ResourceRegistry resources = fixture.Resources;
        MagicObjectRegistry magic = fixture.LoadMagic();
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        LoadedMission mission = fixture.Load(1);
        WorldScene world = WorldScene.Build(mission, root, magic);
        DoorRegistry doors = DoorRegistry.Build(root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);

        new ConditionalContainerGate(loop.Variables).Prepare(root);
        DeclaredTask truck = Assert.IsType<DeclaredTask>(root.Tree.FromId(1099));
        Assert.Equal("Truck which Jones will steal", truck.Label);
        Assert.True(truck.TryGetValue(WorldScene.ModelParameter, out object? model));
        Assert.Equal("622_01_1", model);

        PlacedDoor gate = Assert.Single(doors.Doors, door => door.Task.Id == 401);
        Assert.Equal(new Vector3d(24_979_312.0, -56_202_112.0, 174_413_120.0), gate.Position);
        if (gateStartsOpen)
        {
            gate.State.CommandOpen();
            for (int tick = 0; tick < 300 && !gate.State.IsFullyOpen; tick++)
            {
                doors.Tick();
                RefreshDoorCollision(solids, doors);
            }
            Assert.True(gate.State.IsFullyOpen, "the authored gate did not reach its open state");
        }

        bool openCommandIssued = false;
        CutsceneRuntime runtime = new(
            root,
            loop.Variables,
            vehiclePhysics: physics,
            vehicleObstacleHit: (task, origin, end) =>
                Contact(solids, task, origin, end),
            vehicleObstacleReleased: obstacle => doors.Doors
                .FirstOrDefault(door => ReferenceEquals(door.Task, obstacle))?.State.IsFullyOpen == true);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        RefreshCutsceneCollision(solids, world, runtime);
        RefreshDoorCollision(solids, doors);
        AssertGateCollisionState(solids, gate, expected: !gateStartsOpen);

        runtime.RequestBreak();
        Step(objectives, runtime, solids, world, doors, gate, ref openCommandIssued);
        Assert.True(loop.Variables.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
        Step(objectives, runtime, solids, world, doors, gate, ref openCommandIssued);
        Assert.Equal(1204, runtime.ActiveScene?.Id);
        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d initialPosition, out Matrix3f initialOrientation));

        for (int tick = 0; tick < 600 && runtime.ActiveScene is not null; tick++)
        {
            Step(objectives, runtime, solids, world, doors, gate, ref openCommandIssued);
        }

        Assert.True(runtime.TryGetDrawTransform(truck, out Vector3d finalPosition, out Matrix3f finalOrientation));
        AssertGateCollisionState(solids, gate, expected: false);
        return new TruckRouteResult(
            initialPosition,
            initialOrientation.Column2,
            finalPosition,
            finalOrientation.Column2,
            gate,
            openCommandIssued);

        static (GameTask Owner, WorldRayHit Hit, string Label)? Contact(
            ObjectCollisionIndex solids,
            GameTask ignoredOwner,
            Vector3d origin,
            Vector3d end)
        {
            if (!solids.TryCastIgnoring(ignoredOwner, origin, end, out WorldRayHit hit, out GameTask? owner, out string label)
                || owner is null)
            {
                return null;
            }

            return (owner, hit, label);
        }

        static void Step(
            MissionObjectiveRuntime objectives,
            CutsceneRuntime runtime,
            ObjectCollisionIndex solids,
            WorldScene world,
            DoorRegistry doors,
            PlacedDoor gate,
            ref bool openCommandIssued)
        {
            objectives.TickLevelState();
            runtime.Tick();
            RefreshCutsceneCollision(solids, world, runtime);
            if (runtime.ActiveScene?.Ticks >= 11 * CutsceneRuntime.Frequency
                && !gate.State.IsFullyOpen
                && !openCommandIssued)
            {
                gate.State.CommandOpen();
                openCommandIssued = true;
            }
            doors.Tick();
            RefreshDoorCollision(solids, doors);
            objectives.TickAfterLevelState();
            runtime.FinishExpressionTick();
        }
    }

    private static void RefreshCutsceneCollision(ObjectCollisionIndex solids, WorldScene world, CutsceneRuntime runtime)
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
    }

    private static void RefreshDoorCollision(ObjectCollisionIndex solids, DoorRegistry doors)
    {
        foreach (PlacedDoor door in doors.Doors)
        {
            solids.SetPassable(door.Task, door.State.IsFullyOpen);
            solids.UpdateTransform(door.Task, door.DrawPosition, door.DrawOrientation);
        }
    }

    private static void AssertGateCollisionState(
        ObjectCollisionIndex solids,
        PlacedDoor gate,
        bool expected)
    {
        Vector3d centre = gate.Position;
        bool hitGate = false;
        for (double x = -8d; x <= 8d && !hitGate; x += 1d)
        {
            for (double y = -8d; y <= 8d && !hitGate; y += 1d)
            {
                for (double z = 0.25d; z <= 4d; z += 0.5d)
                {
                    Vector3d point = centre + new Vector3d(x, y, z) * WorldScene.UnitsPerMetre;
                    if (solids.IsPointInTaskVolume(point, gate.Task))
                    {
                        hitGate = true;
                        break;
                    }
                }
            }
        }

        Assert.Equal(expected, hitGate);
    }

    private static void AssertAuthoredRightExit(TruckRouteResult route)
    {
        float turnCross = (route.InitialForward.X * route.FinalForward.Y)
            - (route.InitialForward.Y * route.FinalForward.X);
        double travelled = (route.FinalPosition - route.InitialPosition).Length;
        Assert.True(travelled > 10d,
            $"truck did not leave its authored garage route: start={route.InitialPosition}, end={route.FinalPosition}");
        Assert.True(turnCross > 0f && route.FinalForward.X > 0f,
            $"truck did not take the authored right exit: initial={route.InitialForward}, final={route.FinalForward}, cross={turnCross}");
    }

    private sealed record TruckRouteResult(
        Vector3d InitialPosition,
        Vector3f InitialForward,
        Vector3d FinalPosition,
        Vector3f FinalForward,
        PlacedDoor Gate,
        bool OpenCommandIssued);
}
