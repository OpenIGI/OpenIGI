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
    private UiElement? _cheatGatePanel;
    private Panel? _cheatControlsPanel;

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

        var credits = Group(theme, "Credits");
        credits.Content.AddChild(new Label(theme.Body, CreditsText, 17f, UiPalette.TextBody)
        {
            Wrap = true,
            Margin = new Thickness(0f, 4f, 0f, 10f),
        });

        var developer = Group(theme, "Developer");
        if (Context.Cvars.TryGet("r_perflog", out ICVar? perflog) && perflog is not null
            && CVarBinding.CreateControl(theme, perflog, "Benchmark overlay", _bindings) is { } benchmark)
        {
            developer.Content.AddChild(Row(benchmark));
        }

        if (CVarBinding.CreateControl(theme, PlayerCheatCVars.DeveloperMode, "Developer mode", _bindings) is { } developerMode)
        {
            developerMode.Tooltip = "Expose session-only developer controls in Extras.";
            developer.Content.AddChild(Row(developerMode));
        }

        developer.Content.AddChild(new GroupHeader(theme, "Shortcuts") { Margin = new Thickness(0f, 18f, 0f, 4f) });
        developer.Content.AddChild(new Label(theme.Body, "Photo mode — press F1 in a mission for the free camera.", 17f, UiPalette.TextBody) { Wrap = true, Margin = new Thickness(0f, 6f) });
        developer.Content.AddChild(new Label(theme.Body, "Developer console — press ` at any time.", 17f, UiPalette.TextBody) { Wrap = true, Margin = new Thickness(0f, 6f) });

        var cheats = Group(theme, "Cheats");
        cheats.Content.AddChild(new Label(
            theme.Body,
            "Enable cheats to show the player cheat controls. Values apply to the current session only.",
            16f,
            theme.TextMuted)
        {
            Wrap = true,
            Margin = new Thickness(0f, 2f, 0f, 8f),
        });

        if (CVarBinding.CreateControl(theme, PlayerCheatCVars.CheatsEnabled, "Enable cheats", _bindings) is { } enableCheats)
        {
            enableCheats.Tooltip = "Activates the player cheats below for this session.";
            cheats.Content.AddChild(Row(enableCheats));
        }

        _cheatControlsPanel = new Panel();
        var cheatControls = new StackPanel { Orientation = Orientation.Vertical };
        cheatControls.AddChild(new GroupHeader(theme, "Gameplay cheats") { Margin = new Thickness(0f, 18f, 0f, 4f) });
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.Speed, "Speed multiplier");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.Jump, "High jump multiplier");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.HealthScale, "Health multiplier");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.InfiniteHealth, "Infinite health");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.InfiniteAmmo, "Infinite ammo");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.AllWeapons, "Give all weapons");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.UnlockAllLevels, "Unlock all levels");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.NoRecoil, "No recoil");
        AddCheatControl(cheatControls, theme, PlayerCheatCVars.NoReload, "No reload");
        _cheatControlsPanel.AddChild(cheatControls);
        cheats.Content.AddChild(_cheatControlsPanel);
        _cheatGatePanel = cheats.Panel;

        RefreshCheatVisibility();

        var columns = new Grid { ColumnSpacing = 24f, VerticalAlignment = VerticalAlignment.Top };
        columns.RowDefinitions.Add(GridLength.Auto);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.Add(credits.Panel, 0, 0);
        columns.Add(developer.Panel, 0, 1);
        columns.Add(cheats.Panel, 0, 2);

        var scroll = new ScrollView(theme);
        scroll.SetContent(columns);

        var page = new Grid { RowSpacing = 36f, Padding = new Thickness(SideMargin, 48f, SideMargin, 0f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, "Archive", "Extras") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        page.Add(scroll, 1, 0);
        return page;
    }

    /// <summary>A group of the page: a glass panel holding a heading and its rows.</summary>
    private static (GlassPanel Panel, StackPanel Content) Group(UiTheme theme, string title)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.AddChild(new GroupHeader(theme, title));
        var panel = new GlassPanel(theme) { Padding = new Thickness(28f, 28f, 28f, 20f), VerticalAlignment = VerticalAlignment.Top };
        panel.AddChild(content);
        return (panel, content);
    }

    /// <summary>A settings row widened into the panel's padding, so its hover wash reaches the panel's edge as the design's does.</summary>
    private static UiElement Row(UiElement row)
    {
        row.Margin = new Thickness(-10f, 0f);
        return row;
    }

    /// <inheritdoc/>
    protected override void OnEnter()
    {
        PlayerCheatCVars.DeveloperMode.Changed += OnCheatGateChanged;
        PlayerCheatCVars.CheatsEnabled.Changed += OnCheatGateChanged;
    }

    /// <inheritdoc/>
    protected override void OnExit()
    {
        DisposeBindings();
        PlayerCheatCVars.DeveloperMode.Changed -= OnCheatGateChanged;
        PlayerCheatCVars.CheatsEnabled.Changed -= OnCheatGateChanged;
    }

    private void DisposeBindings()
    {
        foreach (IDisposable binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
    }

    private void AddCheatControl(
        StackPanel panel,
        UiTheme theme,
        ICVar cvar,
        string label)
    {
        if (CVarBinding.CreateControl(theme, cvar, label, _bindings) is { } control)
        {
            control.Tooltip = cvar.Help;
            panel.AddChild(Row(control));
        }
    }

    private void OnCheatGateChanged(object? sender, CVarChangedEventArgs args) => RefreshCheatVisibility();

    private void RefreshCheatVisibility()
    {
        bool developerMode = PlayerCheatCVars.DeveloperMode.Value;
        bool cheatsEnabled = PlayerCheatCVars.CheatsEnabled.Value;
        if (_cheatGatePanel is not null)
        {
            _cheatGatePanel.Visibility = developerMode ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_cheatControlsPanel is not null)
        {
            _cheatControlsPanel.Visibility = developerMode && cheatsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
