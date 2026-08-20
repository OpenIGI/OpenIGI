using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Retail M1 must hide the player behind a closed door and behind a room wall.
/// </summary>
/// <remarks>
/// Building doorways and windows are portal faces. Walking skips them; AI sight (r2 <c>0x489B00</c>
/// → <c>0x4C4630</c>) must not, or a guard sees through the closed opening into the room.
/// </remarks>
public sealed class RetailSightOcclusionTests
{
    private const double U = 4096.0;

    // Doorway of Building_1118 / 400_20_1 from DoorwayDiagnosticTests.
    private static readonly Vector3d DoorwayFeet = new(24501169, -55966720, 174413291);

    [RequiresGameDataFact]
    public void ClosedDoorwayPortalBlocksSightAndStaysWalkable()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);
        Vector3d from = Mark(DoorwayFeet, -4.0);
        Vector3d to = Mark(DoorwayFeet, 4.0);

        Assert.False(index.TryCast(from, to, out _), "walking through the doorway must stay clear");

        Assert.True(
            index.TryTrace(
                new WorldTraceRequest(from, to, WorldTraceMask.World, TracePortals: true),
                out WorldTraceHit blocked),
            "sight through the closed M1 doorway found no portal or door");
        Assert.False(
            AiSoldier.WorldBlockerAllowsSight(Length(from, to), blocked.Distance),
            $"doorway hit '{blocked.Label}' at t={blocked.Fraction:0.###} still counted as visible");
    }

    [RequiresGameDataFact]
    public void RoomWallBesideLevelOneDoorwayBlocksSight()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);

        Vector3d feet = new(DoorwayFeet.X + (1.5 * U), DoorwayFeet.Y, DoorwayFeet.Z);
        Vector3d from = Mark(feet, -4.0);
        Vector3d to = Mark(feet, 4.0);

        Assert.True(
            index.TryTrace(
                new WorldTraceRequest(from, to, WorldTraceMask.World, TracePortals: true),
                out WorldTraceHit blocked),
            "room wall missed");
        Assert.False(
            AiSoldier.WorldBlockerAllowsSight(Length(from, to), blocked.Distance),
            $"room wall '{blocked.Label}' at t={blocked.Fraction:0.###} still counted as visible");
    }

    [RequiresGameDataFact]
    public void OpenDoorUnblocksThePortalItOccupied()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        WorldPlacement door = scene.Placements.First(placement =>
            string.Equals(placement.Owner.Type.Name, "Door", StringComparison.Ordinal)
            && placement.Instance.Name == "500_01_1");
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(scene.Placements);
        ObjectCollisionIndex doorOnly = ObjectCollisionIndex.Build([door]);
        (Vector3d centre, double radius) = doorOnly.Bounds(0);

        // Through the door slab, along its thin axis (local X for 500_01_1).
        Vector3d from = new(centre.X - radius - U, centre.Y, centre.Z);
        Vector3d to = new(centre.X + radius + U, centre.Y, centre.Z);
        Assert.True(
            index.TryTrace(new WorldTraceRequest(from, to, TracePortals: true), out _),
            "closed door slab should block sight");

        index.SetSightOpening(door.Owner, open: true);
        index.SetPassable(door.Owner, passable: true);

        Assert.False(
            index.TryTrace(new WorldTraceRequest(from, to, TracePortals: true), out WorldTraceHit leftover)
            && leftover.Owner == door.Owner,
            $"open door still blocked sight via '{leftover.Label}'");
    }

    [RequiresGameDataFact]
    public void LevelOneDoorTasksHaveWorldCollisionMeshes()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        WorldPlacement[] doors = scene.Placements
            .Where(placement => string.Equals(placement.Owner.Type.Name, "Door", StringComparison.Ordinal))
            .ToArray();

        Assert.True(doors.Length > 0, "M1 placed no Door tasks");

        int withWorldMesh = doors.Count(door =>
            MefCollisionMesh.TryRead(door.Model.Container, meshIndex: 0, out MefCollisionMesh? world)
            && world is { Faces.Count: > 0 });
        int indexed = ObjectCollisionIndex.Build(doors).Count;
        Assert.Equal(doors.Length, withWorldMesh);
        Assert.Equal(doors.Length, indexed);
    }

    private static Vector3d Mark(Vector3d feet, double alongYMetres) =>
        new(feet.X, feet.Y + (alongYMetres * U), feet.Z + AiSoldier.DetectionRaiseStandingInUnits);

    private static double Length(Vector3d a, Vector3d b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double dz = b.Z - a.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
