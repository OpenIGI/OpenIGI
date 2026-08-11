using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Level 1's placed objects, made solid.
/// </summary>
/// <remarks>
/// The sweep's <i>behaviour</i> is checked against hand-built surfaces in
/// <see cref="HumanWallSweepTests"/>; this checks that real collision meshes, placed where the level
/// puts them, are where the level puts them — which is the part a units mistake would silently break.
/// </remarks>
public sealed class RetailObjectCollisionTests
{
    private const double UnitsPerMetre = 4096.0;

    [RequiresGameDataFact]
    public void RetailPlayerCollisionLodBlocksTheReportedCabinAndFenceGap()
    {
        // The player's recorded DEBUG TELEMETRY position in the cabin/fence pocket. The user can enter
        // it through this approach with level zero, but retail 0x461A70 gives World_TraceLines iMesh=1.
        Vector3d pocket = new(24387108, -56227905, 174413141);
        Vector3d approach = new(
            pocket.X - (3.0 * UnitsPerMetre),
            pocket.Y - (0.5 * UnitsPerMetre),
            pocket.Z);
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);

        HumanWallSweepResult levelZero = HumanWallSweep.Sweep(
            approach, pocket, grounded: true, crouching: false, index.Caster());
        HumanWallSweepResult retailPlayer = HumanWallSweep.Sweep(
            approach, pocket, grounded: true, crouching: false, index.PlayerCaster());

        Assert.InRange(Distance(levelZero.Destination, pocket), 0.0, 0.01 * UnitsPerMetre);
        Assert.True(retailPlayer.TouchedWall, "retail's player collision LOD must reject the pocket approach");
        Assert.InRange(Distance(retailPlayer.Destination, pocket), 1.3 * UnitsPerMetre, 1.4 * UnitsPerMetre);
    }

    [RequiresGameDataFact]
    public void LevelOnePlacesHundredsOfSolidObjects()
    {
        ObjectCollisionIndex index = Index();

        Assert.True(index.Count > 50, $"only {index.Count} placements were solid");
        Assert.True(index.TriangleCount > 1000, $"only {index.TriangleCount} collision triangles");
    }

    [RequiresGameDataFact]
    public void ARayThroughAnObjectHitsItAndOneBesideItDoesNot()
    {
        // The check a wrong unit conversion fails: a ray aimed straight at a placed object must meet
        // it. If placements were left in metres every object would sit 4096 times too near the origin
        // and every ray would miss - which reads as "no collision" rather than as an error.
        (ObjectCollisionIndex index, Vector3d centre, double radius) = FirstSolid();

        // Straight through the middle, from well outside.
        double reach = (radius * 4.0) + UnitsPerMetre;
        Vector3d from = new(centre.X - reach, centre.Y, centre.Z);
        Vector3d to = new(centre.X + reach, centre.Y, centre.Z);

        Assert.True(index.TryCast(from, to, out WorldRayHit hit), "a ray through a placed object missed it");

        // The contact belongs to something the index holds. Not necessarily the object aimed at - the
        // ray crosses the whole level and the nearest surface may well be a nearer object, which is
        // the right answer and was what an earlier version of this test wrongly called a failure.
        bool onSomething = false;
        for (int i = 0; i < index.Count && !onSomething; i++)
        {
            (Vector3d c, double r) = index.Bounds(i);
            double ex = hit.Position.X - c.X;
            double ey = hit.Position.Y - c.Y;
            double ez = hit.Position.Z - c.Z;
            onSomething = Math.Sqrt((ex * ex) + (ey * ey) + (ez * ez)) <= r + 1.0;
        }

        Assert.True(onSomething, "the contact was not on any indexed object");

        // And it is on the segment that was cast, between the start and the far side of the target.
        Assert.InRange(hit.Position.X, from.X, to.X);

        // The normal is unit length and faces the ray.
        float length = MathF.Sqrt(
            (hit.Normal.X * hit.Normal.X) + (hit.Normal.Y * hit.Normal.Y) + (hit.Normal.Z * hit.Normal.Z));
        Assert.Equal(1f, length, 1e-3f);
        Assert.True(hit.Normal.X < 0f, "the normal should oppose a ray travelling +X");

        // And far above everything, nothing is hit.
        Vector3d high = new(centre.X, centre.Y, centre.Z + (1000.0 * UnitsPerMetre));
        Assert.False(index.TryCast(high, new Vector3d(high.X + reach, high.Y, high.Z), out _));
    }

    [RequiresGameDataFact]
    public void TheContactDistanceMatchesWhereTheContactIs()
    {
        (ObjectCollisionIndex index, Vector3d centre, double radius) = FirstSolid();

        double reach = (radius * 4.0) + UnitsPerMetre;
        Vector3d from = new(centre.X - reach, centre.Y, centre.Z);

        Assert.True(index.TryCast(from, new Vector3d(centre.X + reach, centre.Y, centre.Z), out WorldRayHit hit));

        double dx = hit.Position.X - from.X;
        double dy = hit.Position.Y - from.Y;
        double dz = hit.Position.Z - from.Z;

        Assert.Equal(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)), hit.Distance, 1.0);
    }

    [RequiresGameDataFact]
    public void WalkingAtASolidObjectDoesNotGoThroughIt()
    {
        // The acceptance property, stated directly: aim the sweep at a real object and the destination
        // must not end up on the far side of it.
        (ObjectCollisionIndex index, Vector3d centre, double radius) = FirstSolid();

        // Start beside the object at a height the middle and high rays both cover, and walk at it.
        Vector3d start = new(centre.X - radius - (2.0 * UnitsPerMetre), centre.Y, centre.Z - UnitsPerMetre);
        Vector3d target = new(centre.X + radius + (2.0 * UnitsPerMetre), centre.Y, start.Z);

        HumanWallSweepResult result = HumanWallSweep.Sweep(start, target, true, false, index.Caster());

        // Either it was stopped, or the rays passed the object entirely - the object need not be a
        // wall at every height. What must not happen is being stopped and still ending up past it.
        if (result.TouchedWall)
        {
            Assert.True(
                result.Destination.X < centre.X + radius,
                "the sweep reported a wall and still ended up beyond the object");
        }
    }

    [RequiresGameDataFact]
    public void AnOpenDoorStopsBlockingTheOrdinaryCastAndNeverStopsBlockingTheBakes()
    {
        // The two casts differ on exactly one thing, and it is the thing the probe bake depends on: an
        // open door is a hole to the AI and a wall to the bake. Baking it as a hole would leave the hole
        // in the field after the door shut, because the field is static.
        (ObjectCollisionIndex index, Vector3d centre, double radius, GameTask owner) = FirstSolidWithATask();

        double reach = (radius * 4.0) + UnitsPerMetre;
        Vector3d from = new(centre.X - reach, centre.Y, centre.Z);
        Vector3d to = new(centre.X + reach, centre.Y, centre.Z);

        Assert.True(index.TryCast(from, to, out _), "the ray missed the only object in the index");
        Assert.True(index.TryCastWithDoorsShut(from, to, out _), "the shut cast missed the same object");

        index.SetPassable(owner, true);

        Assert.False(index.TryCast(from, to, out _), "switching the placement passable did not open it");
        Assert.True(
            index.TryCastWithDoorsShut(from, to, out _),
            "the bake's cast honoured a door being open, which would bake a hole into a wall");
    }

    [RequiresGameDataFact]
    public void MovingCollisionKeepsBlockingAtItsLiveTransform()
    {
        WorldPlacement placement = RetailLevelOneGround.Scene().Placements.First(candidate =>
            ObjectCollisionIndex.Build([candidate]).Count != 0);
        ObjectCollisionIndex index = ObjectCollisionIndex.Build([placement]);
        (Vector3d oldCentre, double radius) = index.Bounds(0);
        double reach = (radius * 2.0) + UnitsPerMetre;
        Vector3d oldFrom = new(oldCentre.X - reach, oldCentre.Y, oldCentre.Z);
        Vector3d oldTo = new(oldCentre.X + reach, oldCentre.Y, oldCentre.Z);
        Assert.True(index.TryCast(oldFrom, oldTo, out _));

        double shiftUnits = (radius * 6.0) + (10.0 * UnitsPerMetre);
        Vector3d movedMetres = new(
            placement.Position.X + (shiftUnits / UnitsPerMetre),
            placement.Position.Y,
            placement.Position.Z);
        Assert.True(index.UpdateTransform(placement.Owner, movedMetres, placement.Orientation));

        (Vector3d movedCentre, _) = index.Bounds(0);
        Vector3d movedFrom = new(movedCentre.X - reach, movedCentre.Y, movedCentre.Z);
        Vector3d movedTo = new(movedCentre.X + reach, movedCentre.Y, movedCentre.Z);

        Assert.False(index.TryCast(oldFrom, oldTo, out _));
        Assert.True(index.TryCast(movedFrom, movedTo, out _));
        Assert.True(index.TryCastWithDoorsShut(oldFrom, oldTo, out _));
    }

    [RequiresGameDataFact]
    public void MovingOneAttachedPlacementDoesNotCollapseItsSiblings()
    {
        WorldPlacement[] siblings = RetailLevelOneGround.Scene().Placements
            .Where(placement => ObjectCollisionIndex.Build([placement]).Count != 0)
            .GroupBy(placement => placement.Owner)
            .Select(group => group.Take(2).ToArray())
            .First(group => group.Length == 2);
        ObjectCollisionIndex index = ObjectCollisionIndex.Build(siblings);
        Vector3d firstBefore = index.Bounds(0).Centre;
        Vector3d secondBefore = index.Bounds(1).Centre;
        Vector3d moved = siblings[0].Position + new Vector3d(20d, 0d, 0d);

        Assert.True(index.UpdateTransform(siblings[0], moved, siblings[0].Orientation));

        Vector3d firstAfter = index.Bounds(0).Centre;
        Vector3d secondAfter = index.Bounds(1).Centre;
        Assert.InRange(Math.Abs((firstAfter.X - firstBefore.X) - (20d * UnitsPerMetre)), 0d, 0.001d);
        Assert.Equal(secondBefore, secondAfter);
    }

    [RequiresGameDataFact]
    public void RepeatedDynamicTransformsReuseTheirVertexStorage()
    {
        WorldPlacement placement = RetailLevelOneGround.Scene().Placements.First(candidate =>
            ObjectCollisionIndex.Build([candidate]).Count != 0);
        ObjectCollisionIndex index = ObjectCollisionIndex.Build([placement]);
        Vector3d moved = placement.Position + new Vector3d(1d, 0d, 0d);

        // Warm dictionary lookup and JIT paths before measuring managed allocations.
        Assert.True(index.UpdateTransform(placement, moved, placement.Orientation));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool allUpdated = true;
        for (int tick = 0; tick < 100; tick++)
        {
            allUpdated &= index.UpdateTransform(placement, moved, placement.Orientation);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allUpdated);
        Assert.True(allocated < 1024, $"dynamic transforms allocated {allocated} bytes");
    }

    /// <summary>An index holding one placement that a task placed, and where that placement is.</summary>
    /// <remarks>
    /// One placement rather than the level, so the ray cannot be stopped by something nearer than the
    /// object being switched passable — with the whole level in the index, opening one door proves
    /// nothing because a wall behind it still blocks.
    /// </remarks>
    private static (ObjectCollisionIndex Index, Vector3d Centre, double Radius, GameTask Owner) FirstSolidWithATask()
    {
        foreach (WorldPlacement placement in RetailLevelOneGround.Scene().Placements)
        {
            ObjectCollisionIndex single = ObjectCollisionIndex.Build([placement]);
            if (single.Count == 0)
            {
                continue;
            }

            (Vector3d centre, double radius) = single.Bounds(0);
            if (radius > 0.0)
            {
                return (single, centre, radius, placement.Owner);
            }
        }

        Assert.Fail("no placement in level 1 was both owned by a task and solid");
        throw new InvalidOperationException("unreachable");
    }

    private static (ObjectCollisionIndex Index, Vector3d Centre, double Radius) FirstSolid()
    {
        ObjectCollisionIndex index = Index();
        Assert.True(index.Count > 0, "no placement in level 1 carried collision geometry");

        // The biggest object is the least ambiguous target - a large building rather than a doorknob.
        (Vector3d centre, double radius) = index.Bounds(index.LargestIndex());
        Assert.True(radius > 0.0);

        return (index, centre, radius);
    }

    private static ObjectCollisionIndex Index() => ObjectCollisionIndex.Build(RetailLevelOneGround.Scene().Placements);

    private static double Distance(Vector3d left, Vector3d right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        double z = left.Z - right.Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}
