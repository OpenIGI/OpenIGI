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

/// <summary>
/// Regression coverage for the reported bug: the elevator call switch only worked on L3 and L8 in
/// real gameplay. This exercises the full authored wiring on every switch-linked level — the player
/// stands on a floor and presses that floor's call switch; the lift must arrive and open that floor's
/// doors. It drives the same components the desktop ticks (interactions + elevators + doors).
/// </summary>
public sealed class ElevatorSwitchInteractionTests
{
    [RequiresGameDataFact]
    public void CallSwitchBringsTheElevatorAndOpensItsDoorsOnAllLevels()
    {
        int levelsTested = 0;
        int elevatorsTested = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, doors, variables, elevators);

            bool levelHasCallableElevator = false;

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.Task is not DeclaredTask declared)
                {
                    continue;
                }

                // The lift only moves with the player inside — stand the player in the cab.
                elevators.SetPlayerPositionProvider(() => elevator.State.Position);

                int settleFloor = Settle(elevator, interactions, elevators, doors);

                // The player calls from a floor different from where the lift currently is, so a real
                // request is exercised rather than a same-floor no-op.
                int playerFloor = settleFloor == 0 && elevator.State.FloorCount > 1
                    ? elevator.State.FloorCount - 1
                    : 0;
                if (playerFloor == settleFloor || playerFloor >= elevator.State.FloorCount)
                {
                    continue; // single-floor lift or no distinct floor to call from.
                }

                WorldInteraction? callSwitch = FindCallSwitch(interactions, declared, playerFloor, elevator);
                if (callSwitch is null)
                {
                    continue; // no authored call switch for that floor (e.g. CutScene-driven lifts).
                }

                levelHasCallableElevator = true;
                elevatorsTested++;
                Assert.True(
                    interactions.ActivateFromAi(callSwitch.Task.Id),
                    $"[M{missionId}] call switch {callSwitch.Task.Name} for floor {playerFloor} did not activate");
                bool arrived = false;
                for (int tick = 0; tick < 3000; tick++)
                {
                    elevators.Tick();
                    interactions.Tick();
                    doors.Tick();
                    if (elevator.State.CurrentFloorIndex == playerFloor && !elevator.State.IsMoving)
                    {
                        arrived = true;
                        break;
                    }
                }

                Assert.True(
                    arrived,
                    $"[M{missionId}] {elevator.Task.Name} did not arrive at floor {playerFloor} " +
                    $"(stayed at {elevator.State.CurrentFloorIndex}) after pressing {callSwitch.Task.Name}");

                // Let the arrival-floor doors travel fully open (they open on the authored floor
                // condition and take several ticks to slide).
                for (int tick = 0; tick < 600 && !ArrivalFloorDoorOpen(doors, elevator.Task, playerFloor); tick++)
                {
                    elevators.Tick();
                    interactions.Tick();
                    doors.Tick();
                }

                // The arrival-floor doors must open.
                Assert.True(
                    ArrivalFloorDoorOpen(doors, elevator.Task, playerFloor),
                    $"[M{missionId}] {elevator.Task.Name} arrival floor {playerFloor} doors did not open");
            }

            if (levelHasCallableElevator)
            {
                levelsTested++;
            }
        }

        Assert.True(levelsTested > 0, "no level exposed a callable elevator switch in the retail campaign");
        Assert.True(elevatorsTested > 0, "no callable elevator switch was found");
    }

    [RequiresGameDataFact]
    public void Mission5ExteriorHallCallBringsAnEmptyCabToThePlayer()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(5);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, doors, variables, elevators);
        PlacedElevator elevator = Assert.Single(elevators.Elevators, item => item.Task.Id == 51);
        DeclaredTask declared = Assert.IsType<DeclaredTask>(elevator.Task);

        int startFloor = Settle(elevator, interactions, elevators, doors);
        int targetFloor = startFloor == 0 ? 1 : 0;
        WorldInteraction callSwitch = Assert.IsType<WorldInteraction>(
            FindCallSwitch(interactions, declared, targetFloor, elevator));

        // A hall call is made from outside the cab. The player provider remains outside for the
        // entire ride, proving that the exterior switch source—not cab occupancy—authorises travel.
        Vector3d outside = elevator.State.Position + new Vector3d(100000, 0, 0);
        elevators.SetPlayerPositionProvider(() => outside);
        callSwitch.Press();

        bool arrived = false;
        for (int tick = 0; tick < 10000; tick++)
        {
            elevators.Tick();
            interactions.Tick();
            doors.Tick();
            if (elevator.State.CurrentFloorIndex == targetFloor && !elevator.State.IsMoving)
            {
                arrived = true;
                break;
            }
        }

        Assert.True(arrived,
            $"Mission 5 exterior hall call did not bring Elevator_51 from floor {startFloor} to {targetFloor}");
    }

    /// <summary>
    /// Exercises the real player-facing activation path rather than a direct task-id press: the player
    /// stands ~1.5 m in front of a floor's hall call switch and looks at it, <see cref="WorldInteractionRegistry.Resolve"/>
    /// must offer that switch, and pressing the resolved switch must bring the lift to that floor and
    /// open its doors. This is the flow the reported bug (only L3/L8 call switches worked in gameplay)
    /// depends on, so it must work on every level that exposes a callable lift.
    /// </summary>
    [RequiresGameDataFact]
    public void ResolvePathFindsAndActivatesCallSwitchOnAllLevels()
    {
        int levelsTested = 0;
        int elevatorsTested = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, doors, variables, elevators);

            bool levelHasCallableElevator = false;

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.Task is not DeclaredTask declared)
                {
                    continue;
                }

                // The lift only moves with the player inside — stand the player in the cab.
                elevators.SetPlayerPositionProvider(() => elevator.State.Position);

                int settleFloor = Settle(elevator, interactions, elevators, doors);
                int playerFloor = settleFloor == 0 && elevator.State.FloorCount > 1
                    ? elevator.State.FloorCount - 1
                    : 0;
                if (playerFloor == settleFloor || playerFloor >= elevator.State.FloorCount)
                {
                    continue; // single-floor lift or no distinct floor to call from.
                }

                WorldInteraction? callSwitch = FindCallSwitch(interactions, declared, playerFloor, elevator);
                if (callSwitch is null)
                {
                    continue; // no authored call switch for that floor.
                }

                levelHasCallableElevator = true;
                elevatorsTested++;

                // Switch::GetActivate (0x444500) uses orientation local +Y as the panel front.
                const double standOffUnits = 6144.0; // 1.5 m, inside the 2 m ActivateRadius.
                Vector3d front = callSwitch.Orientation.Transposed().Transform(
                    new Vector3d(0.0, standOffUnits, 0.0));
                Vector3d eye = callSwitch.Anchor + front;
                float yaw = (float)Math.Atan2(-front.X, -front.Y);

                WorldInteraction? resolved = interactions.Resolve(eye, yaw, 0f, out _);
                Assert.True(
                    ReferenceEquals(resolved, callSwitch),
                    $"[M{missionId}] {elevator.Task.Name} floor {playerFloor} call switch {callSwitch.Task.Name} " +
                    $"was not resolved by the player aim at {eye}");

                resolved.Press();

                bool arrived = false;
                for (int tick = 0; tick < 3000; tick++)
                {
                    elevators.Tick();
                    interactions.Tick();
                    doors.Tick();
                    if (elevator.State.CurrentFloorIndex == playerFloor && !elevator.State.IsMoving)
                    {
                        arrived = true;
                        break;
                    }
                }

                Assert.True(
                    arrived,
                    $"[M{missionId}] {elevator.Task.Name} did not arrive at floor {playerFloor} " +
                    $"after resolving and pressing {callSwitch.Task.Name}");

                for (int tick = 0; tick < 600 && !ArrivalFloorDoorOpen(doors, elevator.Task, playerFloor); tick++)
                {
                    elevators.Tick();
                    interactions.Tick();
                    doors.Tick();
                }

                Assert.True(
                    ArrivalFloorDoorOpen(doors, elevator.Task, playerFloor),
                    $"[M{missionId}] {elevator.Task.Name} arrival floor {playerFloor} doors did not open " +
                    $"after resolving and pressing {callSwitch.Task.Name}");
            }

            if (levelHasCallableElevator)
            {
                levelsTested++;
            }
        }

        Assert.True(levelsTested > 0, "no level exposed a callable elevator switch in the retail campaign");
        Assert.True(elevatorsTested > 0, "no callable elevator switch was found");
    }

    /// <summary>
    /// Parks the lift on its authored start floor and returns that floor index.</summary>
    private static int Settle(
        PlacedElevator elevator,
        WorldInteractionRegistry interactions,
        ElevatorRegistry elevators,
        DoorRegistry doors)
    {
        // Let the lift and its doors reach a clean rest: tick a fixed budget so door channels
        // settle on their authored open/closed state for the current floor (matching the probe
        // harness rather than breaking on the first not-moving tick, which can leave a door
        // half-travelling and disturb the call-switch close/open race).
        for (int tick = 0; tick < 3000; tick++)
        {
            elevators.Tick();
            interactions.Tick();
            doors.Tick();
        }

        return elevator.State.CurrentFloorIndex;
    }

    /// <summary>
    /// Finds the hall call switch authored for <paramref name="playerFloor"/>: the switch named in the
    /// "Wanted floor N" expression that is not owned by the lift (the in-cab panel switch is owned).
    /// </summary>
    private static WorldInteraction? FindCallSwitch(
        WorldInteractionRegistry interactions,
        DeclaredTask elevator,
        int playerFloor,
        PlacedElevator placed)
    {
        if (!elevator.TryGetValue($"Wanted floor {playerFloor}", out object? raw) || raw is not string expr)
        {
            return null;
        }

        foreach (WorldInteraction item in interactions.Items)
        {
            if (item.Type != InteractionActivateType.Switch)
            {
                continue;
            }

            if (expr.Contains(item.Task.Name, StringComparison.Ordinal)
                && !IsDescendant(item.Task, placed.Task))
            {
                return item;
            }
        }

        return null;
    }

    private static bool ArrivalFloorDoorOpen(
        DoorRegistry doors,
        GameTask elevatorTask,
        int floor)
    {
        foreach (PlacedDoor door in doors.Doors)
        {
            if (door.Task is not DeclaredTask dd
                || ReadString(dd, "Open door expression") is not { } openExpr
                || !openExpr.Contains(elevatorTask.Name, StringComparison.Ordinal)
                || !openExpr.Contains($"== {floor}", StringComparison.Ordinal))
            {
                continue;
            }

            if (!door.State.IsFullyOpen)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDescendant(GameTask task, GameTask ancestor)
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

    private static string? ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : null;

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
