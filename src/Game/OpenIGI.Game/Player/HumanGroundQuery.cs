namespace OpenIGI.Game.Player;

/// <summary>
/// The ground query — what a human is standing on, how far below it is, and how far it is willing to
/// drop to stay attached.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x462390</c>, the second of the movement integrator's three passes. It casts <b>downwards</b>
/// from a point above the feet, and if the ground it finds is within the caller's step-down budget it
/// <b>snaps the human onto it</b> and raises the on-ground flag. Beyond that budget the human is
/// declared airborne instead.
/// </para>
/// <para>
/// The budget is the interesting part: it is not a constant, and the caller (<c>0x4614B0</c>) picks it
/// from the human's current state. See <see cref="StepDownBudget"/>.
/// </para>
/// </remarks>
public static class HumanGroundQuery
{
    /// <summary>The step-down budget while airborne — nothing. Falling never snaps to ground.</summary>
    public const double AirborneStepDownInUnits = 0.0;

    /// <summary>The step-down budget while grounded — 0.5 m.</summary>
    public const double GroundedStepDownInUnits = 2048.0;

    /// <summary>The step-down budget while sliding down a slope — 2 m.</summary>
    public const double SlidingStepDownInUnits = 8192.0;

    /// <summary>The downward probe's origin above the feet while standing — 0.4 m.</summary>
    public const double ProbeOriginStandingInUnits = 1638.4;

    /// <summary>The downward probe's origin above the feet while crouching — 0.2 m.</summary>
    public const double ProbeOriginCrouchingInUnits = 819.2;

    /// <summary>How far above the ground point the material probe starts — 0.05 m.</summary>
    public const double MaterialProbeAboveInUnits = 204.8;

    /// <summary>How far the material probe reaches below its origin — 0.5 m.</summary>
    public const double MaterialProbeDepthInUnits = 2048.0;

    /// <summary>
    /// A ceiling closer than this above the feet raises the roof flag (bit <c>0x10</c>) — 1.8 m, the
    /// same height as the standing collision probe.
    /// </summary>
    public const double RoofClearanceInUnits = 7372.8;

    /// <summary>The factor the horizontal step is scaled by into the second ground-angle field.</summary>
    /// <remarks>Exactly <c>2/π</c>, to four more digits than the binary stores.</remarks>
    public const float GroundAngleScale = 0.63661975f;

    /// <summary>Picks the step-down budget for a human's current state.</summary>
    /// <param name="grounded">Whether the on-ground flag (bit 2) is set.</param>
    /// <param name="sliding">Whether the slope slide is active this tick.</param>
    /// <returns>The furthest the human may drop and still be considered standing.</returns>
    /// <remarks>
    /// <para>
    /// Three values, chosen in that order — sliding wins over merely being grounded:
    /// <b>0 in the air, 0.5 m on the ground, 2 m while sliding</b>.
    /// </para>
    /// <para>
    /// The grounded value is what stops a human falling off every kerb: walking over a 0.3 m drop keeps
    /// the feet planted rather than starting a fall. The sliding value is four times larger, which is
    /// what keeps a human glued to a steep slope instead of bouncing down it in a series of little
    /// falls — the one case where the ground is moving away underneath you fastest.
    /// </para>
    /// </remarks>
    public static double StepDownBudget(bool grounded, bool sliding)
    {
        if (sliding)
        {
            return SlidingStepDownInUnits;
        }

        return grounded ? GroundedStepDownInUnits : AirborneStepDownInUnits;
    }

    /// <summary>Gets the height above the feet the downward probe starts from.</summary>
    /// <param name="crouching">The human's crouch flag.</param>
    /// <returns>The origin height in engine units.</returns>
    /// <remarks>
    /// The same two heights as the wall sweep's middle probe (<see cref="HumanWallProbe"/>), which is
    /// what keeps the two passes agreeing about where a human's legs are.
    /// </remarks>
    public static double ProbeOrigin(bool crouching) =>
        crouching ? ProbeOriginCrouchingInUnits : ProbeOriginStandingInUnits;

    /// <summary>Decides whether a ground hit is close enough to stand on.</summary>
    /// <param name="distanceToGround">
    /// The gap between the feet and the surface, as the original computes it:
    /// <c>end.z - groundZ - footOffset</c>.
    /// </param>
    /// <param name="budget">The value from <see cref="StepDownBudget"/>.</param>
    /// <returns><see langword="true"/> when the human should be snapped down onto the surface.</returns>
    /// <remarks>
    /// The comparison is <c>&lt;=</c>, so a drop exactly equal to the budget still counts as standing.
    /// A negative distance — the surface already above the feet — always counts.
    /// </remarks>
    public static bool IsStandingOn(double distanceToGround, double budget) => distanceToGround <= budget;

    /// <summary>Reports whether a ceiling is close enough to raise the roof flag.</summary>
    /// <param name="distanceToRoof">The gap between the feet and the surface overhead.</param>
    /// <returns><see langword="true"/> when the flag should be set.</returns>
    public static bool IsUnderRoof(double distanceToRoof) => distanceToRoof < RoofClearanceInUnits;
}
