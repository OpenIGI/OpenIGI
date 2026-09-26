using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Retail sight must pass through authored openings while physical doors and room walls remain cover.
/// </summary>
/// <remarks>
/// Building doorways and windows are portal faces rather than solid geometry. AI sight includes portal
/// faces only when the owning task is an actual Door, so an opening stays open and a closed leaf blocks.
/// </remarks>
public sealed class RetailSightOcclusionTests
{
    private const double U = 4096.0;

    // Doorway of Building_1118 / 400_20_1 from DoorwayDiagnosticTests.
    private static readonly Vector3d DoorwayFeet = new(24501169, -55966720, 174413291);

    [RequiresGameDataFact]
    public void BuildingDoorwayPortalDoesNotBecomeAnInvisibleSightWall()
    {
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);
        Vector3d from = Mark(DoorwayFeet, -4.0);
        Vector3d to = Mark(DoorwayFeet, 4.0);

        Assert.False(index.TryCast(from, to, out _), "walking through the doorway must stay clear");

        Assert.False(
            index.TryTrace(
                new WorldTraceRequest(from, to, WorldTraceMask.World),
                out WorldTraceHit blocked),
            $"the clear M1 doorway hit invisible portal '{blocked.Label}' at t={blocked.Fraction:0.###}");
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
                new WorldTraceRequest(from, to, WorldTraceMask.World),
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
        ObjectCollisionIndex index = ObjectCollisionIndex.Build([door]);
        ObjectCollisionIndex doorOnly = ObjectCollisionIndex.Build([door]);
        (Vector3d centre, _) = doorOnly.Bounds(0);

        // Through the door slab, along its thin axis (local X for 500_01_1).
        double rayHalfLength = 0.5 * U;
        Vector3d from = new(centre.X - rayHalfLength, centre.Y, centre.Z);
        Vector3d to = new(centre.X + rayHalfLength, centre.Y, centre.Z);
        // TryCast excludes every portal face. A hit here therefore proves the physical, non-portal
        // door leaf blocks; the assertion cannot pass merely because a Door-owned portal was included.
        Assert.True(index.TryCast(from, to, out _), "physical closed door leaf should block sight");

        index.SetSightOpening(door.Owner, open: true);
        index.SetPassable(door.Owner, passable: true);

        Assert.False(index.TryCast(from, to, out _), "passable open door retained its physical leaf");
        Assert.False(
            index.TryTrace(new WorldTraceRequest(from, to), out WorldTraceHit leftover),
            $"open doorway still blocked sight via '{leftover.Label}'");
    }

    [RequiresGameDataFact]
    public void TranslatedOpenDoorStaysClearWithoutSolidifyingTheBuildingPortal()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        WorldPlacement door = scene.Placements.First(placement =>
            string.Equals(placement.Owner.Type.Name, "Door", StringComparison.Ordinal)
            && placement.Instance.Name == "500_01_1");
        WorldPlacement building = scene.Placements.First(placement => placement.Instance.Name == "400_02_1");
        ObjectCollisionIndex index = ObjectCollisionIndex.Build([door, building]);
        ObjectCollisionIndex doorOnly = ObjectCollisionIndex.Build([door]);
        (Vector3d centre, _) = doorOnly.Bounds(0);

        Vector3d displacementMetres = new(0.0, 0.0, 8.0);
        Vector3d displacementUnits = displacementMetres * U;
        Assert.True(index.UpdateTransform(building.Owner, building.Position + displacementMetres, building.Orientation));
        Assert.True(index.UpdateTransform(door.Owner, door.Position + displacementMetres, door.Orientation));

        double rayHalfLength = 0.5 * U;
        Vector3d translatedCentre = centre + displacementUnits;
        Vector3d from = new(translatedCentre.X - rayHalfLength, translatedCentre.Y, translatedCentre.Z);
        Vector3d to = new(translatedCentre.X + rayHalfLength, translatedCentre.Y, translatedCentre.Z);
        Assert.True(index.TryCast(from, to, out _), "translated physical closed door leaf should block sight");

        index.SetPassable(door.Owner, passable: true);
        index.SetSightOpening(door.Owner, open: true);
        Assert.False(index.TryCast(from, to, out _), "translated passable door retained its physical leaf");
        Assert.False(
            index.TryTrace(new WorldTraceRequest(from, to), out WorldTraceHit authored),
            $"the passable door still hit '{authored.Label}' through a building portal");

        index.SetSightOpening(door.Owner, open: true, displacementMetres: displacementMetres);
        Assert.False(
            index.TryTrace(new WorldTraceRequest(from, to), out WorldTraceHit leftover),
            $"translated open doorway still blocked sight via '{leftover.Label}'");
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
            && world is { Faces.Length: > 0 });
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
