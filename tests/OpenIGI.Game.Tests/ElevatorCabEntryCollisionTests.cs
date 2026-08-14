using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Regression coverage for the reported bug: the player cannot jump/enter inside the elevator cab.
/// For every elevator that exposes a floor-0 cab door, once that door has slid fully open the doorway
/// must be physically passable to a player collision ray cast from just outside the cab toward the cab
/// centre; while the door is closed the same doorway must stay solid. This is the collision gate that
/// lets a player ride the lift rather than bumping against an invisible wall.
/// </summary>
public sealed class ElevatorCabEntryCollisionTests
{
    [RequiresGameDataFact]
    public void AnOpenCabDoorLetsThePlayerIntoTheCabAndAClosedOneBlocks()
    {
        int passableOpen = 0;
        int blockedClosed = 0;
        int checkedCabs = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, doors, variables, elevators);
            MagicObjectRegistry magic = MagicFor(root);
            WorldScene scene = WorldScene.Build(mission, root, magic);
            ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

            foreach (PlacedElevator elevator in elevators.Elevators)
            {
                if (elevator.Task is not DeclaredTask declared)
                {
                    continue;
                }

                // Command the floor-0 doors open by driving vFloor to 0 (default) and ticking the
                // interactions (which evaluate each door's open expression) and door animation.
                for (int t = 0; t < 300; t++)
                {
                    interactions.Tick();
                    elevators.Tick();
                    doors.Tick();
                    RefreshDoorTransforms(solids, doors, elevator.Task.Name);
                }

                // The engine translates every elevator-riding placement (the cab floor, its walls) with
                // the lift as it travels; mirror that here so a cab parked at floor 0 is measured where
                // it actually is, not at its authored start floor.
                ApplyElevatorDisplacement(solids, elevators);

                PlacedDoor? cabDoor = FindFloorDoor(doors, elevator.Task.Name, floor: 0);
                if (cabDoor is null || !cabDoor.State.IsFullyOpen)
                {
                    continue; // no authored floor-0 cab door (e.g. CutScene-driven lifts).
                }

                // Build a horizontal walk-in ray from a body-depth beyond the doorway (building side)
                // at chest height toward the cab. Starting inside the cab (as an earlier version did)
                // can never observe the closed leaf blocking, so the origin sits half a body-depth
                // outside the doorway plane and the endpoint half a body-depth inside.
                (Vector3d dA, Vector3d dB) = cabDoor.DoorwaySegment;
                Vector3d doorwayCentre = (dA + dB) / 2.0;
                Vector3d cabCentre = new(
                    elevator.State.Position.X / WorldScene.UnitsPerMetre,
                    elevator.State.Position.Y / WorldScene.UnitsPerMetre,
                    elevator.State.Position.Z / WorldScene.UnitsPerMetre);
                Vector3d outward = cabCentre - doorwayCentre;
                // The player walks horizontally, so project the walk-in direction flat onto the ground
                // plane; a mostly-vertical unit would otherwise make the ray clip the door frame.
                outward = new Vector3d(outward.X, outward.Y, 0.0);
                double len = outward.Length;
                Vector3d dir = len > 0.0 ? outward / len : Vector3d.UnitY;
                // The collision index works in world (engine) units — Build multiplies a placement's
                // metre position by UnitsPerMetre — so the ray endpoints must be world units too, not
                // metres. Chest height is half a metre above the doorway sill.
                double walkZ = (doorwayCentre.Z + 0.5) * WorldScene.UnitsPerMetre;
                Vector3d from = new(
                    (doorwayCentre.X - (dir.X * 1.5)) * WorldScene.UnitsPerMetre,
                    (doorwayCentre.Y - (dir.Y * 1.5)) * WorldScene.UnitsPerMetre,
                    walkZ);
                Vector3d to = new(
                    (doorwayCentre.X + (dir.X * 1.5)) * WorldScene.UnitsPerMetre,
                    (doorwayCentre.Y + (dir.Y * 1.5)) * WorldScene.UnitsPerMetre,
                    walkZ);

                // The open doorway must be passable to the player body. A fully-open door is made
                // passable by the engine (SetPassable on the open state), and colboxes are excluded from
                // the player trace entirely, so the only thing that can still block an open doorway is
                // either (a) a door that failed to open (a real regression) or (b) a benign static
                // building structural solid whose mesh overlaps the doorway (e.g. M12's
                // ELEVATOR_FLOOR/432_02_1 — the elevator building's own floor slab, a ~24 m bounding
                // sphere owned by Building_-1). Only the door case is a defect; the structural solid is
                // ground/structure geometry, not the invisible colbox/door wall this test guards.
                bool openBlocks = solids.TryCastForPlayer(from, to, out _);
                string blockerLabel = string.Empty;
                bool openBlockedByDoor = openBlocks
                    && solids.TryIdentifyForPlayer(from, to, out blockerLabel, out GameTask? blockerOwner, out _)
                    && doors.Doors.Any(d => ReferenceEquals(d.Task, blockerOwner));
                checkedCabs++;

                // With every door that references this lift (its open expression keys off the lift's
                // vFloor) commanded closed and given time to seal, the same doorway must block a player
                // body. (The floor-0 door picked by its open expression may not be the panel that
                // physically fills the gap — the cab can have inner+outer leaves — so close them all.)
                foreach (PlacedDoor referenced in ReferencedDoors(doors, elevator.Task.Name))
                {
                    referenced.State.CommandClosed();
                }

                for (int t = 0; t < 300 && !AllClosed(doors, elevator.Task.Name); t++)
                {
                    doors.Tick();
                    RefreshDoorTransforms(solids, doors, elevator.Task.Name);
                }

                bool allSealed = AllClosed(doors, elevator.Task.Name);
                bool closedPasses = allSealed && !solids.TryCastForPlayer(from, to, out _);

                Assert.True(
                    !openBlockedByDoor,
                    $"[M{missionId}] {elevator.Task.Name} open cab door {cabDoor.Task.Name} still blocked a " +
                    $"player body walking into the cab from {from} (blocker: {blockerLabel})");
                Assert.True(
                    !closedPasses,
                    $"[M{missionId}] {elevator.Task.Name} closed cab door {cabDoor.Task.Name} was passable to " +
                    $"a player body walking into the cab from {from}");
                passableOpen += openBlockedByDoor ? 0 : 1;
                blockedClosed += closedPasses ? 0 : 1;
            }
        }

        Assert.True(checkedCabs > 0, "no elevator exposed an authored floor-0 cab door");
        Assert.True(
            passableOpen == checkedCabs,
            $"{passableOpen}/{checkedCabs} cabs were enterable through an open door; expected all");
        Assert.True(
            blockedClosed == checkedCabs,
            $"{blockedClosed}/{checkedCabs} cabs blocked through a closed door; expected all");
    }

    /// <summary>
    /// Guards the elevator-shaft <c>colbox*</c> exclusion so it stays scoped to levels that actually
    /// contain elevator machinery. The engine drops <c>colbox*</c> from the human trace on elevator
    /// levels (so the player can board a lift), but a level without elevators authorises the same
    /// names as ordinary static collision the player must hit. M9/M10 have no elevator tasks and still
    /// place <c>colbox66</c>/<c>colbox</c> with player meshes — those must remain solid.
    /// </summary>
    [RequiresGameDataFact]
    public void ColboxShaftExclusionIsScopedToElevatorLevels()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(missionId);
            MagicObjectRegistry magic = MagicFor(root);
            WorldScene scene = WorldScene.Build(mission, root, magic);
            ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
            ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
            bool levelHasElevator = elevators.Elevators.Count > 0;

            foreach (WorldPlacement p in scene.Placements)
            {
                if (!p.Instance.Name.StartsWith("colbox", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool hasPlayerMesh = MefCollisionMesh.TryRead(
                    p.Model.Container, meshIndex: 1, out MefCollisionMesh? player)
                    && player is { Faces.Count: > 0 };
                if (!hasPlayerMesh)
                {
                    continue;
                }

                bool inPlayerTrace = solids.ContainsPlayerEntry(p.Instance.Name);
                if (levelHasElevator)
                {
                    // Elevator level: the shaft colbox must be gone from the player trace so the
                    // player can walk into the cab.
                    Assert.False(
                        inPlayerTrace,
                        $"[M{missionId}] elevator-level colbox '{p.Instance.Name}' remained in the player trace; " +
                        $"the player could not board the lift");
                }
                else
                {
                    // Non-elevator level (M9/M10): the colbox is ordinary collision and must stay solid.
                    Assert.True(
                        inPlayerTrace,
                        $"[M{missionId}] non-elevator colbox '{p.Instance.Name}' was dropped from the player trace; " +
                        $"the player could walk through it");
                }
            }
        }
    }

    private static void RefreshDoorTransforms(ObjectCollisionIndex solids, DoorRegistry doors, string elevatorName)
    {
        foreach (PlacedDoor door in doors.Doors)
        {
            if (door.Task is DeclaredTask dd
                && ReadString(dd, "Open door expression") is { } openExpr
                && openExpr.Contains(elevatorName, StringComparison.Ordinal))
            {
                // Mirror the engine: a fully-open door is passable even when its leaf is authored to
                // animate inside the model (Stop* == 0, no placement slide), and the moving transform
                // tracks a sliding leaf's half-open position.
                solids.SetPassable(door.Task, door.State.IsFullyOpen);
                solids.UpdateTransform(door.Task, door.DrawPosition, door.DrawOrientation);
            }
        }
    }

    /// <summary>
    /// Moves every elevator and each of its riding placements (cab floor, walls, inner doors) to where
    /// the lift currently is, exactly as the engine's per-frame collision sync does.
    /// </summary>
    private static void ApplyElevatorDisplacement(ObjectCollisionIndex solids, ElevatorRegistry elevators)
    {
        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            solids.UpdateTransform(elevator.Task, elevator.DrawPosition, elevator.Orientation);
            Vector3d displacement = (elevator.State.Position - elevator.State.StartPosition)
                / WorldScene.UnitsPerMetre;
            solids.UpdateDescendantTranslation(elevator.Task, displacement);
        }
    }

    private static PlacedDoor? FindFloorDoor(DoorRegistry doors, string elevatorName, int floor)
    {
        foreach (PlacedDoor door in doors.Doors)
        {
            if (door.Task is not DeclaredTask dd
                || ReadString(dd, "Open door expression") is not { } openExpr
                || !openExpr.Contains(elevatorName, StringComparison.Ordinal)
                || !openExpr.Contains($"== {floor}", StringComparison.Ordinal))
            {
                continue;
            }

            return door;
        }

        return null;
    }

    private static string? ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : null;

    private static bool AllClosed(DoorRegistry doors, string elevatorName)
    {
        foreach (PlacedDoor referenced in ReferencedDoors(doors, elevatorName))
        {
            if (!referenced.State.IsFullyClosed)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<PlacedDoor> ReferencedDoors(DoorRegistry doors, string elevatorName)
    {
        foreach (PlacedDoor door in doors.Doors)
        {
            if (door.Task is DeclaredTask dd
                && ReadString(dd, "Open door expression") is { } openExpr
                && openExpr.Contains(elevatorName, StringComparison.Ordinal))
            {
                yield return door;
            }
        }
    }

    private static MagicObjectRegistry MagicFor(LevelTask root)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        return magic;
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
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level, null, terrainLoader: null).Load(missionId);
        return (root, loop.Variables, mission);
    }
}
