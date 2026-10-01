namespace OpenIGI.Rendering;

/// <summary>
/// Turns the resolved frame into the picture on the map computer's tube: a phosphor display transform
/// plus the power-on behaviour that goes with one.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>display transform</em>, not an overlay. The world under it has to stay readable — the
/// map is the whole reason the screen exists — so the grade is derived from the resolved luminance and
/// the terrain, the roads and the roofs all survive it. A translucent green rectangle laid over the
/// frame would be much simpler and would turn the useful picture into a blank field, which is exactly
/// the mistake the retail path's own comment records.
/// </para>
/// <para>
/// It is separate from <see cref="PostProcessSettings.GreenPhosphor"/> rather than a generalisation of
/// it. That flag is the retail handheld's calibrated look and the vanilla preset's regression gate; this
/// is the remaster's, and it animates. Keeping them apart means turning the tactical interface on cannot
/// change what <c>-vanilla</c> puts on screen, which is the property the whole remaster track is checked
/// against. The two are never on together.
/// </para>
/// <para>
/// Everything here is continuous because the transition is the point. The screen does not appear: the
/// camera climbs, the grade fades in under it, a refresh band sweeps down the tube and the interface
/// arrives behind it. A boolean cannot express any of that.
/// </para>
/// </remarks>
/// <param name="Amount">
/// How much of the phosphor transform is applied, 0 for the ordinary picture to 1 for the tube. Also
/// scales the tube's own geometry — the curvature, the scanlines and the shadow mask — so the screen
/// bends into existence rather than snapping on.
/// </param>
/// <param name="Boot">
/// The power-on energy, 0 to 1: the horizontal tearing, the block noise and the brightness surge a
/// display makes in the moment it is driven. Independent of <paramref name="Amount"/> because the
/// disturbance peaks while the grade is still arriving.
/// </param>
/// <param name="Sweep">
/// How strongly the refresh band shows, 0 for none. The bright line that runs down a tube being driven
/// for the first time, and the thing that makes the arrival read as a screen rather than as a fade.
/// </param>
/// <param name="SweepPosition">Where that band is, 0 at the top of the frame to 1 at the bottom.</param>
/// <param name="Seconds">
/// A continuously rising time in seconds, for the flicker, the drifting scanlines and the animated
/// noise. Wall-clock rather than a frame counter: the tube's own behaviour must not run faster on a
/// faster machine.
/// </param>
public readonly record struct TacticalDisplaySettings(
    float Amount = 0f,
    float Boot = 0f,
    float Sweep = 0f,
    float SweepPosition = 0f,
    float Seconds = 0f)
{
    /// <summary>Gets a value indicating whether any of this would change the picture.</summary>
    public bool IsActive => Amount > 0f || Boot > 0f || Sweep > 0f;
}
