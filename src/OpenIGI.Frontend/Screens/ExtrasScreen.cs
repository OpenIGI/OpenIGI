using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The extras section: the credits, and the developer-facing toggles — the benchmark overlay and the
/// reminders for photo mode and the console.
/// </summary>
/// <remarks>
/// The benchmark toggle binds the existing <c>r_perflog</c> CVar, which already measures and reports the
/// per-pass GPU timings, so "benchmark reporting per-pass timings" is the switch for a facility the
/// renderer provides. Photo mode and the developer console are reached with the keys the game already
/// binds; the screen points to them rather than duplicating them.
/// </remarks>
public sealed class ExtrasScreen : ShellScreen
{
    private const string CreditsText =
        "OpenIGI — a clean-room reimplementation of Project I.G.I.\n\n" +
        "Engine, renderer and cinematic shell: the OpenIGI contributors.\n" +
        "Reverse engineering against the original binary; no original code is used.\n\n" +
        "Fonts: Saira and Saira Condensed, under the SIL Open Font License.\n" +
        "Project I.G.I. © 2000 its respective rights holders.";

    /// <summary>Initializes a new instance of the <see cref="ExtrasScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public ExtrasScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Extras;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };

        column.AddChild(new Label(theme.Body, "Credits", theme.BodyTextSize * 1.15f, theme.Accent));
        column.AddChild(new Label(theme.Body, CreditsText, theme.BodyTextSize, theme.TextMuted)
        {
            Margin = new Thickness(0f, 0f, 0f, 18f),
        });

        column.AddChild(new Label(theme.Body, "Developer", theme.BodyTextSize * 1.15f, theme.Accent));

        if (Context.Cvars.TryGet("r_perflog", out ICVar? perflog) && perflog is not null
            && CVarBinding.CreateControl(theme, perflog, "Benchmark overlay") is { } benchmark)
        {
            column.AddChild(benchmark);
        }

        column.AddChild(new Label(theme.Body, "Photo mode — press F1 in a mission for the free camera.", theme.BodyTextSize, theme.TextMuted));
        column.AddChild(new Label(theme.Body, "Developer console — press ` at any time.", theme.BodyTextSize, theme.TextMuted));
        return column;
    }
}
