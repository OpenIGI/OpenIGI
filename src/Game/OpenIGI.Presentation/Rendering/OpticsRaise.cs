namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// How far a remastered optic's display transform is raised: a clamped ramp over the render clock that rises as the device comes
/// up and falls, a little faster, as it goes down. The binoculars' glass and the thermal sensor are each one.
/// </summary>
/// <remarks>
/// Presentation state, kept off the simulation: the device's open state ticks at the world's rate and changes in steps, while
/// a display transform has to move with the frames, or it shimmers -- and at the same speed whatever the frame rate.
/// </remarks>
/// <param name="openSeconds">How long the transform takes to reach full strength as the device is raised, in seconds.</param>
public sealed class OpticsRaise(float openSeconds)
{
    /// <summary>How much faster the transform falls away than it rises.</summary>
    public const float CloseRateMultiplier = 1.6f;

    /// <summary>The largest render step folded into the ramp at once, so a hitch cannot snap it.</summary>
    public const float MaxStepSeconds = 0.1f;

    private readonly float _openSeconds = MathF.Max(openSeconds, 1e-3f);

    /// <summary>Gets how much of the transform is applied, from 0 to 1.</summary>
    public float Amount { get; private set; }

    /// <summary>Gets the continuously rising render-clock time, for what the transform animates.</summary>
    public float Seconds { get; private set; }

    /// <summary>Advances the ramp one rendered frame.</summary>
    /// <param name="open">Whether the device is raised this frame.</param>
    /// <param name="elapsedSeconds">Real seconds since the previous frame.</param>
    /// <returns>The step folded in, after the clamp.</returns>
    public float Update(bool open, float elapsedSeconds)
    {
        float step = Math.Clamp(elapsedSeconds, 0f, MaxStepSeconds);
        Seconds += step;

        float rate = step / _openSeconds;
        Amount = open
            ? MathF.Min(1f, Amount + rate)
            : MathF.Max(0f, Amount - (rate * CloseRateMultiplier));
        return step;
    }

    /// <summary>Returns the ramp to its lowered state, for a teardown or a control-mode change.</summary>
    public void Reset()
    {
        Amount = 0f;
        Seconds = 0f;
    }
}
