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
