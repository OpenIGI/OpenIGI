namespace OpenIGI.UI.Animation;

/// <summary>
/// A scalar animation from one value to another over a fixed duration, with an easing curve and an
/// optional start delay.
/// </summary>
/// <remarks>
/// <para>
/// Advanced by <see cref="Update"/> with a <b>real elapsed time</b> delta, never a simulation step. The
/// interface runs on wall-clock time exactly as the smooth presentation blend and the free camera do;
/// a tween measuring its life in ticks would couple animation to the 30 Hz lockstep and stutter
/// whenever the simulation caught up. Feeding it seconds keeps the two clocks apart.
/// </para>
/// <para>
/// The tween is deliberately re-targetable: <see cref="To"/> is set through <see cref="Retarget"/>,
/// which restarts the curve from wherever the value currently is. That is what a focus highlight needs
/// when the selection moves again before the previous move has finished — it eases on from the current
/// position rather than snapping back to a start.
/// </para>
/// </remarks>
public sealed class Tween
{
    private float _elapsed;

    /// <summary>Initializes a new instance of the <see cref="Tween"/> class already settled at a value.</summary>
    /// <param name="value">The value the tween starts and rests at.</param>
    /// <param name="duration">The default duration, in seconds, used by <see cref="Retarget"/>.</param>
    /// <param name="ease">The default easing curve.</param>
    public Tween(float value = 0f, float duration = 0.2f, Ease ease = Ease.OutCubic)
    {
        From = value;
        To = value;
        Duration = MathF.Max(0f, duration);
        Ease = ease;
        _elapsed = Duration;
    }

    /// <summary>Gets the value the current segment eases from.</summary>
    public float From { get; private set; }

    /// <summary>Gets the value the current segment eases to.</summary>
    public float To { get; private set; }

    /// <summary>Gets or sets the default duration in seconds used when a new target is set.</summary>
    public float Duration { get; set; }

    /// <summary>Gets or sets the default easing curve used when a new target is set.</summary>
    public Ease Ease { get; set; }

    /// <summary>Gets or sets the delay before the current segment begins, in seconds.</summary>
    public float Delay { get; set; }

    /// <summary>Gets the current value.</summary>
    public float Value
    {
        get
        {
            if (Duration <= 0f)
            {
                return _elapsed >= Delay ? To : From;
            }

            float active = _elapsed - Delay;
            if (active <= 0f)
            {
                return From;
            }

            float t = Math.Clamp(active / Duration, 0f, 1f);
            return Interpolate.Lerp(From, To, Easing.Evaluate(Ease, t));
        }
    }

    /// <summary>Gets a value indicating whether the current segment has finished.</summary>
    public bool IsComplete => _elapsed >= Delay + Duration;

    /// <summary>Sets a new target, easing on from the current value.</summary>
    /// <param name="target">The value to ease to.</param>
    /// <param name="duration">The duration in seconds, or <see langword="null"/> to keep <see cref="Duration"/>.</param>
    /// <param name="ease">The easing curve, or <see langword="null"/> to keep <see cref="Ease"/>.</param>
    /// <param name="delay">The delay before the segment begins, in seconds.</param>
    public void Retarget(float target, float? duration = null, Ease? ease = null, float delay = 0f)
    {
        // Ease on from where the value is now, so a redirected tween never jumps back to an old start.
        From = Value;
        To = target;
        if (duration is { } d)
        {
            Duration = MathF.Max(0f, d);
        }

        if (ease is { } e)
        {
            Ease = e;
        }

        Delay = MathF.Max(0f, delay);
        _elapsed = 0f;
    }

    /// <summary>Jumps immediately to a value, cancelling any animation in progress.</summary>
    /// <param name="value">The value to settle at.</param>
    public void SnapTo(float value)
    {
        From = value;
        To = value;
        Delay = 0f;
        _elapsed = Duration;
    }

    /// <summary>Advances the tween by a real-time delta.</summary>
    /// <param name="elapsedSeconds">The seconds since the last update.</param>
    /// <returns>The value after advancing.</returns>
    public float Update(float elapsedSeconds)
    {
        if (elapsedSeconds > 0f && !IsComplete)
        {
            _elapsed += elapsedSeconds;
        }

        return Value;
    }
}
