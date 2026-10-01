using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The two angles the first-person rig is turned by relative to the eye, and the ease that drives them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the weapon-change animation, and it is not a clip.</b> Swapping weapons plays no
/// animation of its own: <c>0x40F230</c> asks these two angles to travel to <b>−35°</b> and
/// <b>−20°</b>, which tips the whole rig down and aside until it is off the bottom of the screen; the
/// model is exchanged the moment they arrive; and <c>0x40F280</c> sends them back to zero. The same
/// lowering is shared by peeking, throwing and placing.
/// </para>
/// <para>
/// The four fields are the original's <c>HumanView+460</c> and <c>+472</c> (current), <c>+464</c> and
/// <c>+476</c> (target) and <c>+468</c> and <c>+480</c> (rate). <c>0x464770</c> sets a target and a
/// rate, <c>0x4647A0</c> snaps both ends together, <c>0x4647D0</c> reports arrival, and
/// <c>0x464BD0</c> steps them once a tick.
/// </para>
/// <para>
/// <b>Both angles arrive together.</b> <c>35° / 0.0581776</c> and <c>20° / 0.0332444</c> are both
/// <b>10.5 ticks</b>, so the pair is one motion rather than two — the rates were chosen to make it so,
/// and a port that picked its own would tip the weapon crooked on the way down.
/// </para>
/// </remarks>
public sealed class HumanViewSway
{
    /// <summary>How far the rig tips down while a weapon is being put away — <b>−35°</b>.</summary>
    /// <remarks>Spelled <c>-0.61086524</c>.</remarks>
    public const float LoweredPitch = -0.61086524f;

    /// <summary>How far it turns aside at the same time — <b>−20°</b>.</summary>
    /// <remarks>Spelled as the bit pattern <c>0xBEB2B8C2</c>, which is <c>-0.34906587</c>.</remarks>
    public const float LoweredYaw = -0.34906587f;

    /// <summary>How much of the pitch one tick covers.</summary>
    /// <remarks><c>0.058177643</c> — <see cref="LoweredPitch"/> over 10.5 ticks.</remarks>
    public const float PitchRate = 0.058177643f;

    /// <summary>How much of the yaw one tick covers.</summary>
    /// <remarks><c>0.033244368</c> — <see cref="LoweredYaw"/> over the same 10.5 ticks.</remarks>
    public const float YawRate = 0.033244368f;

    /// <summary>How many ticks a full lowering or raising takes.</summary>
    /// <remarks>
    /// Eleven: the travel is 10.5 ticks and the step snaps on overshoot, so the eleventh tick is the
    /// one that arrives. A swap is two of these, about <b>0.73 s</b> in all.
    /// </remarks>
    public const int TicksToTravel = 11;

    /// <summary>Gets the pitch the rig is turned by now, in radians.</summary>
    public float Pitch { get; private set; }

    /// <summary>Gets the yaw the rig is turned by now, in radians.</summary>
    public float Yaw { get; private set; }

    /// <summary>Gets the pitch being travelled to.</summary>
    public float TargetPitch { get; private set; }

    /// <summary>Gets the yaw being travelled to.</summary>
    public float TargetYaw { get; private set; }

    /// <summary>Gets how much of the pitch one tick covers.</summary>
    public float PitchStep { get; private set; }

    /// <summary>Gets how much of the yaw one tick covers.</summary>
    public float YawStep { get; private set; }

    /// <summary>Gets whether both angles have arrived — the original's <c>0x4647D0</c>.</summary>
    /// <remarks>
    /// An <b>exact</b> equality, which is safe only because <see cref="Advance"/> snaps to the target on
    /// overshoot rather than approaching it. Every weapon state that lowers the rig gates on this.
    /// </remarks>
    public bool Settled => Pitch == TargetPitch && Yaw == TargetYaw;

    /// <summary>Sends both angles somewhere, at a rate each — the original's <c>0x464770</c>.</summary>
    /// <param name="targetPitch">Where the pitch is going.</param>
    /// <param name="targetYaw">Where the yaw is going.</param>
    /// <param name="pitchStep">How much of the pitch a tick covers; the sign is ignored.</param>
    /// <param name="yawStep">How much of the yaw a tick covers; the sign is ignored.</param>
    public void MoveTo(float targetPitch, float targetYaw, float pitchStep, float yawStep)
    {
        TargetPitch = targetPitch;
        TargetYaw = targetYaw;
        PitchStep = pitchStep;
        YawStep = yawStep;
    }

    /// <summary>Puts both angles somewhere at once — the original's <c>0x4647A0</c>.</summary>
    /// <param name="pitch">The pitch.</param>
    /// <param name="yaw">The yaw.</param>
    /// <remarks>
    /// Sets the current value <i>and</i> the target, so <see cref="Settled"/> is true immediately. The
    /// throw and the mine use it to start already lowered rather than easing into it.
    /// </remarks>
    public void SnapTo(float pitch, float yaw)
    {
        Pitch = pitch;
        Yaw = yaw;
        TargetPitch = pitch;
        TargetYaw = yaw;
    }

    /// <summary>Lowers the rig out of the frame, at the original's rates.</summary>
    public void Lower() => MoveTo(LoweredPitch, LoweredYaw, PitchRate, YawRate);

    /// <summary>Raises the rig back to the eye, at the same rates.</summary>
    public void Raise() => MoveTo(0f, 0f, PitchRate, YawRate);

    /// <summary>Steps both angles one tick — the block inside <c>0x464BD0</c>.</summary>
    /// <remarks>
    /// The whole step is skipped when both have arrived, and each angle snaps to its target the moment
    /// it would pass it. That snap is what makes <see cref="Settled"/>'s exact comparison correct.
    /// </remarks>
    public void Advance()
    {
        if (Settled)
        {
            return;
        }

        Pitch = Step(Pitch, TargetPitch, PitchStep);
        Yaw = Step(Yaw, TargetYaw, YawStep);
    }

    /// <summary>Builds the rotation the rig is turned by, relative to the eye.</summary>
    /// <returns>
    /// <c>FromEngineEulerAngles(pitch, 0, yaw)</c> — the same builder and the same argument order
    /// <c>0x464BD0</c> hands <c>0x4B38E0</c>.
    /// </returns>
    /// <remarks>
    /// The view object's own 3×3 is <c>eyeOrientation × this</c>, dotting the eye's rows with this
    /// matrix's columns, which is what <c>0x464BD0</c>'s nine multiply-adds spell out.
    /// </remarks>
    public Matrix3f ToMatrix() => Matrix3f.FromEngineEulerAngles(Pitch, 0f, Yaw);

    private static float Step(float current, float target, float step)
    {
        if (current == target)
        {
            return current;
        }

        if (current < target)
        {
            float raised = current + step;
            return raised > target ? target : raised;
        }

        float lowered = current - step;
        return lowered < target ? target : lowered;
    }
}
