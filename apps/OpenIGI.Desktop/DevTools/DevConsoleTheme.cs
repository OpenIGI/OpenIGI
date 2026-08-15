using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Desktop;

/// <summary>
/// The developer tools' palette and type scale: a <see cref="UiTheme"/> for the shared widget roles,
/// plus the syntax colours only a console has.
/// </summary>
/// <remarks>
/// <para>
/// Wraps a theme rather than extending one. The elevation, selection and status roles belong to any
/// modern surface and live on <see cref="UiTheme"/> where every widget can reach them; a colour for
/// "a token that turned out to be an alias" belongs to exactly one tool and would be clutter on the
/// shared type.
/// </para>
/// <para>
/// The palette stays in the same cold, near-black, amber-accented family as the cinematic shell, a
/// little cooler and a little higher in contrast because this is a working surface rather than a
/// cinematic one. The point is that the console reads as part of the same product without pretending
/// to be part of the game.
/// </para>
/// </remarks>
public sealed class DevConsoleTheme
{
    /// <summary>Initialises a new instance of the <see cref="DevConsoleTheme"/> class.</summary>
    /// <param name="mono">The fixed-pitch face everything is set in.</param>
    /// <param name="palette">Which colour variant to build.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mono"/> is <see langword="null"/>.</exception>
    public DevConsoleTheme(UiFont mono, ConsolePalette palette = ConsolePalette.Dark)
    {
        ArgumentNullException.ThrowIfNull(mono);

        Palette = palette;
        Ui = new UiTheme(mono, mono, mono);
        Apply(palette);
    }

    /// <summary>Gets the shared widget theme, which the window chrome and any reused widget draw from.</summary>
    public UiTheme Ui { get; }

    /// <summary>Gets which colour variant this theme was built for.</summary>
    public ConsolePalette Palette { get; }

    /// <summary>Gets the fixed-pitch face the tools are set in.</summary>
    public UiFont Mono => Ui.Mono;

    /// <summary>Gets or sets the colour of a token that resolved to a command.</summary>
    public Bgra32 SyntaxCommand { get; set; } = Bgra32.FromRgb(140, 200, 255);

    /// <summary>Gets or sets the colour of a token that resolved to a console variable.</summary>
    public Bgra32 SyntaxVariable { get; set; } = Bgra32.FromRgb(150, 220, 170);

    /// <summary>Gets or sets the colour of a token that resolved to an alias.</summary>
    public Bgra32 SyntaxAlias { get; set; } = Bgra32.FromRgb(201, 168, 255);

    /// <summary>Gets or sets the colour of a numeric argument.</summary>
    public Bgra32 SyntaxNumber { get; set; } = Bgra32.FromRgb(242, 201, 125);

    /// <summary>Gets or sets the colour of a quoted argument.</summary>
    public Bgra32 SyntaxString { get; set; } = Bgra32.FromRgb(195, 232, 141);

    /// <summary>Gets or sets the colour of the inline completion shown ahead of the caret.</summary>
    /// <remarks>Must read as a suggestion rather than as typed text, or the reader cannot tell what they have actually written.</remarks>
    public Bgra32 SyntaxGhost { get; set; } = Bgra32.FromRgb(110, 120, 132);

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

    private void Apply(ConsolePalette palette)
    {
        switch (palette)
        {
            case ConsolePalette.Dim:
                Ui.Panel = Bgra32.FromRgb(24, 28, 33);
                Ui.Surface = Bgra32.FromRgb(33, 38, 44);
                Ui.SurfaceRaised = Bgra32.FromRgb(41, 47, 54);
                Ui.Text = Bgra32.FromRgb(222, 228, 234);
                Ui.TextMuted = Bgra32.FromRgb(150, 160, 170);
                Ui.Border = Bgra32.FromRgba(130, 145, 158, 100);
                break;

            case ConsolePalette.Amber:
                Ui.Panel = Bgra32.FromRgb(8, 7, 4);
                Ui.Surface = Bgra32.FromRgb(20, 16, 8);
                Ui.SurfaceRaised = Bgra32.FromRgb(28, 22, 11);
                Ui.Text = Bgra32.FromRgb(255, 206, 122);
                Ui.TextMuted = Bgra32.FromRgb(168, 133, 74);
                Ui.Border = Bgra32.FromRgba(180, 140, 70, 110);
                SyntaxCommand = Bgra32.FromRgb(255, 226, 160);
                SyntaxVariable = Bgra32.FromRgb(226, 190, 120);
                SyntaxAlias = Bgra32.FromRgb(255, 180, 110);
                SyntaxGhost = Bgra32.FromRgb(120, 96, 52);
                break;

            case ConsolePalette.Dark:
            default:
                Ui.Panel = Bgra32.FromRgb(10, 13, 16);
                Ui.Surface = Bgra32.FromRgb(17, 22, 27);
                Ui.SurfaceRaised = Bgra32.FromRgb(23, 29, 36);
                Ui.Text = Bgra32.FromRgb(232, 238, 242);
                Ui.TextMuted = Bgra32.FromRgb(150, 162, 172);
                Ui.Border = Bgra32.FromRgba(120, 134, 146, 90);
                break;
        }
    }
}
