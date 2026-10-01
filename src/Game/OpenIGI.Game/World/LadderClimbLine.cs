using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A ladder's climb geometry: the segment between its bottom and top, divided into the discrete steps
/// the original climbs it in.
/// </summary>
/// <remarks>
/// <para>
/// The bottom and top are the ladder model's <b>magic vertices 2 and 3</b> in world space — the two
/// points the ladder constructor (<c>sub_440280</c>) reads to size the ladder, and the two the mount
/// (<c>sub_4113E0</c>) and the per-tick climb (IGI2 <c>HumanPlayer_ClimbLadder 0x4A0B30</c>) place the
/// human between.
/// </para>
/// <para>
/// <b>The ladder is climbed in whole steps.</b> The constructor divides the bottom-to-top height by
/// <see cref="StepLengthUnits"/> and truncates, giving <see cref="StepCount"/>; the climb tracks an
/// integer step from 0 (foot) to <see cref="StepCount"/> (head), and the human's position along the
/// ladder is <c>bottom + (step / StepCount) · (top − bottom)</c> — exactly the fraction IGI2's
/// <c>HumanPlayer_ClimbLadder</c> forms.
/// </para>
/// </remarks>
public readonly struct LadderClimbLine
{
    /// <summary>World units per climb step — <c>4096 · 0.3 = 1228.8</c>, one rung's spacing (0.3 m).</summary>
    /// <remarks>
    /// The constructor scales the height by <c>0.0008138020833333334 = 1/1228.8</c> and truncates it
    /// into an integer step count; IGI2's climb uses the same reciprocal (<c>0.0008138020509957453</c>,
    /// the single-precision rounding of the same value).
    /// </remarks>
    public const double StepLengthUnits = 1228.8;

    /// <summary>
    /// How many steps below the top the climb hands off to the get-off-top state — the <c>−6</c> in
    /// <c>sub_40D6B0</c>'s "at the top" test and <c>sub_4113E0</c>'s top-mount step.
    /// </summary>
    public const int TopStepMargin = 6;

    /// <summary>Initializes a new instance of the <see cref="LadderClimbLine"/> struct.</summary>
    /// <param name="bottom">The ladder's foot in world units — magic vertex 2.</param>
    /// <param name="top">The ladder's head in world units — magic vertex 3.</param>
    public LadderClimbLine(Vector3d bottom, Vector3d top)
    {
        Bottom = bottom;
        Top = top;

        // The constructor measures the vertical height and truncates height/1228.8 into an int (fistp),
        // so a 3 m ladder is 10 steps. At least one step, so a degenerate ladder still climbs.
        double height = Math.Abs(top.Z - bottom.Z);
        StepCount = Math.Max(1, (int)(height / StepLengthUnits));
    }

    /// <summary>Gets the ladder's foot in world units (magic vertex 2).</summary>
    public Vector3d Bottom { get; }

    /// <summary>Gets the ladder's head in world units (magic vertex 3).</summary>
    public Vector3d Top { get; }

    /// <summary>Gets the number of steps the ladder is climbed in.</summary>
    public int StepCount { get; }

    /// <summary>Gets the step at which the climb hands off to getting off the top.</summary>
    /// <remarks><see cref="StepCount"/> less <see cref="TopStepMargin"/>, floored at zero.</remarks>
    public int TopStep => Math.Max(0, StepCount - TopStepMargin);

    /// <summary>The human's world position at a given step along the ladder.</summary>
    /// <param name="step">The step, clamped to <c>[0, StepCount]</c>.</param>
    /// <returns>The position, interpolated along the bottom-to-top segment.</returns>
    /// <remarks>
    /// <c>bottom + (step / StepCount) · (top − bottom)</c> — the interpolation IGI2's
    /// <c>HumanPlayer_ClimbLadder</c> forms from the step and the model's stored step count.
    /// </remarks>
    public Vector3d PositionAtStep(int step)
    {
        int clamped = Math.Clamp(step, 0, StepCount);
        double fraction = (double)clamped / StepCount;
        return new Vector3d(
            Bottom.X + ((Top.X - Bottom.X) * fraction),
            Bottom.Y + ((Top.Y - Bottom.Y) * fraction),
            Bottom.Z + ((Top.Z - Bottom.Z) * fraction));
    }
}
