using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// The developer tools' palette, type scale and spacing rhythm.
/// </summary>
/// <remarks>
/// <para>
/// Wraps a <see cref="UiTheme"/> rather than extending it. The elevation, selection and status roles
/// belong to any modern surface and live on the shared theme where every widget can reach them; a
/// colour for "a token that turned out to be an alias" belongs to exactly one tool.
/// </para>
/// <para>
/// <b>The spacing scale is the reason the panels look like one tool.</b> Every inset, gap and row
/// height below comes from <see cref="Unit"/>, so nothing is nudged by eye and no two panels drift
/// apart. Type sizes are derived the same way from one body size, and every one of them is rounded to
/// a whole pixel before a face is asked for — see <see cref="MonoFontSet"/> for why that matters more
/// than it sounds like it should.
/// </para>
/// </remarks>
public sealed class DevConsoleTheme
{
    /// <summary>Initialises a new instance of the <see cref="DevConsoleTheme"/> class.</summary>
    /// <param name="fonts">The faces, rasterised per size on demand.</param>
    /// <param name="palette">Which colour variant to build.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is <see langword="null"/>.</exception>
    public DevConsoleTheme(MonoFontSet fonts, ConsolePalette palette = ConsolePalette.Dark)
    {
        ArgumentNullException.ThrowIfNull(fonts);

        Fonts = fonts;
        Palette = palette;
        Ui = new UiTheme(fonts.At(14f), fonts.At(14f), fonts.At(14f));
        Apply(palette);
    }

    /// <summary>Gets the faces, rasterised per size.</summary>
    public MonoFontSet Fonts { get; }

    /// <summary>Gets the shared widget theme the window chrome draws from.</summary>
    public UiTheme Ui { get; }

    /// <summary>Gets which colour variant this theme was built for.</summary>
    public ConsolePalette Palette { get; }

    /// <summary>Gets or sets the scale everything is measured in, in pixels.</summary>
    /// <remarks>
    /// One number the whole interface is built from. Four units is a comfortable rhythm at ordinary
    /// sizes and scales with <c>con_scale</c> without anything needing to be re-tuned.
    /// </remarks>
    public float Unit { get; set; } = 4f;

    /// <summary>Gets the inset kept between a panel's edge and its content.</summary>
    public float Gutter => Unit * 3f;

    /// <summary>Gets the gap between neighbouring controls.</summary>
    public float Gap => Unit * 2f;

    /// <summary>Gets or sets the body text size in pixels.</summary>
    public float BodySize { get; set; } = 14f;

    /// <summary>Gets the size of secondary text — column headings, metadata, hints.</summary>
    public float SmallSize => MathF.Max(9f, MathF.Round(BodySize * 0.82f));

    /// <summary>Gets the size a panel's title is set in.</summary>
    public float TitleSize => MathF.Max(10f, MathF.Round(BodySize * 0.9f));

    /// <summary>Gets the height of one line of body text, with the air a log needs to be scannable.</summary>
    public float LineHeight => MathF.Round(BodyFont.LineHeight(BodySize) * 1.45f);

    /// <summary>Gets the height of a table row, which is tighter than a log line because it is tabular.</summary>
    public float RowHeight => MathF.Round(BodyFont.LineHeight(BodySize) * 1.35f);

    /// <summary>Gets the height of a panel's title bar.</summary>
    public float TitleBarHeight => MathF.Round(BodyFont.LineHeight(BodySize) + (Unit * 4f));

    /// <summary>Gets the height of a control row such as a filter bar or the prompt.</summary>
    public float ControlHeight => MathF.Round(BodyFont.LineHeight(BodySize) + (Unit * 3f));

    /// <summary>Gets the body face at the current size.</summary>
    public UiFont BodyFont => Fonts.At(BodySize);

    /// <summary>Gets the face used for secondary text.</summary>
    public UiFont SmallFont => Fonts.At(SmallSize);

    /// <summary>Gets the face used for panel titles.</summary>
    public UiFont TitleFont => Fonts.At(TitleSize);

    /// <summary>Gets the width of one character cell at the body size.</summary>
    /// <remarks>
    /// The unit every column is placed on. Exact because the face is fixed-pitch, which is asserted by
    /// a test so that swapping in a proportional face fails loudly rather than misaligning quietly.
    /// </remarks>
    public float Cell => BodyFont.MeasureWidth("0", BodySize);

    /// <summary>Gets the corner radius panels are drawn with.</summary>
    /// <remarks>
    /// Zero, deliberately. A developer console is a dense grid of text, and rounding its corners and
    /// its controls makes it read as a web page laid over a game rather than as a tool built into one.
    /// Square edges also let a border be exactly one pixel everywhere, which is what makes the whole
    /// thing look sharp rather than soft.
    /// </remarks>
    public static float CornerRadius => 0f;

    /// <summary>Gets or sets the colour of a token that resolved to a command.</summary>
    public Bgra32 SyntaxCommand { get; set; } = Bgra32.FromRgb(121, 192, 255);

    /// <summary>Gets or sets the colour of a token that resolved to a console variable.</summary>
    public Bgra32 SyntaxVariable { get; set; } = Bgra32.FromRgb(126, 231, 135);

    /// <summary>Gets or sets the colour of a token that resolved to an alias.</summary>
    public Bgra32 SyntaxAlias { get; set; } = Bgra32.FromRgb(210, 168, 255);

    /// <summary>Gets or sets the colour of a numeric argument.</summary>
    public Bgra32 SyntaxNumber { get; set; } = Bgra32.FromRgb(255, 166, 87);

    /// <summary>Gets or sets the colour of a quoted argument.</summary>
    public Bgra32 SyntaxString { get; set; } = Bgra32.FromRgb(165, 214, 255);

    /// <summary>Gets or sets the colour of the inline completion shown ahead of the caret.</summary>
    /// <remarks>Must read as a suggestion rather than as typed text, or the reader cannot tell what they have actually written.</remarks>
    public Bgra32 SyntaxGhost { get; set; } = Bgra32.FromRgb(86, 96, 107);

    /// <summary>Gets the colour a line of the given severity is drawn in.</summary>
    /// <param name="severity">The line's severity.</param>
    /// <returns>The colour.</returns>
    public Bgra32 ColorFor(ConsoleSeverity severity) => severity switch
    {
        ConsoleSeverity.Echo => Ui.Accent,
        ConsoleSeverity.Notice => Ui.Info,
        ConsoleSeverity.Warning => Ui.Warning,
        ConsoleSeverity.Error => Ui.Danger,
        _ => Ui.Text,
    };

    /// <summary>Gets the colour a completion candidate of the given kind is drawn in.</summary>
    /// <param name="kind">What the candidate refers to.</param>
    /// <returns>The colour.</returns>
    public Bgra32 ColorFor(ConsoleSuggestionKind kind) => kind switch
    {
        ConsoleSuggestionKind.Command => SyntaxCommand,
        ConsoleSuggestionKind.Variable => SyntaxVariable,
        ConsoleSuggestionKind.Alias => SyntaxAlias,
        ConsoleSuggestionKind.Value => SyntaxNumber,
        _ => Ui.TextMuted,
    };

    /// <summary>Re-derives the type scale for a new body size and interface scale.</summary>
    /// <param name="bodySize">The body text size in pixels.</param>
    /// <param name="scale">The interface scale, which the spacing unit follows.</param>
    public void Resize(float bodySize, float scale)
    {
        BodySize = MathF.Round(Math.Clamp(bodySize, 9f, 40f));
        Unit = MathF.Max(2f, MathF.Round(4f * scale));
    }

    private void Apply(ConsolePalette palette)
    {
        // The borders are solid rather than translucent. A translucent hairline over a translucent
        // panel picks up whatever the scene happens to be behind it, so the same edge reads as two
        // different colours along its length -- which is most of why the first pass looked muddy.
        switch (palette)
        {
            case ConsolePalette.Dim:
                Ui.Panel = Bgra32.FromRgb(28, 33, 40);
                Ui.Surface = Bgra32.FromRgb(35, 41, 49);
                Ui.SurfaceRaised = Bgra32.FromRgb(44, 51, 60);
                Ui.Border = Bgra32.FromRgb(60, 68, 78);
                Ui.Text = Bgra32.FromRgb(228, 234, 240);
                Ui.TextMuted = Bgra32.FromRgb(150, 161, 172);
                Ui.TextDisabled = Bgra32.FromRgb(101, 111, 121);
                break;

            case ConsolePalette.Amber:
                Ui.Panel = Bgra32.FromRgb(14, 11, 6);
                Ui.Surface = Bgra32.FromRgb(23, 18, 9);
                Ui.SurfaceRaised = Bgra32.FromRgb(32, 25, 13);
                Ui.Border = Bgra32.FromRgb(64, 50, 25);
                Ui.Text = Bgra32.FromRgb(255, 208, 128);
                Ui.TextMuted = Bgra32.FromRgb(184, 148, 88);
                Ui.TextDisabled = Bgra32.FromRgb(118, 94, 54);
                SyntaxCommand = Bgra32.FromRgb(255, 226, 160);
                SyntaxVariable = Bgra32.FromRgb(226, 190, 120);
                SyntaxAlias = Bgra32.FromRgb(255, 180, 110);
                SyntaxNumber = Bgra32.FromRgb(255, 196, 120);
                SyntaxGhost = Bgra32.FromRgb(112, 89, 48);
                break;

            case ConsolePalette.Dark:
            default:
                Ui.Panel = Bgra32.FromRgb(16, 18, 22);
                Ui.Surface = Bgra32.FromRgb(30, 34, 40);
                Ui.SurfaceRaised = Bgra32.FromRgb(38, 43, 50);
                Ui.Border = Bgra32.FromRgb(96, 104, 114);
                Ui.Text = Bgra32.FromRgb(224, 228, 233);
                Ui.TextMuted = Bgra32.FromRgb(150, 158, 168);
                Ui.TextDisabled = Bgra32.FromRgb(104, 112, 122);
                break;
        }

        Ui.Accent = Bgra32.FromRgb(240, 176, 84);
        Ui.Selection = Bgra32.FromRgba(58, 110, 190, 190);
        Ui.FocusFill = Ui.Accent.WithAlpha(28);
        Ui.Info = Bgra32.FromRgb(121, 192, 255);
        Ui.Success = Bgra32.FromRgb(126, 231, 135);
        Ui.Warning = Bgra32.FromRgb(240, 176, 84);
        Ui.Danger = Bgra32.FromRgb(248, 113, 113);
        Ui.Shadow = Bgra32.FromRgba(0, 0, 0, 160);
    }
}
