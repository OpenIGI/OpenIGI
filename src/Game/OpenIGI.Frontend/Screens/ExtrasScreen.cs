using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Player;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
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
/// <para>
/// The player cheats sit behind two switches, so they are never one click away: "Developer mode" shows
/// the section, "Enable cheats" shows the controls and puts them in force. None of them is saved.
/// </para>
/// </remarks>
public sealed class ExtrasScreen : ShellScreen
{
    private const string CreditsText =
        "OpenIGI — a clean-room reimplementation of Project I.G.I.\n\n" +
        "Engine, renderer and cinematic shell: the OpenIGI contributors.\n" +
        "Reverse engineering against the original binary; no original code is used.\n\n" +
        "Fonts: Barlow, Barlow Condensed and JetBrains Mono, under the SIL Open Font License.\n" +
        "Project I.G.I. © 2000 its respective rights holders.";

    // Rebuilt with the tree on every resize, so the old tree's bindings are released first.
    private readonly List<IDisposable> _bindings = [];

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
        DisposeBindings();
        UiTheme theme = Context.Theme;

        bool compact = Size == ShellSize.Compact;
        float text = compact ? 15f : 18f;
        var inset = new Thickness(compact ? 16f : 20f, 6f);

        var credits = Group(theme, "Credits", compact);
        credits.Content.AddChild(new Label(theme.Body, CreditsText, text, theme.Text) { Wrap = true, Margin = inset });

        var developer = Group(theme, "Developer", compact);
        if (Context.Cvars.TryGet("r_perflog", out ICVar? perflog) && perflog is not null
            && CVarBinding.CreateControl(theme, perflog, "Benchmark overlay", _bindings) is { } benchmark)
        {
            benchmark.Hint = "Frame time and GPU timings in a corner of the screen.";
            developer.Content.AddChild(benchmark);
        }

        if (CVarBinding.CreateControl(theme, PlayerCheatCVars.DeveloperMode, "Developer mode", _bindings) is { } developerMode)
        {
            developerMode.Hint = "Debug views and the developer console, for this session.";
            developerMode.Tooltip = "Session-only developer controls. The cheats are in Settings, under Gameplay.";
            developer.Content.AddChild(developerMode);
        }

        // The keys are a keyboard's: a touch screen has neither, and is told nothing of them.
        if (!Context.Host.TouchScreen)
        {
            var shortcuts = Group(theme, "Shortcuts", compact);
            shortcuts.Content.AddChild(new Label(theme.Body, "Photo mode: press F1 in a mission for the free camera.", text, theme.Text) { Wrap = true, Margin = inset });
            shortcuts.Content.AddChild(new Label(theme.Body, "Developer console: press ` at any time.", text, theme.Text) { Wrap = true, Margin = inset });
            developer.Column.AddChild(shortcuts.Column);
        }

        // Side by side where there is room for two columns of readable lines, one above the other where there is not.
        UiElement columns;
        if (compact)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, Margin = new Thickness(0f, 0f, 10f, 16f) };
            stack.AddChild(credits.Column);
            stack.AddChild(developer.Column);
            columns = stack;
        }
        else
        {
            var grid = new Grid { ColumnSpacing = 20f, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0f, 0f, 18f, 16f) };
            grid.RowDefinitions.Add(GridLength.Auto);
            grid.ColumnDefinitions.Add(GridLength.Star);
            grid.ColumnDefinitions.Add(GridLength.Star);
            grid.Add(credits.Column, 0, 0);
            grid.Add(developer.Column, 0, 1);
            columns = grid;
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(columns);

        var page = new Grid
        {
            RowSpacing = compact ? 8f : 24f,
            Padding = compact ? new Thickness(SideMargin, 6f, SideMargin, 0f) : new Thickness(SideMargin, 32f, SideMargin, 0f),
        };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(
            compact
                ? new SectionTitle(theme, string.Empty, "Extras") { TitleSize = 24f, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 10f, 0f, 4f) }
                : new SectionTitle(theme, "Archive", "Extras") { HorizontalAlignment = HorizontalAlignment.Left },
            0,
            0);
        page.Add(scroll, 1, 0);
        return page;
    }

    // A group in its panel, and the column the panel stands in so another can be put under it.
    private static (GlassPanel Panel, StackPanel Content, StackPanel Column) Group(UiTheme theme, string title, bool compact)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.AddChild(new GroupHeader(theme, title)
        {
            Margin = compact ? new Thickness(16f, 12f, 16f, 4f) : new Thickness(20f, 28f, 20f, 14f),
        });
        var panel = new GlassPanel(theme)
        {
            Tint = OpenIGI.Core.Graphics.Bgra32.FromRgba(12, 14, 15, compact ? (byte)235 : (byte)199),
            Border = UiPalette.White(0.12f),
            Padding = compact ? new Thickness(0f, 0f, 0f, 10f) : new Thickness(8f, 0f, 8f, 20f),
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.AddChild(content);
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 10f : 20f, VerticalAlignment = VerticalAlignment.Top };
        column.AddChild(panel);
        return (panel, content, column);
    }

    /// <inheritdoc/>
    protected override void OnExit() => DisposeBindings();

    private void DisposeBindings()
    {
        foreach (IDisposable binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
    }
}
