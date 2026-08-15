using OpenIGI.Core.Graphics;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// The fonts and palette a screen's widgets draw from, so a whole shell reads as one design rather than
/// each widget inventing its own colours.
/// </summary>
/// <remarks>
/// The default palette is a cold, dark, tactical scheme in the lineage of I.G.I.'s own green-and-amber
/// menus, tuned for legibility over a blurred showcase scene: near-black translucent panels, off-white
/// text, an amber accent for focus. A screen is free to build a theme with different colours; the widgets
/// only ask the theme for named roles, never for literal values.
/// </remarks>
public sealed class UiTheme
{
    /// <summary>Initializes a new instance of the <see cref="UiTheme"/> class.</summary>
    /// <param name="body">The font used for body text and controls.</param>
    /// <param name="heading">The font used for headings and titles, or <see langword="null"/> to reuse <paramref name="body"/>.</param>
    /// <param name="mono">The fixed-pitch font used for values and tabular data, or <see langword="null"/> to reuse <paramref name="body"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public UiTheme(UiFont body, UiFont? heading = null, UiFont? mono = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Body = body;
        Heading = heading ?? body;
        Mono = mono ?? body;
    }

    /// <summary>Gets the font used for body text and controls.</summary>
    public UiFont Body { get; }

    /// <summary>Gets the font used for headings and titles.</summary>
    public UiFont Heading { get; }

    /// <summary>Gets the fixed-pitch font used for values, tables and anything that must align in columns.</summary>
    /// <remarks>
    /// Falls back to <see cref="Body"/> when a screen has no use for one, so a theme built for the
    /// cinematic shell needs no monospaced face and nothing has to check for null.
    /// </remarks>
    public UiFont Mono { get; }

    /// <summary>Gets or sets the default body text size, in design units.</summary>
    public float BodyTextSize { get; set; } = 22f;

    /// <summary>Gets or sets the heading text size, in design units.</summary>
    public float HeadingTextSize { get; set; } = 44f;

    /// <summary>Gets or sets the primary text colour.</summary>
    public Bgra32 Text { get; set; } = Bgra32.FromRgb(232, 238, 242);

    /// <summary>Gets or sets the colour of secondary, lower-emphasis text.</summary>
    public Bgra32 TextMuted { get; set; } = Bgra32.FromRgb(150, 162, 172);

    /// <summary>Gets or sets the colour of disabled text and controls.</summary>
    public Bgra32 TextDisabled { get; set; } = Bgra32.FromRgb(96, 104, 112);

    /// <summary>Gets or sets the accent colour used for focus and selection.</summary>
    public Bgra32 Accent { get; set; } = Bgra32.FromRgb(240, 176, 84);

    /// <summary>Gets or sets the fill of a panel behind content.</summary>
    public Bgra32 Panel { get; set; } = Bgra32.FromRgba(10, 13, 16, 210);

    /// <summary>Gets or sets the fill highlight of a focused row.</summary>
    public Bgra32 FocusFill { get; set; } = Bgra32.FromRgba(240, 176, 84, 40);

    /// <summary>Gets or sets a hairline border colour.</summary>
    public Bgra32 Border { get; set; } = Bgra32.FromRgba(120, 134, 146, 90);

    /// <summary>Gets or sets the soft text shadow colour.</summary>
    public Bgra32 Shadow { get; set; } = Bgra32.FromRgba(0, 0, 0, 150);

    /// <summary>Gets or sets the fill of a header, toolbar or input row sitting on a <see cref="Panel"/>.</summary>
    /// <remarks>
    /// The middle of three elevation tiers — <see cref="Panel"/> behind, this for the chrome standing on
    /// it, <see cref="SurfaceRaised"/> for anything floating above both. Three is deliberate: more tiers
    /// stop reading as depth and start reading as noise.
    /// </remarks>
    public Bgra32 Surface { get; set; } = Bgra32.FromRgb(17, 22, 27);

    /// <summary>Gets or sets the fill of content that floats above the panel, such as a dropdown.</summary>
    public Bgra32 SurfaceRaised { get; set; } = Bgra32.FromRgb(23, 29, 36);

    /// <summary>Gets or sets the wash drawn behind selected text or a selected row.</summary>
    public Bgra32 Selection { get; set; } = Bgra32.FromRgba(240, 176, 84, 70);

    /// <summary>Gets or sets the colour carrying neutral information.</summary>
    public Bgra32 Info { get; set; } = Bgra32.FromRgb(143, 184, 255);

    /// <summary>Gets or sets the colour reporting that something succeeded.</summary>
    public Bgra32 Success { get; set; } = Bgra32.FromRgb(140, 220, 170);

    /// <summary>Gets or sets the colour reporting something unexpected that did not fail.</summary>
    public Bgra32 Warning { get; set; } = Bgra32.FromRgb(255, 200, 90);

    /// <summary>Gets or sets the colour reporting a failure.</summary>
    public Bgra32 Danger { get; set; } = Bgra32.FromRgb(255, 110, 110);
}
