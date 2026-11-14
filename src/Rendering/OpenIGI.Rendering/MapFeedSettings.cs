namespace OpenIGI.Rendering;

/// <summary>
/// How the remastered computer's map is put on screen: the overhead picture turned into a one-colour satellite feed, and the
/// way that feed comes up (<c>Ui/MapFeed.slang</c>).
/// </summary>
/// <remarks>
/// <para>
/// A display transform, not an overlay: the level under it has to stay readable, so the feed is made from the picture's own
/// brightness and the terrain, the roads and the roofs all survive it.
/// </para>
/// <para>
/// <b>Not retail.</b> The original's computer keeps its own calibrated look (<see cref="PostProcessSettings.GreenPhosphor"/>)
/// and is never drawn through this.
/// </para>
/// <para>
/// Every term is continuous because the arrival is the point: the camera climbs, the feed fades in under it, a refresh band
/// runs down it and the interface arrives behind that.
/// </para>
/// </remarks>
public readonly record struct MapFeedSettings
{
    /// <summary>Gets how much of the feed is applied, nought for the picture as it was to one for the feed.</summary>
    public float Amount { get; init; }

    /// <summary>Gets the power-on disturbance, nought to one: the tearing, the block noise and the surge of a display being driven.</summary>
    public float Boot { get; init; }

    /// <summary>Gets how strongly the refresh band shows, nought for none.</summary>
    public float Sweep { get; init; }

    /// <summary>Gets where the refresh band is, nought at the top of the screen to one at the bottom.</summary>
    public float SweepPosition { get; init; }

    /// <summary>Gets a rising time in seconds, the wall clock's, for the drift, the grain and the slips.</summary>
    public float Seconds { get; init; }

    /// <summary>Gets how far a text page over the feed dims and softens it, nought to one.</summary>
    public float Dim { get; init; }

    /// <summary>Gets the disturbance of a change of page, nought to one.</summary>
    public float Burst { get; init; }

    /// <summary>Gets how far the visor bends the feed at its corners, as the display over it is bent (<see cref="HudFxSettings.Curvature"/>).</summary>
    public float Curvature { get; init; }

    /// <summary>Gets the scanlines across the screen's height, per 1080 lines.</summary>
    public float ScanlineDensity { get; init; }

    /// <summary>Gets how dark a scanline's trough is, nought to one.</summary>
    public float ScanlineStrength { get; init; }

    /// <summary>Gets how strong the grain is.</summary>
    public float Grain { get; init; }

    /// <summary>Gets the feed's darkest colour, as display red, green and blue from nought to one.</summary>
    public (float R, float G, float B) Shadow { get; init; }

    /// <summary>Gets the colour of the feed's middle brightness.</summary>
    public (float R, float G, float B) Body { get; init; }

    /// <summary>Gets the feed's brightest colour.</summary>
    public (float R, float G, float B) Highlight { get; init; }

    /// <summary>Gets a value indicating whether the pass changes the picture at all.</summary>
    public bool IsActive => Amount > 0f;
}
