namespace OpenIGI.UI.Animation;

/// <summary>
/// A critically-damped spring that chases a moving target, carrying velocity so it settles smoothly
/// rather than restarting on every change.
/// </summary>
/// <remarks>
/// <para>
/// Where a <see cref="Tween"/> plays a fixed curve to a fixed target, a spring is the right tool when
/// the target itself keeps moving — a value tracking the mouse, a carousel following a flung gesture,
/// a focus ring chasing a selection that the player is scrubbing through. It never overshoots (it is
/// critically damped) and it is framerate-independent.
/// </para>
/// <para>
/// The integrator is the closed-form critically-damped step from <i>Game Programming Gems 4</i>
/// (Thomas Lowe's "smooth damp"): stable for any time step and any smooth time, so a long stall between
/// frames eases in correctly instead of exploding the way a naive Euler spring would. Advance it with a
/// <b>real elapsed time</b> delta, never a simulation tick — the same rule every other animation here
/// follows.
/// </para>
/// </remarks>
public sealed class Spring
{
    /// <summary>Initializes a new instance of the <see cref="Spring"/> class settled at a value.</summary>
    /// <param name="value">The value the spring starts at, also its initial target.</param>
    /// <param name="smoothTime">Roughly the time in seconds to reach the target; smaller is snappier.</param>
    public Spring(float value = 0f, float smoothTime = 0.15f)
    {
        Value = value;
        Target = value;
        SmoothTime = MathF.Max(0.0001f, smoothTime);
    }

    /// <summary>Gets the current value.</summary>
    public float Value { get; private set; }

    /// <summary>Gets or sets the value the spring is chasing.</summary>
    public float Target { get; set; }

    /// <summary>Gets the current velocity.</summary>
    public float Velocity { get; private set; }

    /// <summary>Gets or sets roughly the time in seconds the spring takes to reach its target.</summary>
    public float SmoothTime { get; set; }

    /// <summary>Gets or sets the largest speed the value may move at, in units per second.</summary>
    public float MaxSpeed { get; set; } = float.PositiveInfinity;

    /// <summary>Jumps immediately to a value, cancelling velocity.</summary>
    /// <param name="value">The value to settle at, also the new target.</param>
    public void SnapTo(float value)
    {
        Value = value;
        Target = value;
        Velocity = 0f;
    }

    /// <summary>Advances the spring towards its target by a real-time delta.</summary>
    /// <param name="elapsedSeconds">The seconds since the last update.</param>
    /// <returns>The value after advancing.</returns>
    public float Update(float elapsedSeconds)
    {
        if (elapsedSeconds <= 0f)
        {
            return Value;
        }

        float smoothTime = MathF.Max(0.0001f, SmoothTime);
        float omega = 2f / smoothTime;
        float x = omega * elapsedSeconds;
        float exp = 1f / (1f + x + (0.48f * x * x) + (0.235f * x * x * x));

        float change = Value - Target;
        float maxChange = MaxSpeed * smoothTime;
        change = Math.Clamp(change, -maxChange, maxChange);
        float target = Value - change;

        float temp = (Velocity + (omega * change)) * elapsedSeconds;
        Velocity = (Velocity - (omega * temp)) * exp;
        float output = target + ((change + temp) * exp);

        // Guard the classic overshoot-past-target artefact when the target is approached from one side.
        if ((Target - Value > 0f) == (output > Target))
        {
            output = Target;
            Velocity = (output - Target) / elapsedSeconds;
        }

        Value = output;
        return Value;
    }
}
