using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Input;
using OpenIGI.Rendering.Presets;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The settings screen: a tab per category, every row a CVar bound to its control, with a live preview,
/// per-row help, restart badges, reset-to-default and a graphics-preset selector.
/// </summary>
/// <remarks>
/// <para>
/// Categories are grouped by CVar name prefix — <c>r_</c> is graphics/display, <c>snd_</c> audio,
/// <c>g_</c>/<c>ui_</c> gameplay — because that is the only grouping the CVar system carries (there is no
/// category metadata). Controls are the <see cref="CVarBinding"/> rows, so every change previews the
/// instant it is made through the CVar's own change event, exactly as the console does. A row whose CVar
/// requires a restart is flagged in the footer while it is focused.
/// </para>
/// <para>
/// The Controls tab rebinds keys against the active profile. It captures a key code the port understands
/// and stores its name; that is the port's binding, not a faithful DirectInput scan-code name, which is a
/// deliberate simplification until input remap reaches full retail fidelity.
/// </para>
/// </remarks>
public sealed class SettingsScreen : Screen
{
    private readonly ShellContext _context;
    private readonly Dictionary<Control, ICVar> _boundCvars = [];
    private readonly List<UiElement> _tabPanels = [];
    private readonly List<ICVar[]> _tabCvars = [];

    private TabStrip? _tabs;
    private Label? _help;

    /// <summary>Initializes a new instance of the <see cref="SettingsScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SettingsScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Name = "Settings";
    }

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        _boundCvars.Clear();
        _tabPanels.Clear();
        _tabCvars.Clear();

        UiTheme theme = _context.Theme;
        var canvas = new Canvas();
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f };

        column.AddChild(new Label(theme.Heading, "SETTINGS", theme.HeadingTextSize, theme.Accent) { Shadow = theme.Shadow });

        (string Title, Func<Panel> Build)[] categories =
        [
            ("Graphics", () => BuildCvarPanel(NamesWithPrefix("r_", excludeDisplay: true), includePresets: true)),
            ("Display", () => BuildCvarPanel(DisplayNames(), includePresets: false)),
            ("Audio", () => BuildCvarPanel(NamesWithPrefix("snd_"), includePresets: false)),
            ("Controls", BuildControlsPanel),
            ("Gameplay", () => BuildCvarPanel(GameplayNames(), includePresets: false)),
        ];

        string[] tabTitles = new string[categories.Length];
        for (int i = 0; i < categories.Length; i++)
        {
            tabTitles[i] = categories[i].Title;
        }

        _tabs = new TabStrip(theme, tabTitles);
        _tabs.SelectionChanged += ShowTab;
        column.AddChild(_tabs);

        // Build every tab panel once; only the selected one is visible.
        var content = new Panel { Height = 620f };
        for (int i = 0; i < categories.Length; i++)
        {
            Panel panel = categories[i].Build();
            panel.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            _tabPanels.Add(panel);
            content.AddChild(panel);
        }

        column.AddChild(content);

        _help = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.95f, theme.TextMuted)
        {
            Margin = new Thickness(0f, 12f, 0f, 0f),
        };
        column.AddChild(_help);

        canvas.Add(column, Anchor.Filled(64f));
        return canvas;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // A key-binding row that is listening swallows the keyboard until it captures a key, so a stray
        // navigation press does not escape mid-rebind.
        if (Focus?.Focused is KeyBindingRow { IsListening: true } row)
        {
            while (input.Keyboard.TryDequeuePress(out KeyCode key))
            {
                if (row.TryCaptureKey(key))
                {
                    break;
                }
            }

            return false;
        }

        return base.HandleInput(nav, input);
    }

    /// <inheritdoc/>
    protected override void OnExit() => _context.Host.SaveSettings();

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_help is null)
        {
            return;
        }

        // The footer shows the focused row's help and whether it needs a restart.
        if (Focus?.Focused is Control control && _boundCvars.TryGetValue(control, out ICVar? cvar))
        {
            string restart = cvar.Flags.HasFlag(CVarOptions.RequiresRestart) ? "   [restart required]" : string.Empty;
            _help.Text = cvar.Help + restart;
        }
        else if (Focus?.Focused is KeyBindingRow)
        {
            _help.Text = "Press this row, then a key, to rebind it.";
        }
        else
        {
            _help.Text = string.Empty;
        }
    }

    private void ShowTab(int index)
    {
        for (int i = 0; i < _tabPanels.Count; i++)
        {
            _tabPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        // Move focus into the newly-shown tab; the collapsed tabs' controls are no longer focusable.
        Focus?.Validate();
    }

    private Panel BuildCvarPanel(IReadOnlyList<string> names, bool includePresets)
    {
        UiTheme theme = _context.Theme;
        var scroll = new ScrollView(theme);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };

        if (includePresets)
        {
            string[] presetNames = Enum.GetNames<GraphicsPreset>();
            int current = Array.IndexOf(presetNames, _context.Presets.Current.ToString());
            var presets = new Dropdown(theme, "Preset", presetNames, Math.Max(0, current));
            presets.SelectionChanged += i =>
            {
                if (Enum.TryParse(presetNames[i], out GraphicsPreset preset))
                {
                    _context.Presets.Apply(preset);
                }
            };
            _context.Presets.PresetChanged += (_, preset) =>
            {
                int idx = Array.IndexOf(presetNames, preset.ToString());
                if (idx >= 0)
                {
                    presets.SelectedIndex = idx;
                }
            };
            stack.AddChild(presets);
        }

        var cvars = new List<ICVar>();
        foreach (string name in names)
        {
            if (_context.Cvars.TryGet(name, out ICVar? cvar) && cvar is not null && CVarBinding.CanBind(cvar))
            {
                Control? control = CVarBinding.CreateControl(theme, cvar);
                if (control is not null)
                {
                    _boundCvars[control] = cvar;
                    cvars.Add(cvar);
                    stack.AddChild(control);
                }
            }
        }

        _tabCvars.Add([.. cvars]);
        scroll.SetContent(stack);
        return WrapScroll(scroll);
    }

    private Panel BuildControlsPanel()
    {
        UiTheme theme = _context.Theme;
        var scroll = new ScrollView(theme);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };

        PlayerProfileBindings(stack, theme);
        _tabCvars.Add([]);
        scroll.SetContent(stack);
        return WrapScroll(scroll);
    }

    private void PlayerProfileBindings(StackPanel stack, UiTheme theme)
    {
        var profile = _context.Config.Active;
        if (profile is null || profile.Bindings.Count == 0)
        {
            stack.AddChild(new Label(theme.Body, "No control bindings in the active profile.", theme.BodyTextSize, theme.TextMuted));
            return;
        }

        for (int i = 0; i < profile.Bindings.Count; i++)
        {
            int index = i;
            var binding = profile.Bindings[index];
            KeyCode current = Enum.TryParse(binding.Key, ignoreCase: true, out KeyCode parsed) ? parsed : KeyCode.None;
            var row = new KeyBindingRow(theme, binding.Action, current);
            row.BindingChanged += key => profile.Bindings[index] = binding with { Key = key.ToString() };
            stack.AddChild(row);
        }
    }

    private static Panel WrapScroll(ScrollView scroll) => new Panel().Also(p => p.AddChild(scroll));

    private List<string> NamesWithPrefix(string prefix, bool excludeDisplay = false)
    {
        var names = new List<string>();
        foreach (ICVar cvar in _context.Cvars.All)
        {
            if (cvar.Name.StartsWith(prefix, StringComparison.Ordinal)
                && !cvar.Flags.HasFlag(CVarOptions.Hidden)
                && !(excludeDisplay && IsDisplayName(cvar.Name)))
            {
                names.Add(cvar.Name);
            }
        }

        return names;
    }

    private List<string> DisplayNames()
    {
        var names = new List<string>();
        foreach (ICVar cvar in _context.Cvars.All)
        {
            if (IsDisplayName(cvar.Name) && !cvar.Flags.HasFlag(CVarOptions.Hidden))
            {
                names.Add(cvar.Name);
            }
        }

        return names;
    }

    private List<string> GameplayNames()
    {
        var names = new List<string>();
        foreach (ICVar cvar in _context.Cvars.All)
        {
            if ((cvar.Name.StartsWith("g_", StringComparison.Ordinal) || cvar.Name.StartsWith("ui_", StringComparison.Ordinal))
                && !cvar.Flags.HasFlag(CVarOptions.Hidden))
            {
                names.Add(cvar.Name);
            }
        }

        return names;
    }

    private static bool IsDisplayName(string name) =>
        name is "r_vsync" or "r_scale" or "r_smooth" or "r_msaa";
}

/// <summary>Small fluent helper for one-line child wiring.</summary>
internal static class PanelFluent
{
    public static Panel Also(this Panel panel, Action<Panel> configure)
    {
        configure(panel);
        return panel;
    }
}
