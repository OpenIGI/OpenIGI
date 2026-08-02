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
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public UiTheme(UiFont body, UiFont? heading = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Body = body;
        Heading = heading ?? body;
    }

    /// <summary>Gets the font used for body text and controls.</summary>
    public UiFont Body { get; }

    /// <summary>Gets the font used for headings and titles.</summary>
    public UiFont Heading { get; }

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
}
