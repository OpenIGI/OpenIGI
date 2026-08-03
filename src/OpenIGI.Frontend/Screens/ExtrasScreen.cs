using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Extras: the credits, and the developer-facing toggles — the benchmark overlay and the reminders for
/// photo mode and the console — behind the front end.
/// </summary>
/// <remarks>
/// The benchmark toggle binds the existing <c>r_perflog</c> CVar, which already measures and reports the
/// per-pass GPU timings, so "benchmark reporting per-pass timings" is the switch for a facility the
/// renderer provides rather than a new one. Photo mode and the developer console are reached with the
/// keys the game already binds; the screen points to them rather than duplicating them.
/// </remarks>
public sealed class ExtrasScreen : Screen
{
    private const string CreditsText =
        "OpenIGI — a clean-room reimplementation of Project I.G.I.\n\n" +
        "Engine, renderer and cinematic shell: the OpenIGI contributors.\n" +
        "Reverse engineering against the original binary; no original code is used.\n\n" +
        "Fonts: Saira and Saira Condensed, under the SIL Open Font License.\n" +
        "Project I.G.I. © 2000 its respective rights holders.";

    private readonly ShellContext _context;

    /// <summary>Initializes a new instance of the <see cref="ExtrasScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public ExtrasScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Name = "Extras";
    }

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        UiTheme theme = _context.Theme;
        var canvas = new Canvas();
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 18f };
        column.AddChild(new Label(theme.Heading, "EXTRAS", theme.HeadingTextSize, theme.Accent) { Shadow = theme.Shadow });

        column.AddChild(new Label(theme.Body, "Credits", theme.BodyTextSize * 1.1f, theme.Text));
        column.AddChild(new Label(theme.Body, CreditsText, theme.BodyTextSize, theme.TextMuted)
        {
            Margin = new Thickness(0f, 0f, 0f, 18f),
        });

        column.AddChild(new Label(theme.Body, "Developer", theme.BodyTextSize * 1.1f, theme.Text));

        // Benchmark: the renderer's per-pass GPU timing overlay, bound to its CVar.
        if (_context.Cvars.TryGet("r_perflog", out ICVar? perflog) && perflog is not null
            && CVarBinding.CreateControl(theme, perflog, "Benchmark overlay") is { } benchmark)
        {
            column.AddChild(benchmark);
        }

        column.AddChild(new Label(theme.Body, "Photo mode — press F1 in a mission for the free camera.", theme.BodyTextSize, theme.TextMuted));
        column.AddChild(new Label(theme.Body, "Developer console — press ` at any time.", theme.BodyTextSize, theme.TextMuted));

        canvas.Add(column, Anchor.Filled(64f));
        return canvas;
    }
}
