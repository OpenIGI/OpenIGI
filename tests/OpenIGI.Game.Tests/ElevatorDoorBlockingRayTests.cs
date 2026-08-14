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
/// The mid-swing doorway ray — the retail mechanism (0x446D10 at 0x447302) that keeps a door from
/// sealing while a human stands in its opening. Because the elevator's authored <c>Can start</c> is
/// typically <c>Door.isClosed</c>, a blocked door keeps the lift from running: it is how the original
/// refuses to carry a lift while somebody is inside it or blocks its door.
/// </summary>
public sealed class ElevatorDoorBlockingRayTests
{
    /// <summary>A closing door whose doorway holds a human is forced back open and never seals.</summary>
    /// <remarks>
    /// The registry re-opens the door via the existing <see cref="DoorState.CommandOpen"/> path the
    /// moment a closing door (state 3, latch clear) reports a human in its doorway. Over a long run it
    /// must never reach <see cref="DoorState.IsFullyClosed"/>.
    /// </remarks>
    [RequiresGameDataFact]
    public void ABlockedClosingDoorIsForcedBackOpenAndNeverSeals()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        DoorRegistry doors = DoorRegistry.Build(root);

        (PlacedDoor? door, Vector3d a, Vector3d b) = PickElevatorDoor(root, variables, doors);
        if (door is null)
        {
            return; // mission 14 has no elevator doors in this build
        }

        // No human present: the door opens and then closes all the way on its own.
        doors.SetDoorwayBlockedQuery(null);
        door.State.CommandOpen();
        for (int tick = 0; tick < 200 && !door.State.IsFullyOpen; tick++)
        {
            doors.Tick();
        }

        Assert.True(door.State.IsFullyOpen, "the door should open fully when nobody blocks it");
        door.State.CommandClosed();
        bool everSealedUnblocked = false;
        for (int tick = 0; tick < 200; tick++)
        {
            doors.Tick();
            everSealedUnblocked |= door.State.IsFullyClosed;
        }

        Assert.True(everSealedUnblocked, "an unblocked closing door should seal");

        // With a human standing in the doorway, it re-opens and never seals.
        doors.SetDoorwayBlockedQuery((from, to) =>
            Math.Abs(from.X - a.X) < 1e-6 && Math.Abs(to.X - b.X) < 1e-6);
        door.State.CommandOpen();
        for (int tick = 0; tick < 200 && !door.State.IsFullyOpen; tick++)
        {
            doors.Tick();
        }

        Assert.True(door.State.IsFullyOpen, "the door should open fully to let the human through");
        door.State.CommandClosed();
        bool everSealedBlocked = false;
        for (int tick = 0; tick < 200; tick++)
        {
            doors.Tick();
            everSealedBlocked |= door.State.IsFullyClosed;
        }

        Assert.False(everSealedBlocked, "a blocked closing door must never reach fully closed");
    }

    /// <summary>The same door, human and blocking check, expressed through a concrete human position.</summary>
    /// <remarks>
    /// The wiring in the desktop game passes a live position (the player body or a soldier's feet) in
    /// world units and answers the doorway query by testing that point against the doorway segment. This
    /// test drives that same contract end to end: a human standing inside the doorway blocks it, a human
    /// far from it does not.
    /// </remarks>
    [RequiresGameDataFact]
    public void AHumanInsideTheDoorwayBlocksItWhileAHumanOutsideDoesNot()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        DoorRegistry doors = DoorRegistry.Build(root);

        (PlacedDoor? door, Vector3d a, Vector3d b) = PickElevatorDoor(root, variables, doors);
        if (door is null)
        {
            return;
        }

        Vector3d centre = new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);
        Vector3d centreWorld = new(
            centre.X * WorldScene.UnitsPerMetre,
            centre.Y * WorldScene.UnitsPerMetre,
            centre.Z * WorldScene.UnitsPerMetre);
        Vector3d farWorld = new(
            (centre.X + 50.0) * WorldScene.UnitsPerMetre,
            centre.Y * WorldScene.UnitsPerMetre,
            centre.Z * WorldScene.UnitsPerMetre);

        // The desktop helper: a human's feet in world units, tested against the metre-space doorway.
        static bool StandsInSegment(Vector3d? feetWorld, Vector3d from, Vector3d to)
        {
            if (feetWorld is not { } feet)
            {
                return false;
            }

            Vector3d point = new(
                feet.X / WorldScene.UnitsPerMetre,
                feet.Y / WorldScene.UnitsPerMetre,
                feet.Z / WorldScene.UnitsPerMetre);
            Vector3d closest = ClosestPointOnSegment(from, to, point);
            return Vector3d.Distance(closest, point) <= 0.5;
        }

        // The human's live position; move it between phases to prove the doorway test reacts.
        Vector3d humanWorld = farWorld;
        doors.SetDoorwayBlockedQuery((from, to) => StandsInSegment(humanWorld, from, to));

        // With a human far from the opening, the door seals.
        door.State.CommandOpen();
        for (int tick = 0; tick < 200 && !door.State.IsFullyOpen; tick++)
        {
            doors.Tick();
        }

        Assert.True(door.State.IsFullyOpen, "the door should open fully");
        door.State.CommandClosed();
        bool sealedOutside = false;
        for (int tick = 0; tick < 200; tick++)
        {
            doors.Tick();
            sealedOutside |= door.State.IsFullyClosed;
        }

        Assert.True(sealedOutside, "a door with no human in its opening should seal");

        // Move the human into the doorway: the door opens, then re-opens on every close attempt.
        humanWorld = centreWorld;
        door.State.CommandOpen();
        for (int tick = 0; tick < 200 && !door.State.IsFullyOpen; tick++)
        {
            doors.Tick();
        }

        Assert.True(door.State.IsFullyOpen, "the door should open fully to let the human through");
        door.State.CommandClosed();
        bool everSealedWhileBlocked = false;
        for (int tick = 0; tick < 200; tick++)
        {
            doors.Tick();
            everSealedWhileBlocked |= door.State.IsFullyClosed;
        }

        Assert.False(everSealedWhileBlocked, "a door whose doorway holds a human must not seal");
    }

    /// <summary>A lift whose <c>Can start</c> gates on its doors stays stopped while any is blocked.</summary>
    /// <remarks>
    /// This is the whole point of the feature: the elevator must not run when a human is inside it or
    /// blocks its door. Retail realises that rule entirely through the door — <c>Can start</c> reads
    /// <c>Door.isClosed</c>, and a blocked door never reaches <c>isClosed</c>, so
    /// <see cref="ElevatorRegistry.Tick"/> keeps <c>CanStart</c> false and the platform never moves.
    /// </remarks>
    [RequiresGameDataFact]
    public void AnElevatorWhoseDoorIsBlockedDoesNotRun()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        DoorRegistry doors = DoorRegistry.Build(root);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, doors, variables, elevators);

        PlacedElevator? withDoor = null;
        PlacedDoor? door = null;
        Vector3d a = default, b = default;
        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            PlacedDoor? owned = Descendants(elevator.Task)
                .OfType<DeclaredTask>()
                .Where(static t => string.Equals(t.Type.Name, "Door", StringComparison.Ordinal))
                .Select(t => doors.Doors.First(d => ReferenceEquals(d.Task, t)))
                .FirstOrDefault();
            if (owned is not null)
            {
                withDoor = elevator;
                door = owned;
                (a, b) = owned.DoorwaySegment;
                break;
            }
        }

        if (withDoor is null || door is null)
        {
            return;
        }

        // The lift only moves with the player inside — stand the player in the cab.
        elevators.SetPlayerPositionProvider(() => withDoor.State.Position);

        // Start from a sealed door and no human: the lift should run.
        doors.SetDoorwayBlockedQuery(null);
        Assert.True(door.State.IsFullyClosed, "the chosen elevator door should start closed");

        // Request a floor and drive all registries together (as the real game does each frame).
        int target = withDoor.State.CurrentFloorIndex + 1 < withDoor.State.FloorCount
            ? withDoor.State.CurrentFloorIndex + 1
            : withDoor.State.CurrentFloorIndex - 1;
        withDoor.State.RequestFloor(target);

        bool ranUnblocked = false;
        for (int tick = 0; tick < 200; tick++)
        {
            elevators.Tick();
            doors.Tick();
            interactions.Tick();
            ranUnblocked |= withDoor.State.IsMoving;
        }

        Assert.True(ranUnblocked, "an elevator with sealed doors and nobody blocking them should run");

        // Reset to sealed, then block the doorway with a human.
        withDoor.State.RequestFloor(withDoor.State.CurrentFloorIndex);
        door.State.CommandClosed();
        for (int tick = 0; tick < 200 && !door.State.IsFullyClosed; tick++)
        {
            elevators.Tick();
            doors.Tick();
            interactions.Tick();
        }

        Assert.True(door.State.IsFullyClosed, "the door should seal once more with nobody blocking it");

        doors.SetDoorwayBlockedQuery((from, to) =>
            Math.Abs(from.X - a.X) < 1e-6 && Math.Abs(to.X - b.X) < 1e-6);

        bool everStartedWhileBlocked = false;
        for (int tick = 0; tick < 200; tick++)
        {
            elevators.Tick();
            doors.Tick();
            interactions.Tick();
            // The blocked door re-opens, so isClosed is false, so Can start is false.
            everStartedWhileBlocked |= withDoor.State.IsMoving;
        }

        Assert.False(everStartedWhileBlocked, "an elevator whose door is blocked by a human must not run");
    }

    private static (PlacedDoor? Door, Vector3d A, Vector3d B) PickElevatorDoor(
        LevelTask root,
        ScriptVarRegistry variables,
        DoorRegistry doors)
    {
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables, doors);
        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            PlacedDoor? owned = Descendants(elevator.Task)
                .OfType<DeclaredTask>()
                .Where(static t => string.Equals(t.Type.Name, "Door", StringComparison.Ordinal))
                .Select(t => doors.Doors.First(d => ReferenceEquals(d.Task, t)))
                .FirstOrDefault();
            if (owned is not null)
            {
                (Vector3d a, Vector3d b) = owned.DoorwaySegment;
                return (owned, a, b);
            }
        }

        return (null, default, default);
    }

    private static Vector3d ClosestPointOnSegment(Vector3d start, Vector3d end, Vector3d point)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared <= 0.0)
        {
            return start;
        }

        double t = (((point.X - start.X) * dx)
            + ((point.Y - start.Y) * dy)
            + ((point.Z - start.Z) * dz)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return new Vector3d(start.X + (dx * t), start.Y + (dy * t), start.Z + (dz * t));
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
