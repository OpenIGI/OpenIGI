namespace OpenIGI.Game.Player;

/// <summary>The first-person wall-clearance offset <c>0x464BD0</c> maintains at <c>HumanView+804</c>.</summary>
/// <remarks>
/// The retail view casts 2,048 world units (half a metre) straight out of the eye. A contact pulls the
/// rig, its arms, and its held weapon back by the unoccupied part of that segment; it never moves the
/// camera itself. The two deliberately different fixed-tick rates below are the values read from
/// <c>flt_5339D4</c> and <c>flt_5339D8</c>: a weapon retreats faster than it returns to the ready pose.
/// </remarks>
public static class HumanViewWallPullback
{
    /// <summary>The forward clearance probe's retail length, in world units (0.5 m).</summary>
    public const double ProbeDistanceInUnits = 2048.0;

    /// <summary>How far the rig retreats toward a blocker on each 30 Hz tick.</summary>
    public const double PullInStepInUnits = 113.77778;

    /// <summary>How far the rig returns to its ready position on each 30 Hz tick.</summary>
    public const double ReturnStepInUnits = 75.851852;

    /// <summary>Gets the desired signed rig offset for a hit along the probe.</summary>
    /// <param name="hitDistance">The contact's distance from the eye, in world units.</param>
    /// <returns>A non-positive distance along the eye's forward vector.</returns>
    /// <remarks>
    /// This is the literal <c>-(2048 - hitDistance)</c> expression in <c>0x464BD0</c>. The collision
    /// implementation owns the segment bounds, so this method intentionally does not invent a skin
    /// width or a clamp the retail code does not contain.
    /// </remarks>
    public static double TargetForHit(double hitDistance) => -(ProbeDistanceInUnits - hitDistance);

    /// <summary>Moves the present offset one retail fixed tick toward the desired offset.</summary>
    /// <param name="current">The prior signed offset.</param>
    /// <param name="target">Zero in open space, otherwise <see cref="TargetForHit"/>.</param>
    /// <returns>The next signed offset, clamped at the target without overshooting.</returns>
    public static double Advance(double current, double target)
    {
        if (current == target)
        {
            return current;
        }

        double step = target == 0.0 ? ReturnStepInUnits : PullInStepInUnits;
        return current < target
            ? Math.Min(current + step, target)
            : Math.Max(current - step, target);
    }
}
