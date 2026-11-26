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
/// same way. The ramp itself is <see cref="OpticsRaise"/>, which the thermal sensor shares.
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

    private readonly OpticsRaise _raise = new(OpenSeconds);

    /// <summary>Gets how much of the glass transform is applied, from 0 to 1.</summary>
    public float Amount => _raise.Amount;

    /// <summary>Gets the continuously rising wall-clock time driving the glass grain.</summary>
    public float Seconds => _raise.Seconds;

    /// <summary>Gets the settings to hand the resolve pass this frame.</summary>
    public BinocularDisplaySettings Display => new(Amount, 0f, Seconds);

    /// <summary>Advances the fade one rendered frame.</summary>
    /// <param name="open">Whether the binoculars are raised this frame.</param>
    /// <param name="elapsedSeconds">Real wall-clock seconds since the previous frame.</param>
    public void Update(bool open, float elapsedSeconds) => _raise.Update(open, elapsedSeconds);

    /// <summary>Returns the fade to its closed state, for a teardown or a control-mode change.</summary>
    public void Reset() => _raise.Reset();
}
