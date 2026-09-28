using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Player;
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
        "Fonts: Saira and Saira Condensed, under the SIL Open Font License.\n" +
        "Project I.G.I. © 2000 its respective rights holders.";

    // Rebuilt with the tree on every resize, so the old tree's bindings are released first.
    private readonly List<IDisposable> _bindings = [];
    private Panel? _cheatGatePanel;
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
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };

        column.AddChild(new Label(theme.Body, "Credits", theme.BodyTextSize * 1.15f, theme.Accent));
        column.AddChild(new Label(theme.Body, CreditsText, theme.BodyTextSize, theme.TextMuted)
        {
            Margin = new Thickness(0f, 0f, 0f, 18f),
        });

        column.AddChild(new Label(theme.Body, "Developer", theme.BodyTextSize * 1.15f, theme.Accent));

        if (Context.Cvars.TryGet("r_perflog", out ICVar? perflog) && perflog is not null
            && CVarBinding.CreateControl(theme, perflog, "Benchmark overlay", _bindings) is { } benchmark)
        {
            column.AddChild(benchmark);
        }

        if (CVarBinding.CreateControl(theme, PlayerCheatCVars.DeveloperMode, "Developer mode", _bindings) is { } developerMode)
        {
            developerMode.Tooltip = "Expose session-only developer controls in Extras.";
            column.AddChild(developerMode);
        }

        var cheatGate = new Panel();
        var cheatColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        cheatColumn.AddChild(new Label(theme.Body, "Cheats", theme.BodyTextSize * 1.05f, theme.Accent));
        cheatColumn.AddChild(new Label(
            theme.Body,
            "Enable cheats to show the player cheat controls. Values apply to the current session only.",
            theme.BodyTextSize,
            theme.TextMuted));

        if (CVarBinding.CreateControl(theme, PlayerCheatCVars.CheatsEnabled, "Enable cheats", _bindings) is { } enableCheats)
        {
            enableCheats.Tooltip = "Activates the player cheats below for this session.";
            cheatColumn.AddChild(enableCheats);
        }

        _cheatControlsPanel = new Panel();
        var cheatControls = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f };
        cheatControls.AddChild(new Label(
            theme.Body,
            "Gameplay cheats",
            theme.BodyTextSize * 0.95f,
            theme.TextMuted));
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
        cheatColumn.AddChild(_cheatControlsPanel);
        cheatGate.AddChild(cheatColumn);
        _cheatGatePanel = cheatGate;
        column.AddChild(cheatGate);

        RefreshCheatVisibility();

        column.AddChild(new Label(theme.Body, "Photo mode — press F1 in a mission for the free camera.", theme.BodyTextSize, theme.TextMuted));
        column.AddChild(new Label(theme.Body, "Developer console — press ` at any time.", theme.BodyTextSize, theme.TextMuted));

        var scroll = new ScrollView(theme);
        scroll.SetContent(column);
        return scroll;
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
            panel.AddChild(control);
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
