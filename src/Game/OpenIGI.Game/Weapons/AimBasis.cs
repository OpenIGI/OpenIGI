using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// Turns a shot's aiming frame to point at a place, without disturbing anything else about it.
/// </summary>
/// <remarks>
/// <para>
/// A human fires along the middle column of its view's 3×3 (<c>0x462E50</c>), with the spread cone
/// opening across the other two. That is exactly right when the eye and the camera are the same point,
/// which in the original they always are. A third-person camera sits behind and to the side of the eye
/// and so has a different sight line, and no fixed crosshair can be true of both — so the shot is turned
/// to agree with the camera instead of the crosshair being moved to agree with the shot.
/// </para>
/// <para>
/// This is deliberately the whole of that change, in one place, so it can be read and so it can be shown
/// to be inert everywhere else: <see cref="Converge"/> with no target, or with a target on the eye's own
/// forward, returns the basis it was handed unchanged.
/// </para>
/// </remarks>
public static class AimBasis
{
    /// <summary>How far in front of the eye a convergence target must be, in engine units.</summary>
    /// <remarks>
    /// Half a metre. The camera traces from behind the player, so it can meet a wall the player is
    /// backed against — a point beside or behind the eye — and pointing the shot at that would fire the
    /// round sideways or into the player's own back. Anything nearer than this is refused and the shot
    /// keeps the eye's own forward, which is the first-person answer and always a safe one.
    /// </remarks>
    public const double MinimumReachInUnits = 0.5 * 4096.0;

    /// <summary>Points an aiming frame at a world position, keeping its roll.</summary>
    /// <param name="eyeUnits">Where the round leaves, in engine units. Not moved.</param>
    /// <param name="targetUnits">
    /// The world point to converge on, or <see langword="null"/> to leave the frame alone.
    /// </param>
    /// <param name="forward">The barrel axis, turned onto the target.</param>
    /// <param name="right">The cone's first axis, re-squared against the new forward.</param>
    /// <param name="up">The cone's second axis, rebuilt from the other two.</param>
    /// <returns><see langword="true"/> when the frame was actually turned.</returns>
    /// <remarks>
    /// <para>
    /// The two cone axes are <b>re-squared rather than rebuilt from scratch</b> — Gram-Schmidt against
    /// the new forward, then a cross product — so the frame keeps the roll it had and stays
    /// right-handed. Building them from a world up instead would spin the spread pattern about the
    /// barrel as the player looked around, which is not visible in a round but is in a shotgun.
    /// </para>
    /// <para>
    /// Every refusal path leaves all three arguments exactly as they arrived: no target, a degenerate
    /// one, a target too close, or a target so nearly on the existing forward that squaring the frame
    /// would divide by nothing.
    /// </para>
    /// </remarks>
    public static bool Converge(
        Vector3d eyeUnits,
        Vector3d? targetUnits,
        ref Vector3f forward,
        ref Vector3f right,
        ref Vector3f up)
    {
        if (targetUnits is not { } target)
        {
            return false;
        }

        Vector3d offset = new(target.X - eyeUnits.X, target.Y - eyeUnits.Y, target.Z - eyeUnits.Z);
        double reach = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y) + (offset.Z * offset.Z));
        if (!double.IsFinite(reach) || reach < MinimumReachInUnits)
        {
            return false;
        }

        Vector3f aim = new(
            (float)(offset.X / reach),
            (float)(offset.Y / reach),
            (float)(offset.Z / reach));

        // Behind the shoulder is not in front of the eye. A camera pressed against a wall can find one,
        // and firing at it would send the round back through the player.
        if (Vector3f.Dot(aim, forward) <= 0f)
        {
            return false;
        }

        float alongForward = Vector3f.Dot(right, aim);
        Vector3f squared = new(
            right.X - (aim.X * alongForward),
            right.Y - (aim.Y * alongForward),
            right.Z - (aim.Z * alongForward));

        float length = MathF.Sqrt(Vector3f.Dot(squared, squared));
        if (length <= 1e-4f)
        {
            // The barrel has turned onto the frame's own right axis, which no aim reachable from a
            // camera two metres behind the eye can do. Keep the frame rather than normalise a zero.
            return false;
        }

        squared = new Vector3f(squared.X / length, squared.Y / length, squared.Z / length);

        forward = aim;
        right = squared;
        up = Vector3f.Cross(squared, aim);
        return true;
    }

}
