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
    // Who made what, as the credits set it out: a caption over each line.
    private static readonly (string Caption, string Text)[] Credits =
    [
        ("Engine, renderer and cinematic shell", "The OpenIGI contributors"),
        ("Method", "Reverse engineering against the original binary. No original code is used."),
        ("Fonts", "Barlow, Barlow Condensed and JetBrains Mono, under the SIL Open Font License"),
        ("Original game", "Project I.G.I. © 2000 its respective rights holders"),
    ];

    // A keyboard's shortcuts: the key, what it does, and when.
    private static readonly (string Key, string Title, string When)[] Shortcuts =
    [
        ("F1", "Photo mode", "In a mission, for the free camera"),
        ("`", "Developer console", "At any time"),
    ];

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
    protected override IReadOnlyList<KeyHint> Hints => [new("↑ ↓", "Select"), new("Esc", "Back")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        DisposeBindings();
        UiTheme theme = Context.Theme;

        bool compact = Size == ShellSize.Compact;
        float text = compact ? 16f : 20f;
        var inset = new Thickness(compact ? 16f : 20f, 6f);

        var credits = Group(theme, "Credits", compact);
        credits.Content.AddChild(new Label(theme.Heading, "OpenIGI is a clean-room reimplementation of Project I.G.I.", compact ? 22f : 34f, theme.Text) { Wrap = true, Margin = new Thickness(inset.Left, 0f, inset.Right, compact ? 10f : 18f) });
        var facts = new Grid { ColumnSpacing = 32f, RowSpacing = 18f, Margin = new Thickness(inset.Left, 0f, inset.Right, 18f) };
        int across = compact ? 1 : 2;

        // A fact's width, worked out from the page's: a line that wraps is measured at the width it is given, and a
        // column of a grid has none until its rows have been measured.
        float canvas = ShellSizes.Canvas(context).X;
        float panelWidth = compact
            ? canvas - RailWidth - (SideMargin * 2f) - 10f
            : ((canvas - (SideMargin * 2f) - 18f - 24f) * 1.25f / 2.25f) - 16f;
        float factWidth = MathF.Max(120f, (panelWidth - inset.Horizontal - (32f * (across - 1))) / across);
        for (int column = 0; column < across; column++)
        {
            facts.ColumnDefinitions.Add(GridLength.Star);
        }

        for (int i = 0; i < Credits.Length; i++)
        {
            if (i % across == 0)
            {
                facts.RowDefinitions.Add(GridLength.Auto);
            }

            var fact = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f };
            fact.AddChild(new Eyebrow(theme, Credits[i].Caption) { TextSize = compact ? 12f : 14f, Color = theme.TextMuted });
            fact.AddChild(new Label(theme.Body, Credits[i].Text, compact ? 15f : 17f, theme.Text) { Wrap = true, Width = factWidth });
            facts.Add(fact, i / across, i % across);
        }

        credits.Content.AddChild(facts);
        credits.Content.AddChild(new Label(theme.Mono, BuildLine(), 13f, theme.TextMuted) { Margin = new Thickness(inset.Left, 0f, inset.Right, 0f) });

        var developer = Group(theme, "Developer", compact);
        if (Context.Cvars.TryGet("r_perflog", out ICVar? perflog) && perflog is not null
            && CVarBinding.CreateControl(theme, perflog, "Benchmark overlay", _bindings) is { } benchmark)
        {
            benchmark.Hint = "Shows frame time and GPU timings in a corner of the screen.";
            developer.Content.AddChild(benchmark);
        }

        if (CVarBinding.CreateControl(theme, PlayerCheatCVars.DeveloperMode, "Developer mode", _bindings) is { } developerMode)
        {
            developerMode.Hint = "Unlocks debug views and the developer console.";
            developerMode.Tooltip = "Session-only developer controls. The cheats are in Settings, under Gameplay.";
            developer.Content.AddChild(developerMode);
        }

        // The keys are a keyboard's: a touch screen has neither, and is told nothing of them.
        if (!Context.Host.TouchScreen)
        {
            var shortcuts = Group(theme, "Shortcuts", compact);
            for (int i = 0; i < Shortcuts.Length; i++)
            {
                var line = new Grid { ColumnSpacing = 16f, Margin = new Thickness(inset.Left, 0f, inset.Right, 0f), Height = 52f };
                line.RowDefinitions.Add(GridLength.Star);
                line.ColumnDefinitions.Add(GridLength.Absolute(48f));
                line.ColumnDefinitions.Add(GridLength.Star);
                line.Add(new KeyCapLabel(theme, Shortcuts[i].Key) { TextSize = 14f, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
                var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0f, VerticalAlignment = VerticalAlignment.Center };
                words.AddChild(new Label(theme.BodyStrong, Shortcuts[i].Title, text, theme.Text));
                words.AddChild(new Label(theme.Body, Shortcuts[i].When, 15f, theme.TextSecondary));
                line.Add(words, 0, 1);
                shortcuts.Content.AddChild(line);
                if (i + 1 < Shortcuts.Length)
                {
                    shortcuts.Content.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f), Margin = new Thickness(0f, 0f) });
                }
            }
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
            var grid = new Grid { ColumnSpacing = 24f, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0f, 0f, 18f, 16f) };
            grid.RowDefinitions.Add(GridLength.Auto);
            grid.ColumnDefinitions.Add(GridLength.Stars(1.25f));
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

    // The build this is: the version, and the commit where it carries one.
    private static string BuildLine() =>
        BuildStamp.Version.Length == 0 ? "Build unknown"
        : BuildStamp.Commit.Length == 0 ? $"Build {BuildStamp.Version}"
        : $"Build {BuildStamp.Version} · {BuildStamp.Commit}";

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
