using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The render-side envelope of the remastered thermal sensor: its raise, and the calibration it runs each time it comes up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> A cooled sensor calibrates as it starts: a shutter drops over the detector, the picture holds at a flat
/// field while it measures it, and then the picture comes back, grainy at first, and settles. That moment is what tells a
/// player the view changed instruments rather than colours. The raise is quick, so the calibration carries the transition.
/// </para>
/// <para>
/// Presentation state on the render clock, as <see cref="BinocularOptics"/> is, and for the same reasons.
/// </para>
/// </remarks>
public sealed class ThermalOptics
{
    /// <summary>How long the sensor's picture takes to replace the scene as the goggles come up, in seconds.</summary>
    public const float OpenSeconds = 0.18f;

    /// <summary>How long the calibration lasts from the moment the goggles come up, in seconds.</summary>
    public const float CalibrationSeconds = 0.7f;

    /// <summary>How long a palette's name stays on the eyepiece after it is changed, in seconds.</summary>
    public const float PaletteNoticeSeconds = 1.6f;

    private readonly OpticsRaise _raise = new(OpenSeconds);
    private bool _wasOpen;
    private float _sinceRaised = CalibrationSeconds;
    private float _sincePaletteChanged = PaletteNoticeSeconds;

    /// <summary>Gets how much of the sensor's picture replaces the scene, from 0 to 1.</summary>
    public float Amount => _raise.Amount;

    /// <summary>Gets the render clock, for the sensor's animated noise.</summary>
    public float Seconds => _raise.Seconds;

    /// <summary>Gets how far through its calibration the sensor is: 1 as the goggles come up, 0 once settled.</summary>
    public float Calibration => 1f - Math.Clamp(_sinceRaised / CalibrationSeconds, 0f, 1f);

    /// <summary>Gets how strongly the palette's name shows, 1 as it is changed fading to 0.</summary>
    public float PaletteNotice => 1f - Math.Clamp(_sincePaletteChanged / PaletteNoticeSeconds, 0f, 1f);

    /// <summary>Gets the settings to hand the resolve this frame.</summary>
    /// <param name="palette">The palette the sensor colours heat with.</param>
    /// <param name="noise">How strong its noise is.</param>
    /// <returns>The settings; inactive while the goggles are down.</returns>
    public ThermalDisplaySettings Display(ThermalPalette palette, float noise) =>
        new(Amount, palette, Seconds, Calibration, noise);

    /// <summary>Advances the envelope one rendered frame.</summary>
    /// <param name="open">Whether the sensor is up this frame.</param>
    /// <param name="elapsedSeconds">Real seconds since the previous frame.</param>
    public void Update(bool open, float elapsedSeconds)
    {
        float step = _raise.Update(open, elapsedSeconds);
        if (open && !_wasOpen)
        {
            _sinceRaised = 0f;
        }

        _wasOpen = open;
        _sinceRaised = MathF.Min(_sinceRaised + step, CalibrationSeconds);
        _sincePaletteChanged = MathF.Min(_sincePaletteChanged + step, PaletteNoticeSeconds);
    }

    /// <summary>Shows the palette's name on the eyepiece for a moment, as it is changed.</summary>
    public void NotePaletteChanged() => _sincePaletteChanged = 0f;

    /// <summary>Returns the envelope to its lowered state, for a teardown or a control-mode change.</summary>
    public void Reset()
    {
        _raise.Reset();
        _wasOpen = false;
        _sinceRaised = CalibrationSeconds;
        _sincePaletteChanged = PaletteNoticeSeconds;
    }
}
