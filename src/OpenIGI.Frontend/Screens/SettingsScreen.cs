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

    /// <summary>How many rows a category needs before it is dealt into another column.</summary>
    private const int MinRowsPerColumn = 10;

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

        // Advanced is not a category. It is the second half of Graphics — the variables the graphics
        // choices are made of — so it belongs behind a switch inside that page rather than as a sixth
        // heading beside Audio and Controls, which would say it is a different subject.
        (string Title, Func<Panel> Build)[] categories =
        [
            ("Graphics", BuildGraphicsPanel),
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
        else if (Focus?.Focused is Control { Tooltip.Length: > 0 } explained)
        {
            // A row that is not one variable — a quality group, the preset — says the same thing the
            // pointer would be told, cut to the footer's one line. The rest is in the tooltip.
            _help.Text = FirstLine(explained.Tooltip);
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

    /// <summary>The graphics category: the choices a player came for, with the variables behind a switch.</summary>
    /// <remarks>
    /// <para>
    /// Both halves are built here and both stay built; the switch collapses one and shows the other. Which
    /// means the category owns two sets of rows where every other category owns one, so the list the focus
    /// walk reads is a single instance whose <em>contents</em> the switch replaces — see
    /// <see cref="MoveFocus"/>, which walks the active category's rows in the order they were dealt.
    /// </para>
    /// <para>
    /// Basic first: the preset, because it is the one control that answers the whole question at once, then
    /// a group per subsystem, then the few settings that are one number and are better left as that number.
    /// Every variable any of it writes is a row under Advanced — see <see cref="BasicGraphics"/>.
    /// </para>
    /// </remarks>
    private Panel BuildGraphicsPanel()
    {
        UiTheme theme = Context.Theme;

        var basicRows = new List<Control> { BuildPresetControl(theme) };
        foreach (QualityGroup group in BasicGraphics.Groups)
        {
            if (QualityBinding.CreateControl(theme, Context.Cvars, group) is { } control)
            {
                // The tooltip names the variables the row drives, so Advanced is discoverable from Basic
                // rather than being somewhere you have to already know about.
                control.Tooltip = $"{group.Help}\n\nSets: {string.Join(", ", group.Variables)}  (Advanced)";
                basicRows.Add(control);
            }
        }

        AddCvarRows(theme, basicRows, BasicGraphics.DirectVariables);

        var advancedRows = new List<Control>();
        AddCvarRows(theme, advancedRows, AdvancedGraphicsNames());

        Panel basic = BuildColumns(theme, basicRows, register: false);
        Panel advanced = BuildColumns(theme, advancedRows, register: false);
        advanced.Visibility = Visibility.Collapsed;

        // The one list the focus walk reads for this category; the switch swaps what is in it.
        var active = new List<Control>(basicRows);
        _tabRows.Add(active);

        var layout = new Grid { RowSpacing = 8f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto); // the basic / advanced switch
        layout.RowDefinitions.Add(GridLength.Star); // whichever half is showing

        Button? basicButton = null;
        Button? advancedButton = null;

        void Show(bool showAdvanced)
        {
            basic.Visibility = showAdvanced ? Visibility.Collapsed : Visibility.Visible;
            advanced.Visibility = showAdvanced ? Visibility.Visible : Visibility.Collapsed;

            if (basicButton is not null)
            {
                basicButton.Selected = !showAdvanced;
            }

            if (advancedButton is not null)
            {
                advancedButton.Selected = showAdvanced;
            }

            active.Clear();
            active.AddRange(showAdvanced ? advancedRows : basicRows);

            // The focused row belongs to the half that has just been collapsed, so it can no longer hold
            // focus; dropping it and taking the first row of the other half keeps the keyboard working.
            Focus?.Validate();
            Focus?.FocusFirst();
        }

        var strip = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 4f,
            Justify = FlexJustify.Start,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        basicButton = BuildHalfButton(theme, "Basic", "The settings most players want, each standing for "
            + "several of the renderer's own.", selected: true, () => Show(showAdvanced: false));
        advancedButton = BuildHalfButton(theme, "Advanced", "Every graphics variable the renderer has, one "
            + "row each — including the ones the basic choices write.", selected: false, () => Show(showAdvanced: true));

        strip.Add(basicButton);
        strip.Add(advancedButton);
        layout.Add(strip, 0, 0);

        var halves = new Panel();
        halves.AddChild(basic);
        halves.AddChild(advanced);
        layout.Add(halves, 1, 0);

        return new Panel().WithChild(layout);
    }

    private static Button BuildHalfButton(UiTheme theme, string text, string tooltip, bool selected, Action activated)
    {
        var button = new Button(theme, text)
        {
            TextSize = theme.BodyTextSize * 0.95f,
            Padding = new Thickness(18f, 6f),
            Accent = ButtonAccent.Underline,
            Selected = selected,
            Tooltip = tooltip,
        };

        button.Activated += _ => activated();
        return button;
    }

    private Panel BuildCvarPanel(IReadOnlyList<string> names, bool includePresets)
    {
        UiTheme theme = Context.Theme;
        var rows = new List<Control>();

        if (includePresets)
        {
            rows.Add(BuildPresetControl(theme));
        }

        AddCvarRows(theme, rows, names);
        return BuildColumns(theme, rows);
    }

    private Dropdown BuildPresetControl(UiTheme theme)
    {
        string[] presetNames = Enum.GetNames<GraphicsPreset>();
        int current = Array.IndexOf(presetNames, Context.Presets.Current.ToString());
        var presets = new Dropdown(theme, "Preset", presetNames, Math.Max(0, current))
        {
            Tooltip = "Sets every graphics variable at once. Custom is what the settings become as soon as "
                + "you change any of them yourself — it is a reading, not a choice.",
        };

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

        return presets;
    }

    private void AddCvarRows(UiTheme theme, List<Control> rows, IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (Context.Cvars.TryGet(name, out ICVar? cvar) && cvar is not null && CVarBinding.CanBind(cvar))
            {
                Control? control = CVarBinding.CreateControl(theme, cvar);
                if (control is not null)
                {
                    // The variable already carries the sentence that explains it — the same one the console
                    // prints — so the tooltip is the help text the CVar was declared with rather than a
                    // second description kept beside it and left to drift.
                    control.Tooltip = cvar.Flags.HasFlag(CVarOptions.RequiresRestart)
                        ? $"{cvar.Help}\n\nTakes effect after a restart.\n\n{name}"
                        : $"{cvar.Help}\n\n{name}";

                    _boundCvars[control] = cvar;
                    rows.Add(control);
                }
            }
        }
    }

    /// <summary>Deals a category's rows into columns across the page and returns the panel holding them.</summary>
    /// <remarks>
    /// Column-major, so a column is read top to bottom and then the next one starts — the order the rows
    /// were declared in, just folded. The split is by count rather than by measured height: rows differ a
    /// little in height (a slider is taller than a toggle) but not enough for an even count to look uneven,
    /// and folding on a number is predictable where folding on a measurement moves as the display changes.
    /// </remarks>
    /// <param name="theme">The theme the rows draw from.</param>
    /// <param name="rows">The rows to deal.</param>
    /// <param name="register">
    /// Whether these rows are the category's own, for the focus walk. False for a category that owns more
    /// than one set of them and registers the active one itself — the graphics page and its two halves.
    /// </param>
    private Panel BuildColumns(UiTheme theme, List<Control> rows, bool register = true)
    {
        if (register)
        {
            _tabRows.Add(rows);
        }

        ScrollView scroll = BuildScrollHost(theme);

        // A column has to be worth having. The page is dealt into as many as the display will hold, which
        // is right for the advanced tab's fifty rows and wrong for the basic tab's dozen: three columns of
        // four filled the top fifth of the screen and left the rest blank, which reads as a page that
        // failed to load rather than as a short one.
        int wanted = (int)MathF.Ceiling(rows.Count / (float)MinRowsPerColumn);
        int columns = Math.Clamp(Math.Min(_columns, wanted), 1, Math.Max(1, rows.Count));
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
            var row = new KeyBindingRow(theme, binding.Action, current)
            {
                Tooltip = $"Press this row, then a key, to rebind {binding.Action}.",
            };

            row.BindingChanged += key => profile.Bindings[index] = binding with { Key = key.ToString() };
            rows.Add(row);
        }

        return BuildColumns(theme, rows);
    }

    /// <summary>The viewport a category's rows scroll inside: the full page, top to bottom and edge to edge.</summary>
    private static ScrollView BuildScrollHost(UiTheme theme) => new(theme);

    /// <summary>The first line of a tooltip, which is the sentence that says what the row is.</summary>
    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
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

    /// <summary>Every graphics variable that is not on the Display tab and not already a Basic row.</summary>
    /// <remarks>
    /// The variables the Basic groups <em>write</em> are deliberately still here: that is the relationship
    /// the two pages have — Shadow Quality is a name for these five rows, not a replacement for them, and
    /// moving one of them by hand is what turns the group's reading to Custom. Only the handful Basic shows
    /// as themselves are dropped, because two rows for one number would be two places to change one thing.
    /// </remarks>
    private List<string> AdvancedGraphicsNames()
    {
        var names = new List<string>();
        foreach (string name in NamesWithPrefix("r_", excludeDisplay: true))
        {
            if (!BasicGraphics.DirectVariables.Contains(name))
            {
                names.Add(name);
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
