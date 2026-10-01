using OpenIGI.Core.Graphics;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// The fonts and palette a screen's widgets draw from, so a whole shell reads as one design rather than
/// each widget inventing its own colours.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are the design's tokens (<see cref="UiPalette"/>): dark translucent glass, warm grey text
/// and one amber accent, legible over a blurred scene. A screen is free to build a theme with different
/// colours; the widgets only ask the theme for named roles, never for literal values.
/// </para>
/// <para>
/// Four faces: <see cref="Body"/> for prose and captions, <see cref="Heading"/> — a condensed face — for
/// the uppercase tracked labels the design uses on every tab, button and value, <see cref="Display"/> for
/// the big titles, and <see cref="Mono"/> for eyebrows, key caps and numbers. Any of the last three falls
/// back to the one before it, so a test can build a theme from one font.
/// </para>
/// </remarks>
public sealed class UiTheme
{
    /// <summary>Initializes a new instance of the <see cref="UiTheme"/> class.</summary>
    /// <param name="body">The font used for body text and captions.</param>
    /// <param name="heading">The condensed font for labels, tabs, buttons and values, or <see langword="null"/> to reuse <paramref name="body"/>.</param>
    /// <param name="mono">The fixed-pitch font for eyebrows, key caps and numbers, or <see langword="null"/> to reuse <paramref name="body"/>.</param>
    /// <param name="display">The heavy font for titles, or <see langword="null"/> to reuse <paramref name="heading"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public UiTheme(UiFont body, UiFont? heading = null, UiFont? mono = null, UiFont? display = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Body = body;
        Heading = heading ?? body;
        Mono = mono ?? body;
        Display = display ?? Heading;
    }

    /// <summary>Gets the font used for body text and captions.</summary>
    public UiFont Body { get; }

    /// <summary>Gets the condensed font used for tabs, buttons, values and section headings.</summary>
    public UiFont Heading { get; }

    /// <summary>Gets the heavy font used for titles.</summary>
    public UiFont Display { get; }

    /// <summary>Gets the fixed-pitch font used for eyebrows, key caps, values and anything that must align in columns.</summary>
    /// <remarks>
    /// Falls back to <see cref="Body"/> when a screen has no use for one, so nothing has to check for null.
    /// </remarks>
    public UiFont Mono { get; }

    /// <summary>Gets or sets the default body text size, in design units: a settings row's caption.</summary>
    public float BodyTextSize { get; set; } = 17f;

    /// <summary>Gets or sets the heading text size, in design units: a section title.</summary>
    public float HeadingTextSize { get; set; } = 44f;

    /// <summary>Gets or sets the size of a condensed label — a tab, a button, a value — in design units.</summary>
    public float LabelTextSize { get; set; } = 18f;

    /// <summary>Gets or sets the size of a monospaced caption in design units.</summary>
    public float CaptionTextSize { get; set; } = 11f;

    /// <summary>Gets or sets the letterspacing of an uppercase label, in ems.</summary>
    public float LabelTracking { get; set; } = 0.16f;

    /// <summary>Gets or sets the letterspacing of a monospaced caption, in ems.</summary>
    public float CaptionTracking { get; set; } = 0.28f;

    /// <summary>Gets or sets a settings row's height, in design units.</summary>
    public float RowHeight { get; set; } = 46f;

    /// <summary>Gets or sets a value indicating whether settings rows are ruled off from one another.</summary>
    public bool RowDividers { get; set; }

    /// <summary>Gets or sets the backdrop blur behind a glass panel, in design units.</summary>
    public float GlassBlur { get; set; } = 18f;

    /// <summary>Gets or sets the primary text colour.</summary>
    public Bgra32 Text { get; set; } = UiPalette.Text;

    /// <summary>Gets or sets the colour of hovered and selected text.</summary>
    public Bgra32 TextBright { get; set; } = UiPalette.TextBright;

    /// <summary>Gets or sets the colour of a row's caption.</summary>
    public Bgra32 TextLabel { get; set; } = UiPalette.TextLabel;

    /// <summary>Gets or sets the colour of an unselected entry in a list.</summary>
    public Bgra32 TextSecondary { get; set; } = UiPalette.TextSecondary;

    /// <summary>Gets or sets the colour of secondary, lower-emphasis text.</summary>
    public Bgra32 TextMuted { get; set; } = UiPalette.TextMuted;

    /// <summary>Gets or sets the colour of a dim mark: a chevron at rest, the off side of a toggle.</summary>
    public Bgra32 TextDim { get; set; } = UiPalette.TextDim;

    /// <summary>Gets or sets the colour of disabled text and controls.</summary>
    public Bgra32 TextDisabled { get; set; } = UiPalette.TextDisabled;

    /// <summary>Gets or sets the accent colour used for focus and selection.</summary>
    public Bgra32 Accent { get; set; } = UiPalette.Accent;

    /// <summary>Gets or sets the accent fill under the pointer.</summary>
    public Bgra32 AccentHover { get; set; } = UiPalette.AccentHover;

    /// <summary>Gets or sets the colour of text set on an accent fill.</summary>
    public Bgra32 OnAccent { get; set; } = UiPalette.Ink;

    /// <summary>Gets or sets the fill of a panel behind content.</summary>
    public Bgra32 Panel { get; set; } = UiPalette.Glass;

    /// <summary>Gets or sets the fill highlight of a focused or hovered row.</summary>
    public Bgra32 FocusFill { get; set; } = UiPalette.HoverWash;

    /// <summary>Gets or sets a hairline border colour.</summary>
    public Bgra32 Border { get; set; } = UiPalette.Hairline;

    /// <summary>Gets or sets the empty part of a slider or progress track.</summary>
    public Bgra32 Track { get; set; } = UiPalette.Track;

    /// <summary>Gets or sets the soft text shadow colour.</summary>
    public Bgra32 Shadow { get; set; } = Bgra32.FromRgba(0, 0, 0, 150);

    /// <summary>Gets or sets the fill of a header, toolbar or input row sitting on a <see cref="Panel"/>.</summary>
    /// <remarks>
    /// The middle of three elevation tiers — <see cref="Panel"/> behind, this for the chrome standing on
    /// it, <see cref="SurfaceRaised"/> for anything floating above both. Three is deliberate: more tiers
    /// stop reading as depth and start reading as noise.
    /// </remarks>
    public Bgra32 Surface { get; set; } = UiPalette.White(0.04f);

    /// <summary>Gets or sets the fill of content that floats above the panel, such as a dropdown.</summary>
    public Bgra32 SurfaceRaised { get; set; } = UiPalette.GlassMenu;

    /// <summary>Gets or sets the wash drawn behind selected text or a selected row.</summary>
    public Bgra32 Selection { get; set; } = UiPalette.AccentAt(0.1f);

    /// <summary>Gets or sets the colour carrying neutral information.</summary>
    public Bgra32 Info { get; set; } = UiPalette.Info;

    /// <summary>Gets or sets the colour reporting that something succeeded.</summary>
    public Bgra32 Success { get; set; } = UiPalette.Success;

    /// <summary>Gets or sets the colour reporting something unexpected that did not fail.</summary>
    public Bgra32 Warning { get; set; } = UiPalette.Warning;

    /// <summary>Gets or sets the colour reporting a failure.</summary>
    public Bgra32 Danger { get; set; } = UiPalette.Danger;

    /// <summary>Gets or sets the caption colour of an action that destroys something.</summary>
    public Bgra32 Destructive { get; set; } = UiPalette.Destructive;

    /// <summary>Gets or sets the border colour of an action that destroys something.</summary>
    public Bgra32 DestructiveBorder { get; set; } = UiPalette.DestructiveBorder;
}
