using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The gun's clock: how <c>Rounds/Minute</c> becomes a tick count, and the counters that hang off it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here comes out of one expression the original writes five times over — three times in
/// <c>Gun_Fire</c> (<c>0x478BA0</c>) and twice in the per-tick (<c>0x478640</c>):
/// </para>
/// <code>
/// (__int64)(60.0 / (double)roundsPerMinute * 30.0)
/// </code>
/// <para>
/// It is a <b>double</b> divide and multiply, truncated toward zero by an <c>__int64</c> cast, in that
/// order. Doing the arithmetic in single precision, or rounding instead of truncating, changes the
/// cadence of most of the roster: at 400 rounds a minute the exact value is 4.5 ticks and the gun uses
/// <b>4</b>.
/// </para>
/// </remarks>
public static class GunTiming
{
    /// <summary>Seconds in a minute, as the original's literal.</summary>
    public const double SecondsPerMinute = 60.0;

    /// <summary>Ticks in a second.</summary>
    public const double TicksPerSecond = 30.0;

    /// <summary>How much of the trigger-held ramp one tick adds.</summary>
    /// <remarks>
    /// <c>0.022222223</c> — a forty-fifth, so the ramp saturates after 45 ticks, or a second and a
    /// half. It resets to zero the moment the trigger is let go.
    /// </remarks>
    public const float TriggerRampPerTick = 0.022222223f;

    /// <summary>What the recoil phase is charged to when a round goes off.</summary>
    /// <remarks>
    /// Stored as the bit pattern <c>0x42700000</c>. The per-tick then steps it back toward zero by
    /// <see cref="SecondsPerMinute"/> over <see cref="TicksPerRound"/>, so it reaches zero exactly as
    /// the next round becomes available.
    /// </remarks>
    public const float RecoilPhaseOnFire = 60f;

    /// <summary>The divisor of the first sub-counter's period.</summary>
    public const int FirstSubCounterDivisor = 3;

    /// <summary>The divisor of the second sub-counter's period.</summary>
    public const int SecondSubCounterDivisor = 10;

    /// <summary>What the first sub-counter starts at when a round goes off.</summary>
    public const int FirstSubCounterStart = -2;

    /// <summary>What the second sub-counter starts at when a round goes off.</summary>
    public const int SecondSubCounterStart = -9;

    /// <summary>
    /// The value <see cref="GunState.RoundsInClip"/> holds before a gun has ever been loaded.
    /// </summary>
    /// <remarks>
    /// The per-tick's first act is to replace this with the weapon's <c>Rounds/Clip</c>. It is
    /// <c>-2</c> rather than <c>-1</c> because <c>-1</c> already means "unlimited" elsewhere.
    /// </remarks>
    public const int UnloadedClipSentinel = -2;

    /// <summary>How many ticks one round takes at a given cyclic rate.</summary>
    /// <param name="roundsPerMinute">The weapon's <see cref="WeaponType.RoundsPerMinute"/>.</param>
    /// <returns>The tick count, truncated toward zero.</returns>
    /// <exception cref="DivideByZeroException">Never — a rate of zero yields infinity, which truncates to zero.</exception>
    /// <remarks>
    /// Truncation, not rounding: 400 rounds a minute is 4.5 ticks and gives <b>4</b>, so the gun runs
    /// slightly <i>faster</i> than its stated rate rather than slower.
    /// </remarks>
    public static int TicksPerRound(int roundsPerMinute) =>
        IgiMath.TruncToInt32(SecondsPerMinute / roundsPerMinute * TicksPerSecond);

    /// <summary>How much the recoil phase moves toward zero in one tick.</summary>
    /// <param name="roundsPerMinute">The weapon's cyclic rate.</param>
    /// <returns>The step.</returns>
    /// <remarks>
    /// <b>This one is not truncated.</b> The per-tick divides by the <i>undivided</i> double, so the
    /// phase decays over the exact 4.5 ticks while the cooldown that gates the next round uses the
    /// truncated 4. The two are deliberately out of step by a fraction of a tick.
    /// </remarks>
    public static float RecoilPhaseStep(int roundsPerMinute) =>
        (float)(SecondsPerMinute / (SecondsPerMinute / roundsPerMinute * TicksPerSecond));

    /// <summary>The period of the first sub-counter, in ticks.</summary>
    /// <param name="roundsPerMinute">The weapon's cyclic rate.</param>
    /// <returns>The period. Zero means it steps every tick.</returns>
    /// <remarks>An integer divide of an already-truncated value, so it truncates twice.</remarks>
    public static int FirstSubCounterPeriod(int roundsPerMinute) =>
        TicksPerRound(roundsPerMinute) / FirstSubCounterDivisor;

    /// <summary>The period of the second sub-counter, in ticks.</summary>
    /// <param name="roundsPerMinute">The weapon's cyclic rate.</param>
    /// <returns>The period. Zero means it steps every tick.</returns>
    public static int SecondSubCounterPeriod(int roundsPerMinute) =>
        TicksPerRound(roundsPerMinute) / SecondSubCounterDivisor;

    /// <summary>Advances the trigger-held ramp by one tick.</summary>
    /// <param name="current">The ramp's current value.</param>
    /// <param name="triggerHeld">Whether the trigger is down this tick.</param>
    /// <returns>The new value, saturating at one and snapping to zero when the trigger is released.</returns>
    public static float AdvanceTriggerRamp(float current, bool triggerHeld)
    {
        if (!triggerHeld)
        {
            return 0f;
        }

        float next = current + TriggerRampPerTick;
        return next > 1f ? 1f : next;
    }

    /// <summary>Steps the recoil phase one tick toward zero.</summary>
    /// <param name="current">The phase, which may be either sign.</param>
    /// <param name="roundsPerMinute">The weapon's cyclic rate.</param>
    /// <returns>The new phase, snapped to zero on crossing it.</returns>
    /// <remarks>
    /// The original handles the two signs separately and snaps on overshoot, so the phase settles at
    /// exactly zero rather than oscillating around it.
    /// </remarks>
    public static float AdvanceRecoilPhase(float current, int roundsPerMinute)
    {
        float step = RecoilPhaseStep(roundsPerMinute);

        if (current < 0f)
        {
            float raised = current + step;
            return raised > 0f ? 0f : raised;
        }

        if (current <= 0f)
        {
            return current;
        }

        float lowered = current - step;
        return lowered < 0f ? 0f : lowered;
    }
}
