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
/// The settings section: a category tab strip, every row a CVar bound to its control, with a live
/// preview, per-row help, restart notes, and a graphics-preset selector.
/// </summary>
/// <remarks>
/// <para>
/// Categories are grouped by CVar name prefix — <c>r_</c> is graphics/display, <c>snd_</c> audio,
/// <c>g_</c>/<c>ui_</c> gameplay — because that is the only grouping the CVar system carries. Controls are
/// the <see cref="CVarBinding"/> rows, so every change previews the instant it is made through the CVar's
/// own change event. A row whose CVar requires a restart is flagged in the footer while it is focused.
/// </para>
/// <para>
/// The Controls tab rebinds keys against the active profile. It captures a key code the port understands
/// and stores its name; that is the port's binding, not a faithful DirectInput scan-code name — a
/// deliberate simplification until input remap reaches full retail fidelity.
/// </para>
/// </remarks>
public sealed class SettingsScreen : ShellScreen
{
    private readonly Dictionary<Control, ICVar> _boundCvars = [];
    private readonly List<UiElement> _tabPanels = [];

    private Label? _help;

    /// <summary>Initializes a new instance of the <see cref="SettingsScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public SettingsScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Settings;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        _boundCvars.Clear();
        _tabPanels.Clear();

        UiTheme theme = Context.Theme;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14f };

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

        var tabs = new TabStrip(theme, tabTitles);
        tabs.SelectionChanged += ShowTab;
        column.AddChild(tabs);

        var content = new Panel { Height = 560f };
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
            Margin = new Thickness(0f, 8f, 0f, 0f),
        };
        column.AddChild(_help);
        return column;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // A key-binding row that is listening swallows the keyboard until it captures a key.
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
    protected override void OnExit() => Context.Host.SaveSettings();

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_help is null)
        {
            return;
        }

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

        Focus?.Validate();
    }

    private Panel BuildCvarPanel(IReadOnlyList<string> names, bool includePresets)
    {
        UiTheme theme = Context.Theme;
        var scroll = new ScrollView(theme);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };

        if (includePresets)
        {
            string[] presetNames = Enum.GetNames<GraphicsPreset>();
            int current = Array.IndexOf(presetNames, Context.Presets.Current.ToString());
            var presets = new Dropdown(theme, "Preset", presetNames, Math.Max(0, current));
            presets.SelectionChanged += i =>
            {
                if (Enum.TryParse(presetNames[i], out GraphicsPreset preset))
                {
                    Context.Presets.Apply(preset);
                }
            };
            Context.Presets.PresetChanged += (_, preset) =>
            {
                int idx = Array.IndexOf(presetNames, preset.ToString());
                if (idx >= 0)
                {
                    presets.SelectedIndex = idx;
                }
            };
            stack.AddChild(presets);
        }

        foreach (string name in names)
        {
            if (Context.Cvars.TryGet(name, out ICVar? cvar) && cvar is not null && CVarBinding.CanBind(cvar))
            {
                Control? control = CVarBinding.CreateControl(theme, cvar);
                if (control is not null)
                {
                    _boundCvars[control] = cvar;
                    stack.AddChild(control);
                }
            }
        }

        scroll.SetContent(stack);
        return new Panel().WithChild(scroll);
    }

    private Panel BuildControlsPanel()
    {
        UiTheme theme = Context.Theme;
        var scroll = new ScrollView(theme);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };

        var profile = Context.Config.Active;
        if (profile is null || profile.Bindings.Count == 0)
        {
            stack.AddChild(new Label(theme.Body, "No control bindings in the active profile.", theme.BodyTextSize, theme.TextMuted));
        }
        else
        {
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

        scroll.SetContent(stack);
        return new Panel().WithChild(scroll);
    }

    private List<string> NamesWithPrefix(string prefix, bool excludeDisplay = false)
    {
        var names = new List<string>();
        foreach (ICVar cvar in Context.Cvars.All)
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
        foreach (ICVar cvar in Context.Cvars.All)
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
        foreach (ICVar cvar in Context.Cvars.All)
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
    public static Panel WithChild(this Panel panel, UiElement child)
    {
        panel.AddChild(child);
        return panel;
    }
}
