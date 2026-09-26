using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
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
/// The Controls tab rebinds keyboard keys and mouse buttons against the active profile and persists their
/// retail input symbols, so the same assignments the player sees are the ones gameplay resolves.
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
    private KeyBindingRow? _armingBindingRow;

    /// <summary>
    /// Whether a wheel scroll may bind right now. Scrolling the list to reach a row leaves
    /// inertial motion (trackpad momentum especially) that is still non-zero when the prompt arms;
    /// capturing it would rebind without intent. The gate opens on the first quiet frame after
    /// arming, so only motion that starts after the prompt does.
    /// </summary>
    private bool _wheelGateOpen = true;

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

        // Five categories and no sixth. There used to be an Advanced half of Graphics, behind a switch,
        // listing every r_ variable the renderer has as its own row. It is gone: a menu that offers
        // "surface-cache relight amortisation" as a slider has not given the player more control, it has
        // given them a decision they cannot make. The console reaches all of it, by name and with help,
        // which is what an advanced interface should be. See GraphicsMenu.
        //
        // A build locked to one preset has no Graphics category at all: every row on it is either the
        // preset or a step of one, and offering them would only move the settings off the preset the
        // build is held on.
        List<(string Title, Func<Panel> Build)> categories =
        [
            ("Display", () => BuildSettingsPanel(GraphicsMenu.DisplaySettings)),
            ("Audio", () => BuildCvarPanel(NamesWithPrefix("snd_"), includePresets: false)),
            ("Controls", BuildControlsPanel),
            ("Gameplay", BuildGameplayPanel),
        ];

        if (Context.Presets.LockedPreset is null)
        {
            categories.Insert(0, ("Graphics", BuildGraphicsPanel));
        }

        string[] tabTitles = new string[categories.Count];
        for (int i = 0; i < categories.Count; i++)
        {
            tabTitles[i] = categories[i].Title;
        }

        var tabs = new TabStrip(theme, tabTitles) { HorizontalAlignment = HorizontalAlignment.Left };
        tabs.SelectionChanged += ShowTab;
        layout.Add(tabs, 0, 0);

        var content = new Panel();
        for (int i = 0; i < categories.Count; i++)
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
    public override bool CapturesQueuedNavigation =>
        Focus?.Focused is KeyBindingRow { IsListening: true };

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // A key-binding row that is listening swallows the keyboard until it captures a key.
        if (Focus?.Focused is KeyBindingRow { IsListening: true } row)
        {
            // Activation through Enter is itself queued as a key press, and navigation presses can have
            // been waiting in that queue since before the row was selected. Establish a fresh boundary
            // on the first update after activation: none of those presses is the new binding the player
            // was prompted to choose. Without this, the row immediately binds Return or an old arrow key.
            if (!ReferenceEquals(_armingBindingRow, row))
            {
                while (input.Keyboard.TryDequeuePress(out _))
                {
                }

                _armingBindingRow = row;
                // Quiet at arming means nothing is in motion, so the gate starts open; momentum
                // keeps it shut until a still frame (see _wheelGateOpen).
                _wheelGateOpen = input.Mouse.WheelDelta == 0f;
                return false;
            }

            while (input.Keyboard.TryDequeuePress(out KeyCode key))
            {
                if (row.TryCaptureKey(key))
                {
                    break;
                }
            }

            if (row.IsListening)
            {
                for (int i = 0; i < MouseState.ButtonCount; i++)
                {
                    MouseButton button = (MouseButton)i;
                    if (input.Mouse.WasPressed(button))
                    {
                        row.TryCaptureMouseButton(button);
                        break;
                    }
                }
            }

            // A scrolled wheel is a control like a pressed button; the scroll view stays still
            // because the listening branch returns before the base input scrolls it. Motion already
            // underway when the prompt armed waits for a quiet frame first (see _wheelGateOpen).
            if (row.IsListening)
            {
                if (input.Mouse.WheelDelta == 0f)
                {
                    _wheelGateOpen = true;
                }
                else if (_wheelGateOpen)
                {
                    row.TryCaptureMouseWheel(input.Mouse.WheelDelta);
                }
            }

            if (!row.IsListening)
            {
                _armingBindingRow = null;
            }

            return false;
        }

        _armingBindingRow = null;

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
    protected override void OnExit()
    {
        // The Controls tab mutates the live profile (rebinds, sensitivity, invert). Settings alone
        // only persist engine CVars, so the profile — including GOInRemap bindings — must be saved
        // here too, or every remap is silently lost on the next launch (issue #230).
        Context.Host.SaveSettings();
        Context.Host.SaveProfiles();
    }

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

    /// <summary>The graphics category: one page, the preset first and then a section per subject.</summary>
    /// <remarks>
    /// <para>
    /// The preset leads because it is the one control that answers the whole question at once. Under it,
    /// each section heading introduces a run of rows, every row a name for an idea rather than for a
    /// variable — see <see cref="GraphicsMenu"/>, which is where the rows and their words live.
    /// </para>
    /// <para>
    /// There is no second half any more. A row's tooltip still names the variables it drives, because
    /// that is the handle somebody needs to go further with it from the console, and the console is now
    /// the only advanced interface there is.
    /// </para>
    /// </remarks>
    private Panel BuildGraphicsPanel()
    {
        UiTheme theme = Context.Theme;

        var rows = new List<UiElement> { BuildPresetControl(theme) };

        foreach (GraphicsSection section in GraphicsMenu.Sections)
        {
            rows.Add(BuildSectionHeading(theme, section.Title));

            foreach (QualityGroup group in section.Groups)
            {
                if (QualityBinding.CreateControl(theme, Context.Cvars, group) is not { } control)
                {
                    continue;
                }

                // A group can write a variable that only takes effect on the next level load. The player
                // has to be told which, and the footer shows only the tooltip's first line, so the note
                // goes there rather than at the end.
                string restart = NamesRequiringRestart(group.Variables) ? "   [takes effect on the next level]" : string.Empty;
                control.Tooltip =
                    $"{group.Help}{restart}\n\nSets: {string.Join(", ", group.Variables)}\n\nReachable from the console by name.";
                rows.Add(control);
            }

            AddSettingRows(theme, rows, section.Settings);
        }

        return BuildColumns(theme, rows);
    }

    /// <summary>A section heading: a label, not a row, so the focus walk steps straight over it.</summary>
    private static Label BuildSectionHeading(UiTheme theme, string title) =>
        new(theme.Heading, title.ToUpperInvariant(), theme.BodyTextSize * 0.85f, theme.TextMuted)
        {
            Margin = new Thickness(0f, 14f, 0f, 2f),
        };

    /// <summary>Whether any of these variables only takes effect on the next level load.</summary>
    private bool NamesRequiringRestart(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (Context.Cvars.TryGet(name, out ICVar? cvar)
                && cvar is not null
                && cvar.Flags.HasFlag(CVarOptions.RequiresRestart))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The gameplay category, with the interface and effect options that are not quality levels.</summary>
    private Panel BuildGameplayPanel()
    {
        UiTheme theme = Context.Theme;
        var rows = new List<UiElement>();

        AddCvarRows(theme, rows, GameplayNames());

        rows.Add(BuildSectionHeading(theme, "Interface and Effects"));
        AddSettingRows(theme, rows, GraphicsMenu.InterfaceSettings);

        return BuildColumns(theme, rows);
    }

    /// <summary>A page of single-variable rows carrying captions and help written for a player.</summary>
    private Panel BuildSettingsPanel(IReadOnlyList<GraphicsSetting> settings)
    {
        UiTheme theme = Context.Theme;
        var rows = new List<UiElement>();
        AddSettingRows(theme, rows, settings);
        return BuildColumns(theme, rows);
    }

    /// <summary>Adds a row per setting, captioned and explained in the page's own words.</summary>
    /// <remarks>
    /// Deliberately NOT <see cref="AddCvarRows"/>, which captions from the variable name and explains from
    /// the variable's own help. That text is written for the console and says things like "windowed inverse
    /// square"; the whole point of <see cref="GraphicsSetting"/> is that a player reads something else. The
    /// variable name still ends the tooltip, as the handle for going further from the console.
    /// </remarks>
    private void AddSettingRows(UiTheme theme, List<UiElement> rows, IReadOnlyList<GraphicsSetting> settings)
    {
        foreach (GraphicsSetting setting in settings)
        {
            if (!Context.Cvars.TryGet(setting.Variable, out ICVar? cvar)
                || cvar is null
                || !CVarBinding.CanBind(cvar))
            {
                continue;
            }

            if (CVarBinding.CreateControl(theme, cvar, setting.Caption) is not { } control)
            {
                continue;
            }

            string restart = cvar.Flags.HasFlag(CVarOptions.RequiresRestart)
                ? "   [takes effect on the next level]"
                : string.Empty;

            control.Tooltip = $"{setting.Help}{restart}\n\n{setting.Variable}";
            rows.Add(control);
        }
    }

    private Panel BuildCvarPanel(IReadOnlyList<string> names, bool includePresets)
    {
        UiTheme theme = Context.Theme;
        var rows = new List<UiElement>();

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

    private void AddCvarRows(UiTheme theme, List<UiElement> rows, IReadOnlyList<string> names)
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
    /// <param name="rows">The elements to deal. Headings are elements rather than controls, so the focus
    /// walk registered here steps over them.</param>
    private Panel BuildColumns(UiTheme theme, List<UiElement> rows)
    {
        var focusable = new List<Control>(rows.Count);
        foreach (UiElement row in rows)
        {
            if (row is Control control)
            {
                focusable.Add(control);
            }
        }

        _tabRows.Add(focusable);

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
        if (profile is null)
        {
            _tabRows.Add([]); // kept parallel to _tabPanels even when a category has no rows at all
            ScrollView empty = BuildScrollHost(theme);
            empty.SetContent(
                new Label(theme.Body, "No active profile.", theme.BodyTextSize, theme.TextMuted));
            return new Panel().WithChild(empty);
        }

        var rows = new List<UiElement>(profile.Bindings.Count + 3);

        var resetBindings = new Button(theme, "RESET BINDINGS TO DEFAULTS")
        {
            Padding = new Thickness(18f, 8f),
            Tooltip = "Restore the standard WASD keyboard and mouse assignments.",
        };
        rows.Add(resetBindings);

        // The mouse comes first, above the bindings: it is the setting a player reaches for on the way
        // into their first mission, and unlike a binding it has no default anyone is happy with.
        var sensitivity = new Slider(theme, "Mouse Sensitivity", 0f, 1f, profile.MouseSensitivity, 0.05f)
        {
            // The slider position means nothing on its own. What a player is actually choosing is how far
            // the mouse travels for a full turn, so that is what the readout says — in centimetres at a
            // stated DPI, which is the number every sensitivity guide and every friend's config is in.
            Format = v => string.Create(
                CultureInfo.InvariantCulture,
                $"{MouseLookSettings.FromSensitivity(v, invertPitch: false).TurnDistanceCentimetres():0} cm/360° @ 800 DPI"),
            Tooltip = "How far the mouse turns the view.\n\nThe readout is how much desk a full turn "
                + "takes on an 800 DPI mouse — halve it for 1600 DPI. Lower is slower and more precise.",
        };

        sensitivity.ValueChanged += value => profile.MouseSensitivity = value;
        rows.Add(sensitivity);

        var invert = new Toggle(theme, "Invert Mouse", profile.InvertMouse)
        {
            Tooltip = "Pushing the mouse forward looks down instead of up.",
        };

        invert.ValueChanged += value => profile.InvertMouse = value;
        rows.Add(invert);

        var bindingRows = new List<KeyBindingRow>(profile.Bindings.Count);
        for (int i = 0; i < profile.Bindings.Count; i++)
        {
            int index = i;
            var binding = profile.Bindings[index];
            KeyCode current = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed) ? parsed : KeyCode.None;
            var row = new KeyBindingRow(theme, binding.Action, current, InputBindingKeyNames.DisplayName(binding))
            {
                Tooltip = $"Press this row, then a key, mouse button or wheel scroll, to rebind {binding.Action}.",
            };

            row.BindingChanged += key =>
            {
                int conflict = profile.RebindKeyboard(index, key);
                RefreshBindingRow(bindingRows[index], profile.Bindings[index]);
                if (conflict >= 0)
                {
                    RefreshBindingRow(bindingRows[conflict], profile.Bindings[conflict]);
                }
            };
            row.MouseBindingChanged += button =>
            {
                int conflict = profile.RebindMouseButton(index, button);
                RefreshBindingRow(bindingRows[index], profile.Bindings[index]);
                if (conflict >= 0)
                {
                    RefreshBindingRow(bindingRows[conflict], profile.Bindings[conflict]);
                }
            };
            row.MouseWheelBindingChanged += direction =>
            {
                int conflict = profile.RebindMouseWheel(index, direction);
                RefreshBindingRow(bindingRows[index], profile.Bindings[index]);
                if (conflict >= 0)
                {
                    RefreshBindingRow(bindingRows[conflict], profile.Bindings[conflict]);
                }
            };
            bindingRows.Add(row);
            rows.Add(row);
        }

        resetBindings.Activated += _ =>
        {
            profile.ResetBindingsToDefaults();
            for (int i = 0; i < bindingRows.Count; i++)
            {
                RefreshBindingRow(bindingRows[i], profile.Bindings[i]);
            }
        };

        return BuildColumns(theme, rows);
    }

    private static void RefreshBindingRow(KeyBindingRow row, InputBinding binding)
    {
        KeyCode key = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed)
            ? parsed
            : KeyCode.None;
        row.SetBinding(key, InputBindingKeyNames.DisplayName(binding));
    }

    /// <summary>The viewport a category's rows scroll inside: the full page, top to bottom and edge to edge.</summary>
    private static ScrollView BuildScrollHost(UiTheme theme) => new(theme);

    /// <summary>The first line of a tooltip, which is the sentence that says what the row is.</summary>
    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    private List<string> NamesWithPrefix(string prefix)
    {
        var names = new List<string>();
        foreach (ICVar cvar in Context.Cvars.All)
        {
            if (cvar.Name.StartsWith(prefix, StringComparison.Ordinal)
                && !cvar.Flags.HasFlag(CVarOptions.Hidden))
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
