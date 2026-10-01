namespace OpenIGI.Game.Player;

/// <summary>The pair of offsets locomotion state 44 eases.</summary>
/// <param name="Sideways"><c>model+412</c>, ±argument 4, driven by the strafe slots.</param>
/// <param name="Forward"><c>model+420</c>, 0..argument 5, driven by forward while crouched.</param>
public readonly record struct HumanBodyOffsets(float Sideways, float Forward);

/// <summary>
/// The two body offsets locomotion state <b>44</b> drives — <c>0x40DFC0</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is what consumes general arguments <b>4</b>, <b>5</b> and <b>6</b> of
/// <c>DefineHumanPlayerGeneral</c>, which had been named as "two 0.85 m distances and a 7-tick timer"
/// without a consumer. They are not distances travelled: they are the <b>range</b> of two offsets and
/// the <b>time</b> either takes to cover it.
/// </para>
/// <para>
/// State 44's entry (<c>0x40DF70</c>) is the ordinary three-line shape — play a clip, install the
/// per-tick — except that it plays <b>animation 19</b> when the crouch byte <c>Human+829</c> is set and
/// the state's own clip otherwise. Its per-tick then eases two floats on the model instance at
/// <c>Human+1260</c>:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Sideways</b> (<c>model+412</c>), toward <c>±</c><see cref="HumanPlayerParameters.Distance4"/> while
/// the strafe slots are held and back to zero when they are not. Always live.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Forward</b> (<c>model+420</c>), toward <see cref="HumanPlayerParameters.Distance5"/> while forward
/// is held — but <b>only while crouched</b>. Standing, this axis is not touched at all.
/// </description>
/// </item>
/// </list>
/// <para>
/// Both ease at <c>range / </c><see cref="HumanPlayerParameters.Duration6Ticks"/> per tick, and the rate
/// is recomputed on the <b>edge</b> — the tick the key goes down, or the tick the last one comes up —
/// from the previous input frame at <c>Human+272</c> against the current at <c>+432</c>. Retail: 0.85 m
/// either way, reached in 7 ticks, so a quarter of a second.
/// </para>
/// <para>
/// <b>What the offsets do to the body is not established.</b> They are written to the model instance,
/// not to the human's position, and what reads <c>model+412</c> and <c>+420</c> has not been traced. A
/// lean is what a 0.85 m sideways offset gated behind a crouch looks like, and state 44 sits among the
/// self-driving interactive states — but that is an inference and it is recorded as one. The easing
/// itself is transcribed exactly.
/// </para>
/// </remarks>
public static class HumanBodyOffset
{
    /// <summary>The locomotion state whose per-tick this is.</summary>
    /// <remarks>
    /// Entry <c>0x40DF70</c> at <c>0x538664</c>, which is <c>(0x538664 − 0x5385B4) / 4</c> into the
    /// 49-entry function table.
    /// </remarks>
    public const int State = 44;

    /// <summary>The animation the entry plays instead of the state's own while crouched.</summary>
    public const int CrouchedAnimationId = 19;

    /// <summary>How far the eased value moves in one tick.</summary>
    /// <param name="range">The offset's full range — argument 4 or 5, in world units.</param>
    /// <param name="ticks">Argument 6, the ticks a full traverse takes; 7 in retail.</param>
    /// <returns>The per-tick step.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is not positive.</exception>
    /// <remarks>
    /// The original recomputes this on every edge rather than holding it constant, which matters only
    /// if the range or the tick count could change mid-move. Neither can — both are script constants.
    /// </remarks>
    public static float Rate(float range, int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        return range / ticks;
    }

    /// <summary>Eases one offset one tick toward its target.</summary>
    /// <param name="current">Where the offset is now.</param>
    /// <param name="target">Where it is heading — <c>±range</c> or zero.</param>
    /// <param name="rate">The per-tick step from <see cref="Rate"/>.</param>
    /// <returns>The offset after the tick, clamped so it never overshoots.</returns>
    /// <remarks>
    /// The clamp is the original's, and it is done the long way round — move, then test whether the
    /// move went past, then snap. That is the same as clamping the step, but it is worth stating
    /// because it means the offset lands <i>exactly</i> on the target rather than near it.
    /// </remarks>
    public static float Ease(float current, float target, float rate)
    {
        if (current < target)
        {
            float moved = current + rate;
            return moved > target ? target : moved;
        }

        if (current > target)
        {
            float moved = current - rate;
            return moved < target ? target : moved;
        }

        return current;
    }

    /// <summary>The sideways offset's target for a tick.</summary>
    /// <param name="left">Whether the strafe-left slot is held.</param>
    /// <param name="right">Whether strafe-right is held.</param>
    /// <param name="range">Argument 4, in world units.</param>
    /// <returns>Negative for left, positive for right, zero for neither or both.</returns>
    /// <remarks>
    /// Left is tested first and right is <i>added</i> to its result — <c>v13 = Speed3 + v13</c> in the
    /// sibling integrator and the same shape here — so holding both cancels rather than one winning.
    /// </remarks>
    public static float SidewaysTarget(bool left, bool right, float range)
    {
        float target = 0f;

        if (left)
        {
            target = -range;
        }

        if (right)
        {
            target += range;
        }

        return target;
    }

    /// <summary>The forward offset's target for a tick.</summary>
    /// <param name="forward">Whether the forward slot is held.</param>
    /// <param name="crouching">Whether <c>Human+829</c> is set.</param>
    /// <param name="range">Argument 5, in world units.</param>
    /// <returns>The range while crouched and moving forward, zero otherwise.</returns>
    /// <remarks>
    /// <b>Standing, this axis is not driven at all</b> — the whole block sits behind the crouch byte, so
    /// a standing body's forward offset holds whatever it had rather than easing back to zero.
    /// </remarks>
    public static float ForwardTarget(bool forward, bool crouching, float range) =>
        crouching && forward ? range : 0f;

    /// <summary>Advances both offsets one tick.</summary>
    /// <param name="offset">The current pair.</param>
    /// <param name="left">Whether strafe-left is held.</param>
    /// <param name="right">Whether strafe-right is held.</param>
    /// <param name="forward">Whether forward is held.</param>
    /// <param name="crouching">Whether the body is crouched.</param>
    /// <param name="parameters">The script's tuning, for arguments 4, 5 and 6.</param>
    /// <returns>The offsets after the tick.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
    public static HumanBodyOffsets Advance(
        HumanBodyOffsets offset,
        bool left,
        bool right,
        bool forward,
        bool crouching,
        HumanPlayerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        float sideways = Ease(
            offset.Sideways,
            SidewaysTarget(left, right, parameters.Distance4),
            Rate(parameters.Distance4, parameters.Duration6Ticks));

        if (!crouching)
        {
            return new HumanBodyOffsets(sideways, offset.Forward);
        }

        float ahead = Ease(
            offset.Forward,
            ForwardTarget(forward, crouching, parameters.Distance5),
            Rate(parameters.Distance5, parameters.Duration6Ticks));

        return new HumanBodyOffsets(sideways, ahead);
    }
}
