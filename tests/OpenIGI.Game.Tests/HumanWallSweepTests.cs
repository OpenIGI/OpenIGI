using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Sliding along walls — <c>0x461A70</c>.
/// </summary>
/// <remarks>
/// The surfaces here are built by hand rather than loaded, because what is being checked is the
/// <i>response</i> — which surfaces count as walls, what a slide does to the destination, when the loop
/// gives up — and that is the part transcribed from the executable. Whether a ray meets a triangle is
/// ordinary geometry and is checked against the retail meshes in <see cref="RetailObjectCollisionTests"/>.
/// </remarks>
public sealed class HumanWallSweepTests
{
    private const double Metre = 4096.0;

    [Fact]
    public void AMoveIntoNothingIsLeftAlone()
    {
        Vector3d start = new(0.0, 0.0, 0.0);
        Vector3d target = new(1000.0, 0.0, 0.0);

        HumanWallSweepResult result = HumanWallSweep.Sweep(start, target, true, false, Miss);

        Assert.Equal(target, result.Destination);
        Assert.False(result.TouchedWall);
    }

    [Fact]
    public void AWallStopsTheMoveIntoItAndLeavesTheMoveAlongIt()
    {
        // A wall in the YZ plane at x = 1 m, facing back down -X.
        WorldRayCast wall = Plane(new Vector3f(-1f, 0f, 0f), new Vector3d(Metre, 0.0, 0.0));

        // Walking straight into it: the destination lands on the wall, not past it.
        HumanWallSweepResult head = HumanWallSweep.Sweep(
            new Vector3d(0.0, 0.0, 0.0), new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, wall);

        Assert.True(head.TouchedWall);
        Assert.InRange(head.Destination.X, Metre - 100.0, Metre + 100.0);

        // Walking at 45 degrees into it: X is stopped but Y is kept in full. This is the property that
        // makes a corridor walkable - being stopped by a wall must not stop you moving along it.
        HumanWallSweepResult angled = HumanWallSweep.Sweep(
            new Vector3d(0.0, 0.0, 0.0), new Vector3d(2.0 * Metre, 2.0 * Metre, 0.0), true, false, wall);

        Assert.True(angled.TouchedWall);
        Assert.InRange(angled.Destination.X, Metre - 100.0, Metre + 100.0);
        Assert.Equal(2.0 * Metre, angled.Destination.Y, 1.0);
    }

    [Fact]
    public void AFloorIsNotAWallAndDoesNotStopAnything()
    {
        // |normal.z| >= 0.1 is not a wall. A ramp at 45 degrees is walked straight through by this
        // sweep - the ground query handles it, and treating it as a wall would make every slope solid.
        WorldRayCast ramp = Plane(new Vector3f(0f, 0f, 1f), default);
        WorldRayCast gentle = Plane(Unit(-0.7f, 0f, 0.7f), new Vector3d(Metre, 0.0, 0.0));

        Assert.False(HumanWallSweep.Sweep(default, new Vector3d(Metre, 0.0, 0.0), true, false, ramp).TouchedWall);
        Assert.False(HumanWallSweep.Sweep(default, new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, gentle).TouchedWall);

        // Just past the limit it is a wall: 0.1 is the whole of the test.
        WorldRayCast steep = Plane(Unit(-1f, 0f, 0.05f), new Vector3d(Metre, 0.0, 0.0));
        Assert.True(HumanWallSweep.Sweep(default, new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, steep).TouchedWall);
    }

    [Fact]
    public void ALeaningWallSlidesHorizontallyAndNeverLiftsTheBody()
    {
        // A wall's normal has its Z flattened before it is used, so sliding along an overhanging or
        // leaning wall keeps the body at its own height. Without the flatten the player rides up it.
        WorldRayCast leaning = Plane(Unit(-1f, 0f, 0.08f), new Vector3d(Metre, 0.0, 0.0));

        HumanWallSweepResult result = HumanWallSweep.Sweep(
            new Vector3d(0.0, 0.0, 0.0), new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, leaning);

        Assert.True(result.TouchedWall);
        Assert.Equal(0.0, result.Destination.Z, 1.0);
    }

    [Fact]
    public void BrushingAWallOnTheWayOutIsNotACollision()
    {
        // The dot-product guard: only a wall you are moving into stops you. This surface is squarely in
        // the ray's path - it is hit - but its normal points the way the move is going, so the body is
        // leaving it rather than entering. Stopping here would glue the body to every surface it left.
        WorldRayCast behind = Plane(new Vector3f(1f, 0f, 0f), new Vector3d(Metre, 0.0, 0.0));

        HumanWallSweepResult result = HumanWallSweep.Sweep(
            new Vector3d(0.0, 0.0, 0.0), new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, behind);

        Assert.False(result.TouchedWall);
        Assert.Equal(2.0 * Metre, result.Destination.X, 1e-6);
    }

    [Fact]
    public void TheAnkleRayIsLiveOnlyWhileAirborne()
    {
        // On the ground the low probe is skipped so you walk over kerbs; in the air it is live so you
        // cannot drift through one.
        WorldRayCast kerb = PlaneBelow(new Vector3f(-1f, 0f, 0f), new Vector3d(Metre, 0.0, 0.0), HumanWallProbe.LowProbeInUnits + 1.0);

        Assert.False(HumanWallSweep.Sweep(default, new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, kerb).TouchedWall);
        Assert.True(HumanWallSweep.Sweep(default, new Vector3d(2.0 * Metre, 0.0, 0.0), false, false, kerb).TouchedWall);
    }

    [Fact]
    public void CrouchingLowersTheTwoUpperRays()
    {
        // A bar at 1.5 m is a wall to someone standing (high ray at 1.8 m) and not to someone crouched
        // (high ray at 1.3 m).
        const double bar = 1.5 * Metre;
        WorldRayCast overhead = PlaneAbove(new Vector3f(-1f, 0f, 0f), new Vector3d(Metre, 0.0, 0.0), bar);

        Assert.True(HumanWallSweep.Sweep(default, new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, overhead).TouchedWall);
        Assert.False(HumanWallSweep.Sweep(default, new Vector3d(2.0 * Metre, 0.0, 0.0), true, true, overhead).TouchedWall);
    }

    [Fact]
    public void AWedgeTooTightToResolveLeavesTheBodyWhereItStarted()
    {
        // Ten casts and 0x461A70 gives up - and when it does it reverts the whole move rather than
        // leaving the body part-way into geometry.
        Vector3d start = new(0.0, 0.0, 0.0);

        HumanWallSweepResult result = HumanWallSweep.Sweep(
            start, new Vector3d(2.0 * Metre, 0.0, 0.0), true, false, Wedge);

        Assert.Equal(start, result.Destination);
        Assert.True(result.Checks > HumanWallProbe.MaximumIterations);
    }

    private static bool Miss(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        hit = default;
        return false;
    }

    /// <summary>A wall that always deflects the move onto a fresh wall, so the loop can never settle.</summary>
    private static bool Wedge(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        Vector3d motion = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((motion.X * motion.X) + (motion.Y * motion.Y) + (motion.Z * motion.Z));

        if (length <= 0.0)
        {
            hit = default;
            return false;
        }

        // The normal always opposes whatever direction the move currently has.
        Vector3f normal = Unit((float)(-motion.X / length), (float)(-motion.Y / length), 0f);
        hit = new WorldRayHit(new Vector3d(origin.X, origin.Y, origin.Z), normal, 0.0);
        return true;
    }

    /// <summary>An infinite plane through <paramref name="through"/> with the given normal.</summary>
    private static WorldRayCast Plane(Vector3f normal, Vector3d through) =>
        (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            Cast(origin, end, normal, through, double.MinValue, double.MaxValue, out hit);

    /// <summary>A plane that exists only below a height, like a kerb.</summary>
    private static WorldRayCast PlaneBelow(Vector3f normal, Vector3d through, double top) =>
        (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            Cast(origin, end, normal, through, double.MinValue, top, out hit);

    /// <summary>A plane that exists only above a height, like a low bar.</summary>
    private static WorldRayCast PlaneAbove(Vector3f normal, Vector3d through, double bottom) =>
        (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            Cast(origin, end, normal, through, bottom, double.MaxValue, out hit);

    private static bool Cast(
        Vector3d origin,
        Vector3d end,
        Vector3f normal,
        Vector3d through,
        double lowest,
        double highest,
        out WorldRayHit hit)
    {
        hit = default;

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double denominator = (direction.X * normal.X) + (direction.Y * normal.Y) + (direction.Z * normal.Z);

        if (Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        double numerator = ((through.X - origin.X) * normal.X)
            + ((through.Y - origin.Y) * normal.Y)
            + ((through.Z - origin.Z) * normal.Z);
        double t = numerator / denominator;

        // Exclusive at the far end, as the real caster is - see RayTriangle. A surface exactly at the
        // ray's tip is the one the body is already resting against.
        if (t < 0.0 || t >= 1.0)
        {
            return false;
        }

        Vector3d point = new(
            origin.X + (direction.X * t),
            origin.Y + (direction.Y * t),
            origin.Z + (direction.Z * t));

        if (point.Z < lowest || point.Z > highest)
        {
            return false;
        }

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

        hit = new WorldRayHit(point, normal, t * length);
        return true;
    }

    private static Vector3f Unit(float x, float y, float z)
    {
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        return new Vector3f(x / length, y / length, z / length);
    }
}
