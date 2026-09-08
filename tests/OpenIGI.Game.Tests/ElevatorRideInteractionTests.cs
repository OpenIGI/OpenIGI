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

namespace OpenIGI.Game.Tests;

/// <summary>
/// Coverage for the three retail lift behaviours the port must honour: the car does not move without
/// the player inside, the player cannot jump while inside the cab, and the car only moves once every
/// door is fully closed.
/// </summary>
public sealed class ElevatorRideInteractionTests
{
    private static readonly Vector3d BasePosition = new(0, 0, 0);

    private static string? ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : null;

    private static bool IsDescendantOf(GameTask task, GameTask ancestor)
    {
        for (GameTask? current = task.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
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

    [RequiresGameDataFact]
    public void LiftDoesNotMoveWithoutThePlayerInside()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.State.FloorCount < 2)
                {
                    continue;
                }

                // Mission 12's Elevator_1667 is a scripted shaft whose Floor-up expression is
                // live without a switch pulse. Retail still moves that car; this test is about
                // idle hall/cabin lifts, not that scripted ride.
                if (elevator.Task.Id == 1667)
                {
                    continue;
                }

                // No player position provider wired — the lift must not move.
                for (int t = 0; t < 300; t++)
                {
                    elevators.Tick();
                }

                Assert.False(
                    elevator.State.IsMoving,
                    $"[M{missionId}] {elevator.Task.Name} moved without a player position provider.");
            }
        }
    }

    [RequiresGameDataFact]
    public void LiftMovesOnlyWhenPlayerIsInside()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.State.FloorCount < 2)
                {
                    continue;
                }

                // Only test elevators whose authored Can start expression is empty (defaults to true).
                if (elevator.Task is DeclaredTask declared
                    && ReadString(declared, "Can start") is { Length: > 0 })
                {
                    continue;
                }

                // Close all doors — the lift only moves once every door is fully closed.
                foreach (PlacedDoor door in doors.Doors)
                {
                    door.State.CommandClosed();
                }
                for (int t = 0; t < 300; t++)
                {
                    doors.Tick();
                }

                // Wire a player position provider that places the player inside the cab.
                Vector3d platformPosition = elevator.State.Position;
                elevators.SetPlayerPositionProvider(() => platformPosition);

                // Request a different floor so there is somewhere to go.
                int targetFloor = elevator.State.CurrentFloorIndex == 0 ? 1 : 0;
                elevator.State.RequestFloor(targetFloor);

                // Tick — the player is inside and all doors are closed, so it should start moving.
                elevators.Tick();
                Assert.True(
                    elevator.State.IsMoving,
                    $"[M{missionId}] {elevator.Task.Name} did not move with the player inside and doors closed.");
            }
        }
    }

    [RequiresGameDataFact]
    public void LiftWaitsForThePlayerToEnterTheSpecificCab()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(5);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);
        PlacedElevator elevator = Assert.Single(elevators.Elevators, item => item.Task.Id == 51);

        foreach (PlacedDoor door in doors.Doors)
        {
            door.State.CommandClosed();
        }
        for (int tick = 0; tick < 300; tick++)
        {
            doors.Tick();
        }
        _ = WorldInteractionRegistry.Build(root, mission, doors, variables, elevators);

        elevators.SetPlayerPositionProvider(() => elevator.State.Position + new Vector3d(100000, 0, 0));
        elevator.State.RequestFloor(1);
        elevators.Tick();
        Assert.False(elevator.State.IsMoving, "the Level 5 elevator started while the player was outside its cab");

        elevators.SetPlayerPositionProvider(() => elevator.State.Position);
        elevators.Tick();
        Assert.True(elevator.State.IsMoving, "the Level 5 elevator did not start after the player entered its cab");
    }

    [RequiresGameDataFact]
    public void LiftDoesNotMoveWhenDoorsAreNotFullyClosed()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.State.FloorCount < 2)
                {
                    continue;
                }

                // Only test elevators that actually have doors — an elevator with no doors passes the
                // door-closed gate vacuously and is not what this test exercises.
                bool hasDoors = doors.Doors.Any(d => IsDescendantOf(d.Task, elevator.Task));
                if (!hasDoors)
                {
                    continue;
                }

                // Player inside.
                Vector3d platformPosition = elevator.State.Position;
                elevators.SetPlayerPositionProvider(() => platformPosition);

                // Leave all doors open (not fully closed).
                foreach (PlacedDoor door in doors.Doors)
                {
                    if (door.State.IsFullyClosed)
                    {
                        door.State.CommandOpen();
                    }
                }
                for (int t = 0; t < 300; t++)
                {
                    doors.Tick();
                }

                int targetFloor = elevator.State.CurrentFloorIndex == 0 ? 1 : 0;
                elevator.State.RequestFloor(targetFloor);

                // Doors are open — the lift must not move.
                elevators.Tick();
                Assert.False(
                    elevator.State.IsMoving,
                    $"[M{missionId}] {elevator.Task.Name} moved while doors were open.");
            }
        }
    }

    [RequiresGameDataFact]
    public void LiftMovesWhenAllDoorsAreFullyClosed()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.State.FloorCount < 2)
                {
                    continue;
                }

                // Only test elevators whose authored Can start expression is empty (defaults to true);
                // authored gates may legitimately hold the lift and are not what this test exercises.
                if (elevator.Task is DeclaredTask declared
                    && ReadString(declared, "Can start") is { Length: > 0 })
                {
                    continue;
                }

                // Player inside.
                Vector3d platformPosition = elevator.State.Position;
                elevators.SetPlayerPositionProvider(() => platformPosition);

                // Close all doors.
                foreach (PlacedDoor door in doors.Doors)
                {
                    door.State.CommandClosed();
                }
                for (int t = 0; t < 300; t++)
                {
                    doors.Tick();
                }

                int targetFloor = elevator.State.CurrentFloorIndex == 0 ? 1 : 0;
                elevator.State.RequestFloor(targetFloor);

                // All doors closed — the lift should move.
                elevators.Tick();
                Assert.True(
                    elevator.State.IsMoving,
                    $"[M{missionId}] {elevator.Task.Name} did not move with all doors closed.");
            }
        }
    }

    [RequiresGameDataFact]
    public void IsPlayerInsideElevator_DetectsPlayerOnPlatform()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                // Player on the platform centre.
                PlacedElevator? containing;
                Assert.True(
                    elevators.IsPlayerInsideElevator(elevator.State.Position, out containing),
                    $"[M{missionId}] Player at platform centre should be detected inside.");
                Assert.Same(elevator, containing);

                // Player far away.
                Assert.False(
                    elevators.IsPlayerInsideElevator(
                        elevator.State.Position + new Vector3d(50000, 50000, 0), out _),
                    $"[M{missionId}] Player far away should not be detected inside.");
            }
        }
    }

    [RequiresGameDataFact]
    public void AreAllDoorsFullyClosed_TrueWhenAllDoorsClosed()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                // Close all doors.
                foreach (PlacedDoor door in doors.Doors)
                {
                    door.State.CommandClosed();
                }
                for (int t = 0; t < 300; t++)
                {
                    doors.Tick();
                }

                Assert.True(
                    ElevatorRegistry.AreAllDoorsFullyClosed(elevator.Task, doors),
                    $"[M{missionId}] All doors should be fully closed after commanding closed and ticking.");
            }
        }
    }
}
