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
    /// <summary>The design-unit width a settings column wants before a second one is worth having.</summary>
    /// <remarks>
    /// A row is a caption at one end and its control at the other, so past about this width the two stop
    /// reading as one row. Rather than narrow the page to suit, the rows are dealt into as many columns of
    /// roughly this width as the page will hold — which is what lets the panel use the whole screen.
    /// </remarks>
    private const float ColumnWidth = 560f;

    /// <summary>What the page's own margins and padding take off the width before it is divided up.</summary>
    private const float PageChrome = 200f;

    /// <summary>The most columns the rows are ever dealt into, however wide the display is.</summary>
    private const int MaxColumns = 3;

    private readonly Dictionary<Control, ICVar> _boundCvars = [];
    private readonly List<UiElement> _tabPanels = [];

    // Each category's focusable rows in reading order — down a column, then on to the top of the next. Up
    // and down walk THIS rather than the spatial traversal, because a control consumes left and right for
    // its own value, so nothing else could carry focus from one column to another. Parallel to _tabPanels.
    private readonly List<List<Control>> _tabRows = [];

    private Label? _help;
    private int _columns = 1;
    private int _activeTab;

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
        _tabRows.Clear();
        _activeTab = 0;

        UiTheme theme = Context.Theme;

        // How many columns the page will hold. Measured in DESIGN units, so it follows the display's shape
        // rather than its resolution — every 16:9 display is 1920 units wide whatever its pixel count, and
        // it is the aspect ratio that decides whether a third column fits. 4:3 gets two, 16:9 three.
        float usable = (context.SafeRect.Width / context.Scale) - PageChrome;
        _columns = Math.Clamp((int)(usable / ColumnWidth), 1, MaxColumns);

        // A grid rather than a stack: the panel row is a star track, so the categories fill everything
        // between the tab strip and the footer whatever the display is. A fixed height left the bottom of
        // a 1440p screen empty and cut the list short on a small one.
        var layout = new Grid { RowSpacing = 10f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto); // the category tabs
        layout.RowDefinitions.Add(GridLength.Star); // the current category, filling the rest
        layout.RowDefinitions.Add(GridLength.Auto); // the per-row help footer

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

        var tabs = new TabStrip(theme, tabTitles) { HorizontalAlignment = HorizontalAlignment.Left };
        tabs.SelectionChanged += ShowTab;
        layout.Add(tabs, 0, 0);

        var content = new Panel();
        for (int i = 0; i < categories.Length; i++)
        {
            Panel panel = categories[i].Build();
            panel.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            _tabPanels.Add(panel);
            content.AddChild(panel);
        }

        layout.Add(content, 1, 0);

        _help = new Label(theme.Body, string.Empty, theme.BodyTextSize * 0.95f, theme.TextMuted)
        {
            Margin = new Thickness(0f, 6f, 0f, 0f),
        };
        layout.Add(_help, 2, 0);
        return layout;
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
    /// <remarks>
    /// Up and down walk the category's rows in the order they were dealt into columns, so the foot of one
    /// column leads to the head of the next. Spatial traversal cannot do it: there is nothing below the
    /// last row of a column, and left and right — which is where the next column is — belong to the
    /// controls themselves. At either end it falls through, so up from the first row still reaches the
    /// category tabs.
    /// </remarks>
    protected override bool MoveFocus(NavDirection direction)
    {
        if (direction is NavDirection.Up or NavDirection.Down
            && Focus?.Focused is Control focused
            && _activeTab >= 0 && _activeTab < _tabRows.Count)
        {
            List<Control> rows = _tabRows[_activeTab];
            int index = rows.IndexOf(focused);
            int next = index + (direction == NavDirection.Down ? 1 : -1);
            if (index >= 0 && next >= 0 && next < rows.Count)
            {
                Focus.Focus(rows[next]);
                return true;
            }
        }

        return base.MoveFocus(direction);
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
        _activeTab = index;
        for (int i = 0; i < _tabPanels.Count; i++)
        {
            _tabPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        // The focused row belongs to the category that has just been collapsed, so it can no longer hold
        // focus; dropping it and taking the first row of the new panel is what keeps the keyboard working
        // after a tab change.
        Focus?.Validate();
        Focus?.FocusFirst();
    }

    private Panel BuildCvarPanel(IReadOnlyList<string> names, bool includePresets)
    {
        UiTheme theme = Context.Theme;
        var rows = new List<Control>();

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
            rows.Add(presets);
        }

        foreach (string name in names)
        {
            if (Context.Cvars.TryGet(name, out ICVar? cvar) && cvar is not null && CVarBinding.CanBind(cvar))
            {
                Control? control = CVarBinding.CreateControl(theme, cvar);
                if (control is not null)
                {
                    _boundCvars[control] = cvar;
                    rows.Add(control);
                }
            }
        }

        return BuildColumns(theme, rows);
    }

    /// <summary>Deals a category's rows into columns across the page and returns the panel holding them.</summary>
    /// <remarks>
    /// Column-major, so a column is read top to bottom and then the next one starts — the order the rows
    /// were declared in, just folded. The split is by count rather than by measured height: rows differ a
    /// little in height (a slider is taller than a toggle) but not enough for an even count to look uneven,
    /// and folding on a number is predictable where folding on a measurement moves as the display changes.
    /// </remarks>
    private Panel BuildColumns(UiTheme theme, List<Control> rows)
    {
        _tabRows.Add(rows);

        ScrollView scroll = BuildScrollHost(theme);
        int columns = Math.Max(1, Math.Min(_columns, rows.Count));
        int perColumn = (int)MathF.Ceiling(rows.Count / (float)columns);

        // The right margin is the scrollbar's lane: the thumb is drawn down the viewport's right edge, and
        // without it the last column's sliders run underneath it.
        var grid = new Grid { ColumnSpacing = 40f, Margin = new Thickness(0f, 0f, 22f, 0f) };
        grid.RowDefinitions.Add(GridLength.Auto);
        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(GridLength.Star);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4f,
                VerticalAlignment = VerticalAlignment.Top,
            };

            for (int i = column * perColumn; i < Math.Min((column + 1) * perColumn, rows.Count); i++)
            {
                stack.AddChild(rows[i]);
            }

            grid.Add(stack, 0, column);
        }

        scroll.SetContent(grid);
        return new Panel().WithChild(scroll);
    }

    private Panel BuildControlsPanel()
    {
        UiTheme theme = Context.Theme;
        var profile = Context.Config.Active;
        if (profile is null || profile.Bindings.Count == 0)
        {
            _tabRows.Add([]); // kept parallel to _tabPanels even when a category has no rows at all
            ScrollView empty = BuildScrollHost(theme);
            empty.SetContent(
                new Label(theme.Body, "No control bindings in the active profile.", theme.BodyTextSize, theme.TextMuted));
            return new Panel().WithChild(empty);
        }

        var rows = new List<Control>(profile.Bindings.Count);
        for (int i = 0; i < profile.Bindings.Count; i++)
        {
            int index = i;
            var binding = profile.Bindings[index];
            KeyCode current = Enum.TryParse(binding.Key, ignoreCase: true, out KeyCode parsed) ? parsed : KeyCode.None;
            var row = new KeyBindingRow(theme, binding.Action, current);
            row.BindingChanged += key => profile.Bindings[index] = binding with { Key = key.ToString() };
            rows.Add(row);
        }

        return BuildColumns(theme, rows);
    }

    /// <summary>The viewport a category's rows scroll inside: the full page, top to bottom and edge to edge.</summary>
    private static ScrollView BuildScrollHost(UiTheme theme) => new(theme);

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
