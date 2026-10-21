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
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

public sealed class RetailElevatorTests
{
    [RequiresGameDataFact]
    public void Mission5DepartureLandingDoorStaysClosedWhenTheElevatorStartsMoving()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(5);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, doors, variables, elevators);
        PlacedElevator elevator = Assert.Single(elevators.Elevators, item => item.Task.Id == 51);
        PlacedDoor departureDoor = Assert.Single(doors.Doors, item => item.Task.Id == 45);
        elevators.SetPlayerPositionProvider(() => elevator.State.Position);

        // Door_45 is the floor-zero landing door. Its retail open expression is
        // "Elevator_51.vFloor == 0"; Switch_49 is the button inside the car.
        for (int tick = 0; tick < 60; tick++)
        {
            elevators.Tick();
            interactions.Tick();
            doors.Tick();
        }

        Assert.True(departureDoor.State.IsFullyOpen);
        Assert.True(interactions.ActivateFromAi(49));

        bool travelled = false;
        for (int tick = 0; tick < 10000; tick++)
        {
            elevators.Tick();
            interactions.Tick();
            doors.Tick();
            if (elevator.State.TransformChangedThisTick)
            {
                travelled = true;
                Assert.True(departureDoor.State.IsFullyClosed,
                    "Mission 5's departure landing door reopened as the car began moving");
            }

            if (travelled
                && !elevator.State.HasMovementRequest
                && !elevator.State.TransformChangedThisTick)
            {
                break;
            }
        }

        Assert.True(travelled, "Mission 5's cabin switch did not start the elevator");
        Assert.Equal(1, elevator.State.CurrentFloorIndex);
        Assert.True(departureDoor.State.IsFullyClosed);
    }

    [RequiresGameDataFact]
    public void AuthoredElevatorsExposeSplineFloorsAndLiveScriptVariables()
    {
        int liftCount = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, _) = Load(missionId);
            ElevatorRegistry registry = ElevatorRegistry.Build(root, variables);

            liftCount += registry.Elevators.Count;
            foreach (PlacedElevator elevator in registry.Elevators)
            {
                Assert.NotEmpty(elevator.Model);
                Assert.False(elevator.State.IsMoving);
                Assert.InRange(elevator.State.CurrentFloorIndex, 0, elevator.State.FloorCount - 1);
                Assert.Contains($"{elevator.Task.Name}.vFloor", variables.GetBoundNames(elevator.Task));
                Assert.Equal(OpenIGI.Scripting.Runtime.QvmValueKind.Float,
                    variables.GetValueKind($"{elevator.Task.Name}.vFloor"));
                Assert.Contains($"{elevator.Task.Name}.vLastFloor", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.nWantedFloor", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.isWantedFloor0", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.isMoving", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.isStart", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.isStop", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.nInactive", variables.GetBoundNames(elevator.Task));
                Assert.True(elevator.State.FloorCount > 0);
            }
        }

        Assert.True(liftCount > 0, "no Elevator tasks were found in the retail campaign");
    }

    [RequiresGameDataFact]
    public void A_lift_runs_along_its_spline_segments_lengths()
    {
        // Mission 3's Elevator_698 rides a spline whose segments are not Linear: its one segment is the cubic leaving each
        // waypoint level, sampled 20 times (0x4E4530), longer than the shaft it climbs.
        (LevelTask root, ScriptVarRegistry variables, _) = Load(3);
        PlacedElevator bent = Assert.Single(ElevatorRegistry.Build(root, variables).Elevators, item => item.Task.Id == 698);
        ElevatorFloor top = bent.State.Floors[1];
        double rise = Vector3d.Distance(bent.State.Floors[0].Position, top.Position);
        Assert.InRange(top.SegmentLength!.Value / rise, 1.13, 1.15);

        // Mission 14's Elevator_1701 rides Linear Segments: each is its straight run.
        (root, variables, _) = Load(14);
        PlacedElevator straight = Assert.Single(ElevatorRegistry.Build(root, variables).Elevators, item => item.Task.Id == 1701);
        for (int i = 1; i < straight.State.FloorCount; i++)
        {
            double run = Vector3d.Distance(straight.State.Floors[i - 1].Position, straight.State.Floors[i].Position);
            Assert.Equal((float)run, straight.State.Floors[i].SegmentLength!.Value, 0.5);
        }
    }

    [RequiresGameDataFact]
    public void ElevatorSwitchesOfferFromTheirOrientedFront()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables, elevators);

        bool found = false;
        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            foreach (DeclaredTask switchTask in Descendants(elevator.Task)
                         .OfType<DeclaredTask>()
                         .Where(static task => string.Equals(task.Type.Name, "Switch", StringComparison.Ordinal)))
            {
                WorldInteraction interaction = Assert.Single(
                    interactions.Items,
                    item => ReferenceEquals(item.Task, switchTask));
                Vector3d front = interaction.Anchor + interaction.Orientation.Transposed().Transform(
                    new Vector3d(0.0, 4096.0, 0.0));
                Vector3d rear = interaction.Anchor + interaction.Orientation.Transposed().Transform(
                    new Vector3d(0.0, -4096.0, 0.0));

                Assert.True(interaction.CanOfferFrom(front));
                Assert.False(interaction.CanOfferFrom(rear));
                found = true;
            }
        }

        Assert.True(found, "mission 14 contains no elevator-owned switch interaction");
    }

    [RequiresGameDataFact]
    public void ElevatorOwnedInteractionAnchorsFollowTheMovingPlatform()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables,
            elevators);

        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            DeclaredTask? child = Descendants(elevator.Task)
                .OfType<DeclaredTask>()
                .FirstOrDefault(static task => string.Equals(task.Type.Name, "Switch", StringComparison.Ordinal));
            WorldInteraction? interaction = child is null
                ? null
                : interactions.Items.FirstOrDefault(item => ReferenceEquals(item.Task, child));
            if (interaction is null)
            {
                continue;
            }

            DeclaredTask ownedChild = child!;
            Vector3d authoredAnchor = interaction.Anchor;
            int target = elevator.State.CurrentFloorIndex + 1 < elevator.State.FloorCount
                ? elevator.State.CurrentFloorIndex + 1
                : elevator.State.CurrentFloorIndex - 1;
            elevator.State.RequestFloor(target);
            elevator.State.Tick(canStart: true, floorUp: false, floorDown: false);
            interactions.Tick();

            // Interaction anchors are queried in engine units, unlike render placements which are
            // expressed in metres. Keep this assertion independent of the registry conversion helper
            // so a render-space value cannot accidentally make the button appear to follow the lift.
            Vector3d expectedOffset = elevator.State.Position - elevator.State.StartPosition;
            Vector3d actualOffset = interaction.Anchor - authoredAnchor;
            Assert.InRange(Math.Abs(actualOffset.X - expectedOffset.X), 0.0, 1e-8);
            Assert.InRange(Math.Abs(actualOffset.Y - expectedOffset.Y), 0.0, 1e-8);
            Assert.InRange(Math.Abs(actualOffset.Z - expectedOffset.Z), 0.0, 1e-8);

            // The real game ticks the interaction registry every frame (PlayerCamera.Tick), so the
            // switch's dynamic anchor is re-evaluated against the live platform displacement each
            // tick. Driving only the elevator state leaves the anchor's cached offset stale.
            for (int i = 0; i < 10000 && elevator.State.IsMoving; i++)
            {
                elevator.State.Tick(canStart: true, floorUp: false, floorDown: false);
                interactions.Tick();
            }

            Assert.False(elevator.State.IsMoving, "the authored elevator did not reach its requested floor");
            Vector3d travelledOffset = interaction.Anchor - authoredAnchor;
            Vector3d finalExpectedOffset = elevator.State.Position - elevator.State.StartPosition;
            Assert.Equal(finalExpectedOffset, travelledOffset);
            Assert.True(interaction.IsAvailable, "the elevator switch must remain usable after travel");
            return;
        }

        Assert.Fail("mission 14 contains no active elevator-owned switch interaction");
    }

    [RequiresGameDataFact]
    public void ElevatorOwnedSwitchRequestsTheLiftWithoutTogglingItsDoors()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, doors, variables, elevators);

        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            DeclaredTask? switchTask = Descendants(elevator.Task)
                .OfType<DeclaredTask>()
                .FirstOrDefault(static task => string.Equals(task.Type.Name, "Switch", StringComparison.Ordinal));
            if (switchTask is null)
            {
                continue;
            }

            // The lift only moves with the player inside — stand the player in the cab.
            elevators.SetPlayerPositionProvider(() => elevator.State.Position);

            IReadOnlyList<(DoorState State, bool WasOpen, bool WasClosed)> doorStates = Descendants(elevator.Task)
                .OfType<DeclaredTask>()
                .Where(static task => string.Equals(task.Type.Name, "Door", StringComparison.Ordinal))
                .Select(task =>
                {
                    DoorState state = doors.Doors.First(door => ReferenceEquals(door.Task, task)).State;
                    return (state, state.IsFullyOpen, state.IsFullyClosed);
                })
                .ToArray();

            // A door reports closed only once it has updated (0x446D10 writes the flag at the end of its update), so the
            // cab's doors are given that first update -- its motion alone, as their own Open expression (the cab is not
            // moving) would open them; the check below then holds them still, to see that the switch alone does not
            // toggle them.
            foreach (PlacedDoor door in doors.Doors)
            {
                door.State.Tick();
            }

            Assert.True(interactions.ActivateFromAi(switchTask.Id));

            // Retail wanted-floor expressions gate on Switch.isLastPressed, the falling-edge pulse
            // the switch publishes after its three-tick press hold expires (Switch::Run/EndPulse).
            // The real game drives interactions and elevators together every frame, so the request
            // lands a few ticks after the press rather than on the same tick.
            bool started = false;
            for (int tick = 0; tick < 8 && !started; tick++)
            {
                elevators.Tick();
                interactions.Tick();
                started = elevator.State.IsMoving;
            }

            Assert.True(started, "the elevator switch did not start a floor request");
            Assert.All(doorStates, door =>
            {
                Assert.Equal(door.WasOpen, door.State.IsFullyOpen);
                Assert.Equal(door.WasClosed, door.State.IsFullyClosed);
            });
            return;
        }

        Assert.Fail("mission 14 contains no active elevator-owned switch interaction");
    }

    [RequiresGameDataFact]
    public void Mission8LandingSwitchCallsTheCarToItsAuthoredFloor()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(8);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, doors, variables, elevators);

        PlacedElevator elevator = Assert.Single(elevators.Elevators, item => item.Task.Id == 811);
        WorldInteraction callButton = Assert.Single(interactions.Items, item => item.Task.Id == 255);
        Assert.DoesNotContain(callButton.Task, Descendants(elevator.Task));

        for (int tick = 0; tick < 30; tick++)
        {
            elevators.Tick();
            interactions.Tick();
            doors.Tick();
        }

        Assert.NotEqual(2, elevator.State.CurrentFloorIndex);
        Assert.True(interactions.ActivateFromAi(callButton.Task.Id));
        Assert.True(elevators.RequestFromSwitch((DeclaredTask)callButton.Task));

        bool travelled = false;
        for (int tick = 0; tick < 10000; tick++)
        {
            elevators.Tick();
            interactions.Tick();
            doors.Tick();
            travelled |= elevator.State.TransformChangedThisTick;
            if (travelled && !elevator.State.IsMoving && !elevator.State.TransformChangedThisTick)
            {
                break;
            }
        }

        Assert.True(travelled, "mission 8's landing switch never started Elevator_811");
        Assert.Equal(2, elevator.State.CurrentFloorIndex);
    }

    /// <summary>
    /// Mission 12's cutscene gondola rides side-on: its authored 271 degrees turned by its cable's 91 -- the
    /// matrix the retail game draws it with.
    /// </summary>
    /// <remarks>
    /// Elevator 1667 is neither <c>Linear</c> nor <c>Force orientation</c>, so <c>0x43D960</c> walks it along
    /// SplineObj 1666 and draws it with its authored orientation (<c>+288</c>) times the spline's frame, a turn
    /// about Z to the cable's heading on a <c>Linear Segments</c> spline. The expected matrix is the retail
    /// game's own, read by the probe's <c>elevator</c> hook set at <c>0x43D8C0</c> on every tick of the ride.
    /// The port drew the authored 271 degrees, a quarter turn out: issue 220's cable car lying across the shot.
    /// </remarks>
    [RequiresGameDataFact]
    public void Mission12CutsceneGondolaTakesItsCableHeading()
    {
        (LevelTask root, ScriptVarRegistry variables, _) = Load(12);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        PlacedElevator gondola = Assert.Single(elevators.Elevators, item => item.Task.Id == 1667);
        Assert.NotNull(gondola.SplineFrame);

        // Its "Floor up" is the constant 1: it leaves the bottom station on the first tick.
        for (int tick = 0; tick < 30; tick++)
        {
            elevators.Tick();
        }

        Assert.True(gondola.State.FloorPosition > 0.0, "the gondola never left its first waypoint");
        Matrix3f drawn = gondola.Orientation;
        Matrix3f retail = new(
            0.999301f, -0.037361f, 0f,
            0.037361f, 0.999301f, 0f,
            0f, 0f, 1f);
        AssertMatrix(retail, drawn, 1e-4f);
    }

    /// <summary>
    /// Mission 12's empty cutscene gondola leaves on the first tick and is at its full 1.5 m/s on the second, where
    /// the original puts it.
    /// </summary>
    /// <remarks>
    /// The positions are the retail game's own, read by the probe's <c>elevator</c> hook set on the first three
    /// moves of the intro. Nobody is aboard -- <c>0x43D280</c> has no occupancy test -- and <c>0x43D6E0</c> adds
    /// Speed inertia (0.99) to the speed every tick, in metres a second, so the steps are 0.99 then 1.5 m/s.
    /// </remarks>
    [RequiresGameDataFact]
    public void Mission12CutsceneGondolaLeavesEmptyAtTheOriginalsSpeed()
    {
        (LevelTask root, ScriptVarRegistry variables, _) = Load(12);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        PlacedElevator gondola = Assert.Single(elevators.Elevators, item => item.Task.Id == 1667);
        elevators.SetPlayerPositionProvider(() => new Vector3d(0, 0, 0));

        Vector3d[] retail =
        [
            new(112260205.343, -42471830.656, 179499686.058),
            new(112260201.318, -42471628.640, 179499719.480),
            new(112260197.292, -42471426.640, 179499752.900),
        ];
        foreach (Vector3d expected in retail)
        {
            elevators.Tick();
            Assert.True(
                Vector3d.Distance(expected, gondola.State.Position) <= 1.0,
                $"expected {expected}, got {gondola.State.Position}");
        }
    }

    /// <summary>Every other Elevator keeps its authored orientation, as every lift in the game is drawn.</summary>
    /// <remarks>
    /// Only an unforced, non-<c>Linear</c> car takes a spline frame. Of those, mission 14's 1706 rides a vertical
    /// spline whose chord has no X, which <c>0x4B2D00</c> gives heading nought: its frame is the identity.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryOtherElevatorKeepsItsAuthoredOrientation()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, _) = Load(missionId);
            foreach (PlacedElevator elevator in ElevatorRegistry.Build(root, variables).Elevators)
            {
                if (missionId == 12 && elevator.Task.Id == 1667)
                {
                    continue;
                }

                AssertMatrix(elevator.AuthoredOrientation, elevator.Orientation, 1e-6f);
            }
        }
    }

    private static void AssertMatrix(Matrix3f expected, Matrix3f actual, float tolerance)
    {
        float[] e = [expected.M11, expected.M12, expected.M13, expected.M21, expected.M22, expected.M23, expected.M31, expected.M32, expected.M33];
        float[] a = [actual.M11, actual.M12, actual.M13, actual.M21, actual.M22, actual.M23, actual.M31, actual.M32, actual.M33];
        for (int i = 0; i < 9; i++)
        {
            Assert.True(
                MathF.Abs(e[i] - a[i]) <= tolerance,
                $"element {i}: expected {e[i]:F6}, got {a[i]:F6} (expected {expected}, got {actual})");
        }
    }

    private static IEnumerable<GameTask> Descendants(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static (LevelTask Root, ScriptVarRegistry Variables, LoadedMission Mission) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return (root, loop.Variables, mission);
    }
}
