using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The render-side fade that raises and lowers the remastered binocular's glass lens transform.
/// </summary>
/// <remarks>
/// <para>
/// Title-agnostic, which is why it sits here rather than beside one game's renderers: it is a clamped
/// ramp over a wall-clock delta whose only other type comes from <see cref="OpenIGI.Rendering"/>, and
/// both games have binoculars — I.G.I. 2's levels name <c>OVERLAY_BINOCULAR</c> in their own scripts.
/// </para>
/// <para>
/// This is presentation state, deliberately kept off the simulation. <c>BinocularsView</c> owns whether
/// the device is open, but it ticks on the fixed 30 Hz clock and exposes only edges, not a continuous
/// envelope. The glass is a display transform applied at resolve time, so its fade has to run on the
/// render/wall clock -- otherwise it would advance in 30 Hz steps and shimmer, and would fade faster on
/// a faster machine. This mirrors <c>MapComputerCamera.DisplayAmount</c>, which raises the map tube the
/// same way.
/// </para>
/// <para>
/// The close is a little quicker than the open: lowering the binoculars is a deliberate action and a
/// lingering lens reads as lag, whereas the raise wants a moment of the glass settling in.
/// </para>
/// </remarks>
public sealed class BinocularOptics
{
    /// <summary>How long the glass takes to reach full strength when the device is raised, in seconds.</summary>
    public const float OpenSeconds = 0.35f;

    /// <summary>How much faster the glass falls away than it rises.</summary>
    private const float CloseRateMultiplier = 1.6f;

    /// <summary>The largest render step folded into the fade at once, so a hitch cannot snap it.</summary>
    private const float MaxStepSeconds = 0.1f;

    private float _amount;
    private float _seconds;

    /// <summary>Gets how much of the glass transform is applied, from 0 to 1.</summary>
    public float Amount => _amount;

    /// <summary>Gets the continuously rising wall-clock time driving the glass grain.</summary>
    public float Seconds => _seconds;

    /// <summary>Gets the settings to hand the resolve pass this frame.</summary>
    public BinocularDisplaySettings Display => new(_amount, 0f, _seconds);

    /// <summary>Advances the fade one rendered frame.</summary>
    /// <param name="open">Whether the binoculars are raised this frame.</param>
    /// <param name="elapsedSeconds">Real wall-clock seconds since the previous frame.</param>
    public void Update(bool open, float elapsedSeconds)
    {
        float step = Math.Clamp(elapsedSeconds, 0f, MaxStepSeconds);
        _seconds += step;

        float rate = step / OpenSeconds;
        _amount = open
            ? MathF.Min(1f, _amount + rate)
            : MathF.Max(0f, _amount - (rate * CloseRateMultiplier));
    }

    /// <summary>Returns the fade to its closed state, for a teardown or a control-mode change.</summary>
    public void Reset()
    {
        _amount = 0f;
        _seconds = 0f;
    }
}
