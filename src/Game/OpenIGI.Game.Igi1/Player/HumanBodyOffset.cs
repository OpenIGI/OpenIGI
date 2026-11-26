using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The lean locomotion state 44 eases: the eye's slide to either side and its raise out of a crouch.</summary>
/// <param name="Sideways"><c>HumanView+412</c> (<c>lean[0]</c>), ±argument 4, driven by the strafe slots.</param>
/// <param name="Up"><c>HumanView+420</c> (<c>lean[2]</c>), 0..argument 5, driven by forward while crouched.</param>
public readonly record struct HumanBodyOffsets(float Sideways, float Up);

/// <summary>
/// The lean locomotion state <b>44</b> drives — <c>0x40DFC0</c>.
/// </summary>
/// <remarks>
/// <para>
/// Holding the peek slot puts the weapon machine into its lean (<c>0x40F370</c>), which lowers the weapon and forces the
/// body into state 44. The state's entry (<c>0x40DF70</c>) plays the state's own clip, or the crouch's (slot 19) when the
/// crouch byte <c>Human+829</c> is set, and its per-tick moves nothing: it eases two of the three lean floats on the view
/// (<c>HumanView+412..+420</c>), arguments 4, 5 and 6 of <c>DefineHumanPlayerGeneral</c> being their ranges and time.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Sideways</b> (<c>lean[0]</c>), toward ±<see cref="HumanPlayerParameters.LeanSideUnits"/> while a strafe slot is held and
/// back to zero when neither is. Always live.
/// </description></item>
/// <item><description>
/// <b>Up</b> (<c>lean[2]</c>), toward <see cref="HumanPlayerParameters.LeanUpUnits"/> while forward is held — <b>only while
/// crouched</b>, which is the peek over cover. Standing, this axis is not touched at all.
/// </description></item>
/// </list>
/// <para>
/// The consumer is the view's commit (<c>0x464BD0</c>): it adds <c>lean[0]</c> along the eye's right axis and <c>lean[2]</c>
/// on world Z to the eye, then sweeps the eye from the body's eye to the leaned one and stops it short of anything it meets
/// (<see cref="HumanLeanEye"/>). The weapon state's exit (<c>0x40F3F0</c>) zeroes all three and puts the body back in state 0.
/// Both ease at <c>range / </c><see cref="HumanPlayerParameters.LeanTicks"/> per tick — retail 0.85 m either way in 7 ticks.
/// </para>
/// </remarks>
public static class HumanBodyOffset
{
    /// <summary>The locomotion state whose per-tick this is.</summary>
    /// <remarks>
    /// Entry <c>0x40DF70</c> at <c>0x538664</c>, which is <c>(0x538664 − 0x5385B4) / 4</c> into the
    /// 49-entry function table.
    /// </remarks>
    public const int State = HumanBodyStateMachine.Lean;

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

    /// <summary>Eases the sideways lean one tick, as <c>0x40DFC0</c> does.</summary>
    /// <param name="current">Where the lean is now.</param>
    /// <param name="left">Whether the strafe-left slot is held.</param>
    /// <param name="right">Whether strafe-right is held.</param>
    /// <param name="range">Argument 4, in world units.</param>
    /// <param name="rate">The per-tick step from <see cref="Rate"/>.</param>
    /// <returns>The lean after the tick.</returns>
    /// <remarks>
    /// Not one target but up to two eases in a row: left toward <c>-range</c>, then right toward <c>+range</c>, and back toward
    /// zero only when neither is held. Holding both therefore steps out and straight back again, so a lean held to one side
    /// stays there and one at rest stays at rest.
    /// </remarks>
    public static float Sideways(float current, bool left, bool right, float range, float rate)
    {
        if (left)
        {
            current = Ease(current, -range, rate);
        }

        if (right)
        {
            current = Ease(current, range, rate);
        }

        return left || right ? current : Ease(current, 0f, rate);
    }

    /// <summary>The raise's target for a tick.</summary>
    /// <param name="forward">Whether the forward slot is held.</param>
    /// <param name="crouching">Whether <c>Human+829</c> is set.</param>
    /// <param name="range">Argument 5, in world units.</param>
    /// <returns>The range while crouched and moving forward, zero otherwise.</returns>
    /// <remarks>
    /// <b>Standing, this axis is not driven at all</b> — the whole block sits behind the crouch byte, so
    /// a standing body's raise holds whatever it had rather than easing back to zero.
    /// </remarks>
    public static float UpTarget(bool forward, bool crouching, float range) =>
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

        float sideways = Sideways(
            offset.Sideways, left, right, parameters.LeanSideUnits, Rate(parameters.LeanSideUnits, parameters.LeanTicks));

        if (!crouching)
        {
            return new HumanBodyOffsets(sideways, offset.Up);
        }

        float up = Ease(
            offset.Up,
            UpTarget(forward, crouching, parameters.LeanUpUnits),
            Rate(parameters.LeanUpUnits, parameters.LeanTicks));

        return new HumanBodyOffsets(sideways, up);
    }
}
