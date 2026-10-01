using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The world-collision sweep: three probe rays along the direction of travel, and the rules that turn
/// a hit into a slide.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x461A70</c>, the first of the movement integrator's three passes. Each iteration casts
/// <b>three rays from the start position along the direction of travel</b>, at three heights above the
/// feet, with length <c>|delta| + </c><see cref="SkinInUnits"/>. The nearest hit that counts as a wall
/// is resolved by projecting the destination onto the wall plane and pushing it out, and then the whole
/// sweep runs again — up to <see cref="MaximumIterations"/> times.
/// </para>
/// </remarks>
public static class HumanWallProbe
{
    /// <summary>The skin width added to every sweep and used as the push-out distance — 2.5 mm.</summary>
    public const double SkinInUnits = 10.24;

    /// <summary>The lowest probe's height above the feet — 0.1 m. Skipped while grounded.</summary>
    public const double LowProbeInUnits = 409.6;

    /// <summary>The middle probe's height while standing — 0.4 m.</summary>
    public const double MiddleProbeStandingInUnits = 1638.4;

    /// <summary>The middle probe's height while crouching — 0.2 m.</summary>
    public const double MiddleProbeCrouchingInUnits = 819.2;

    /// <summary>The highest probe's height while standing — 1.8 m.</summary>
    public const double HighProbeStandingInUnits = 7372.8;

    /// <summary>The highest probe's height while crouching — 1.3 m.</summary>
    public const double HighProbeCrouchingInUnits = 5324.8;

    /// <summary>
    /// A hit is a wall when the magnitude of its normal's Z is below this — anything more upright is
    /// floor or ceiling and is handled separately.
    /// </summary>
    public const float WallNormalZLimit = 0.1f;

    /// <summary>
    /// How many slide-and-resweep iterations are allowed. On the eleventh the move is abandoned
    /// entirely and the human stays put.
    /// </summary>
    public const int MaximumIterations = 10;

    /// <summary>Gets the three probe heights above the feet, in engine units.</summary>
    /// <param name="crouching">The human's crouch flag (<c>Human+829</c>).</param>
    /// <returns>The heights, lowest first.</returns>
    /// <remarks>
    /// Crouching lowers the upper two — 0.4 m becomes 0.2 m and 1.8 m becomes 1.3 m — so a crouched
    /// human fits under obstacles a standing one does not. The lowest probe does not move.
    /// </remarks>
    public static (double Low, double Middle, double High) ProbeHeights(bool crouching) => (
        LowProbeInUnits,
        crouching ? MiddleProbeCrouchingInUnits : MiddleProbeStandingInUnits,
        crouching ? HighProbeCrouchingInUnits : HighProbeStandingInUnits);

    /// <summary>Reports which probes are cast for a given grounded state.</summary>
    /// <param name="grounded">Whether the human is standing on something (flag bit 2).</param>
    /// <returns>The index of the first probe to cast, and how many.</returns>
    /// <remarks>
    /// <b>A grounded human skips the lowest probe entirely</b> and casts only two. That is the step-up
    /// mechanism: at 0.1 m the low ray would catch every kerb and doorstep and refuse the move, so
    /// while you have your feet on something it is simply not consulted. In the air all three are cast.
    /// </remarks>
    public static (int First, int Count) ActiveProbes(bool grounded) => grounded ? (1, 2) : (0, 3);

    /// <summary>Classifies a surface normal as a wall.</summary>
    /// <param name="normal">The hit normal; need not be unit length.</param>
    /// <returns><see langword="true"/> when the surface is vertical enough to slide along.</returns>
    public static bool IsWall(Vector3f normal)
    {
        if ((normal.X == 0f) && (normal.Y == 0f) && (normal.Z == 0f))
        {
            return false;
        }

        return MathF.Abs(UnitZ(normal)) < WallNormalZLimit;
    }

    /// <summary>Gets the Z component of a normal after the original's unit-length conversion.</summary>
    /// <param name="normal">The hit normal; need not already be unit length.</param>
    /// <returns>The unit normal's Z, or zero for a zero-length normal.</returns>
    public static float UnitZ(Vector3f normal)
    {
        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        return length == 0f ? 0f : normal.Z / length;
    }

    /// <summary>Flattens a wall normal to the horizontal plane and renormalises it.</summary>
    /// <param name="normal">The hit normal.</param>
    /// <returns>The horizontal normal the slide is computed against.</returns>
    /// <remarks>
    /// The original zeroes Z and renormalises the remaining two components, so sliding along a wall is
    /// strictly horizontal however the wall is tilted — a human never gets pushed up or down one. A
    /// normal with no horizontal part at all is left alone rather than divided by zero.
    /// </remarks>
    public static Vector3f Flatten(Vector3f normal)
    {
        float horizontal = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y));
        if (horizontal == 0f)
        {
            return new Vector3f(normal.X, normal.Y, 0f);
        }

        float scale = 1f / horizontal;
        return new Vector3f(normal.X * scale, normal.Y * scale, 0f);
    }

    /// <summary>Slides a destination along a wall and pushes it clear.</summary>
    /// <param name="destination">Where the move wanted to end.</param>
    /// <param name="hit">A point on the wall.</param>
    /// <param name="normal">The wall's normal; flattened internally.</param>
    /// <returns>The destination projected onto the wall plane and pushed out by the skin width.</returns>
    public static Vector3d Slide(Vector3d destination, Vector3d hit, Vector3f normal)
    {
        Vector3f flat = Flatten(normal);
        double dx = destination.X - hit.X;
        double dy = destination.Y - hit.Y;
        double dz = destination.Z - hit.Z;

        // Project the offset onto the plane, then re-anchor at the hit and step out along the normal.
        double along = (dx * flat.X) + (dy * flat.Y) + (dz * flat.Z);
        return new Vector3d(
            hit.X + dx - (along * flat.X) + (flat.X * SkinInUnits),
            hit.Y + dy - (along * flat.Y) + (flat.Y * SkinInUnits),
            hit.Z + dz - (along * flat.Z) + (flat.Z * SkinInUnits));
    }
}
