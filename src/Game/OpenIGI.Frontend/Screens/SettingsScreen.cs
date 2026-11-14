using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The settings section: a segmented category bar, and under it the category's rows grouped into glass
/// panels across three columns — every row a CVar bound to its control, with a live preview, per-row help,
/// restart notes, and a graphics-preset selector.
/// </summary>
/// <remarks>
/// <para>
/// Categories are grouped by CVar name prefix — <c>r_</c> is graphics/display, <c>snd_</c> audio,
/// <c>g_</c>/<c>ui_</c> gameplay — and, inside a category, by the next word of the name, so
/// <c>g_ragdoll_force</c> and <c>g_ragdoll_gravity</c> share a Ragdoll panel. Controls are the
/// <see cref="CVarBinding"/> rows. On Audio and Gameplay a change previews the instant it is made; on
/// Graphics and Display it waits in a <see cref="SettingsDraft"/> until Apply (F), as every game's options
/// do -- a graphics API or a resolution is not something to change on the way past it. A display mode
/// that was applied has to be kept within <see cref="SettingsApplyFlow.KeepSeconds"/> or it is put back, and leaving with
/// changes unapplied asks what to do with them.
/// </para>
/// <para>
/// The Controls tab rebinds keyboard keys and mouse buttons against the active profile and persists their
/// retail input symbols, so the same assignments the player sees are the ones gameplay resolves. R resets
/// the category on screen to its defaults.
/// </para>
/// </remarks>
public sealed class SettingsScreen : ShellScreen
{
    /// <summary>The most columns the panels are ever dealt into, however wide the display is.</summary>
    private const int MaxColumns = 2;

    /// <summary>The design-unit width a column wants before another one is worth having.</summary>
    private const float ColumnWidth = 480f;

    /// <summary>The most rows one panel holds before a long group continues in another.</summary>
    private const int MaxRowsPerPanel = 12;

    private readonly Dictionary<Control, ICVar> _boundCvars = [];
    private readonly List<UiElement> _tabPanels = [];
    private readonly List<List<ICVar>> _tabCvars = [];
    private KeyBindingRow? _armingBindingRow;
    private Action? _resetBindings;

    /// <summary>
    /// Whether a wheel scroll may bind right now. Scrolling the list to reach a row leaves inertial motion
    /// (trackpad momentum especially) that is still non-zero when the prompt arms; capturing it would rebind
    /// without intent. The gate opens on the first quiet frame after arming, so only motion that starts after
    /// the prompt does.
    /// </summary>
    private bool _wheelGateOpen = true;

    // Each category's focusable rows in reading order — down a column, then on to the top of the next. Up
    // and down walk THIS rather than the spatial traversal, because a control consumes left and right for
    // its own value, so nothing else could carry focus from one column to another. Parallel to _tabPanels.
    private readonly List<List<Control>> _tabRows = [];

    // What ties this page's rows to the variables and the preset registry they show. Those live for the
    // process, so a row they still pointed at kept the whole page alive: every build of the page -- each
    // visit, each resize -- used to stay subscribed for good. Disposed on every rebuild and on leaving.
    private readonly List<IDisposable> _bindings = [];

    private SettingsAbout? _about;

    // The row a compact page has lent its explanation to as a line under the caption, to take it back from.
    private Control? _hinted;
    // The gap between the groups and the panel that explains the row in focus, in design units.
    private const float AsideGap = 28f;

    private TabStrip? _tabs;
    private int _columns = MaxColumns;
    private int _controlsColumns = MaxColumns;
    private int _activeTab;

    /// <summary>Gets the category the page opens on, by its title, for a capture; the first when there is none of that name.</summary>
    public string? StartCategory { get; init; }
    private int _controlsTab = -1;

    // Which categories write to the draft rather than straight to their variables.
    private readonly HashSet<int> _stagedTabs = [];

    // What the categories' rows are built with: how they find their variables -- through the draft on a
    // staged category -- and where the variables they bind are kept.
    private readonly SettingsRowBuilder _rows;

    // The draft the staged categories write to, the Apply bar, and the questions over the page. Kept across
    // the page's rebuilds, so a half-made choice and a question on screen survive them.
    private readonly SettingsApplyFlow _flow;

    /// <summary>Initializes a new instance of the <see cref="SettingsScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public SettingsScreen(ShellContext context)
        : base(context)
    {
        _rows = new SettingsRowBuilder(context, _bindings, _boundCvars);
        _flow = new SettingsApplyFlow(context, () => Focus, RefreshHints, () => IsStagedTab);
    }

    /// <inheritdoc/>
    public override bool CapturesQueuedNavigation =>
        Focus?.Focused is KeyBindingRow { IsListening: true };

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Settings;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints =>
        _flow.Prompt is { } prompt ? [new("Enter", "Choose"), new("Esc", prompt.BackCaption)]
        : _activeTab == _controlsTab && _controlsTab >= 0 ? [new("↑ ↓", "Select"), new("Enter", "Rebind"), new("R", "Reset bindings"), new("Esc", "Back")]
        : [new("↑ ↓", "Select"), new("← →", "Adjust"), new("R", "Reset defaults"), new("Esc", "Back")];

    /// <summary>Gets a value indicating whether the category on screen holds its changes until Apply.</summary>
    private bool IsStagedTab => _stagedTabs.Contains(_activeTab);

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        ReleaseBindings();
        _boundCvars.Clear();
        _tabPanels.Clear();
        _tabRows.Clear();
        _tabCvars.Clear();
        _activeTab = 0;
        _controlsTab = -1;
        _resetBindings = null;
        _stagedTabs.Clear();
        SettingsDraft draft = _flow.EnsureDraft();

        UiTheme theme = Context.Theme;

        // How many columns the page will hold, in DESIGN units so it follows the display's shape rather
        // than its resolution: 16:9 takes the design's three, 4:3 two.
        // The explanation of the row in focus has a panel of its own beside the groups; a compact page has no room for
        // one, and the row in focus carries its explanation under its caption there.
        bool compact = Size == ShellSize.Compact;
        // The wide page alone has room for that panel: on a laptop the groups take the width, two columns of them.
        float aside = Size == ShellSize.Wide ? 440f : 0f;
        float across = ShellSizes.Canvas(context).X - (SideMargin * 2f) - (compact ? RailWidth : 0f);
        float usable = across - (aside > 0f ? aside + AsideGap : 0f);
        _columns = compact ? 1 : Math.Clamp((int)(usable / ColumnWidth), 1, MaxColumns);

        // The controls have no panel beside them -- a binding needs no explaining -- and so a third column.
        _controlsColumns = compact ? 1 : Math.Clamp((int)(across / ColumnWidth), 1, MaxColumns + 1);

        // Five categories, and a sixth only for the mods' own: the console reaches every variable by name, which is
        // what an advanced interface should be. A build locked to one preset has no Graphics category at all.
        List<(string Title, Func<List<SettingsGroup>> Build)> categories =
        [
            ("Display", () => DisplaySettingsTab.Build(_rows, theme)),
            ("Audio", () => CvarGroupBuilder.CvarGroups(_rows, theme, CvarGroupBuilder.NamesWithPrefix(Context.Cvars, "snd_"), "Audio")),
            ("Controls", () => ControlsSettingsTab.Build(Context, _rows, theme, out _resetBindings)),
            ("Gameplay", () => GameplaySettingsTab.Build(_rows, theme)),
        ];

        if (Context.Presets.LockedPreset is null)
        {
            categories.Insert(0, ("Graphics", () => GraphicsSettingsTab.Build(_rows, theme, draft)));
        }

        // The mods' own settings (mod_<id>_...), one group a mod, when a mod declares any.
        List<string> modSettings = CvarGroupBuilder.NamesWithPrefix(Context.Cvars, SettingsFile.ModVariablePrefix);
        if (modSettings.Count > 0)
        {
            categories.Add(("Mods", () => CvarGroupBuilder.CvarGroups(_rows, theme, modSettings, "Mods")));
        }

        string[] tabTitles = new string[categories.Count];
        for (int i = 0; i < categories.Count; i++)
        {
            tabTitles[i] = categories[i].Title;
        }

        var tabs = new TabStrip(theme, tabTitles)
        {
            HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            TabPadding = compact ? 6f : 18f,
            SegmentHeight = compact ? 36f : 42f,
            TextSize = compact ? 14f : 17f,
            Stretch = compact,
        };
        tabs.SelectionChanged += ShowTab;
        _tabs = tabs;
        int opened = StartCategory is { Length: > 0 } wanted
            ? Array.FindIndex(tabTitles, title => title.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            : -1;

        UiElement header;
        if (compact)
        {
            // The page's name and Reset on one line -- there is no R to press on a phone -- and the categories under it.
            var reset = new Button(theme, "Reset")
            {
                Accent = ButtonAccent.Text,
                LeadingGlyph = static (painter, centre, size, ink) => OpenIGI.UI.Kit.KitIcons.Draw(painter, OpenIGI.UI.Kit.KitIcon.History, centre, size, ink),
                GlyphSize = 16f,
                TextSize = 16f,
                Tracking = 0.12f,
                Padding = new Thickness(12f, 10f),
            };
            reset.Activated += _ => ResetCurrentTab();
            var top = new FlexPanel { Orientation = Orientation.Horizontal };
            top.Add(new SectionTitle(theme, string.Empty, "Settings") { TitleSize = 24f, VerticalAlignment = VerticalAlignment.Center });
            top.Add(new Panel(), grow: 1f);
            top.Add(reset);
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };
            stack.AddChild(top);
            stack.AddChild(tabs);
            header = stack;
        }
        else
        {
            var strip = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, VerticalAlignment = VerticalAlignment.Bottom };
            if (!Context.Host.TouchScreen)
            {
                strip.Add(new KeyCapLabel(theme, "[") { VerticalAlignment = VerticalAlignment.Center });
            }

            strip.Add(tabs);
            if (!Context.Host.TouchScreen)
            {
                strip.Add(new KeyCapLabel(theme, "]") { VerticalAlignment = VerticalAlignment.Center });
            }

            var row = new Grid { ColumnSpacing = 24f };
            row.ColumnDefinitions.Add(GridLength.Star);
            row.ColumnDefinitions.Add(GridLength.Auto);
            row.RowDefinitions.Add(GridLength.Auto);
            row.Add(new SectionTitle(theme, "Configuration", "Settings") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
            row.Add(strip, 0, 1);
            header = row;
        }

        var content = new Panel();
        for (int i = 0; i < categories.Count; i++)
        {
            // The variables this category's rows bind, which R puts back to their defaults.
            List<ICVar> tabCvars = [];
            _tabCvars.Add(tabCvars);
            _rows.TabCvars = tabCvars;
            if (categories[i].Title == "Controls")
            {
                _controlsTab = i;
            }

            // Graphics and Display bind to the draft, so their rows write nothing until Apply.
            bool staged = categories[i].Title is "Graphics" or "Display";
            if (staged)
            {
                _stagedTabs.Add(i);
            }

            _rows.Find = staged ? draft.Find : FindLive;
            UiElement panel = BuildColumns(theme, categories[i].Build(), i);
            panel.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            _tabPanels.Add(panel);
            content.AddChild(panel);
        }

        if (opened > 0)
        {
            tabs.SelectedIndex = opened;
        }

        // The groups, and beside them what the row in focus does.
        UiElement body = content;
        _about = null;
        _hinted = null;
        if (aside > 0f)
        {
            _about = new SettingsAbout(theme)
            {
                Width = aside,
                Margin = new Thickness(AsideGap, 0f, 0f, 0f),
                Visibility = _activeTab == _controlsTab ? Visibility.Collapsed : Visibility.Visible,
            };
            var split = new Grid();
            split.ColumnDefinitions.Add(GridLength.Star);
            split.ColumnDefinitions.Add(GridLength.Auto);
            split.RowDefinitions.Add(GridLength.Star);
            split.Add(content, 0, 0);
            split.Add(_about, 0, 1);
            body = split;
        }

        var layout = new Grid
        {
            RowSpacing = compact ? 8f : 24f,
            Padding = compact ? new Thickness(SideMargin, 6f, SideMargin, 0f) : new Thickness(SideMargin, 32f, SideMargin, 0f),
        };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto); // the title and the categories
        layout.RowDefinitions.Add(GridLength.Star); // the current category, filling the rest
        layout.RowDefinitions.Add(GridLength.Auto); // what is waiting to be applied, on a staged category
        layout.Add(header, 0, 0);
        layout.Add(body, 1, 0);
        layout.Add(_flow.BuildApplyBar(theme, compact), 2, 0);

        // Transparent to the pointer where no question is shown: the empty layer spans the page and would otherwise
        // answer every hit test, so the wheel never found the category's viewport under it.
        var promptLayer = new Panel { IsHitTestTransparent = true };
        var root = new Panel();
        root.AddChild(layout);
        root.AddChild(promptLayer);
        _flow.Attach(layout, promptLayer);
        return root;
    }

    /// <inheritdoc/>
    /// <remarks>The keys start on the category's first row, not on the strip of categories: the rows are what the page is for.</remarks>
    protected override Control? InitialFocus =>
        _activeTab >= 0 && _activeTab < _tabRows.Count && _tabRows[_activeTab].Find(static row => row is not (TextBox or Button)) is { } first ? first : base.InitialFocus;

    /// <inheritdoc/>
    protected override void OnBuilt() => _flow.FocusPrompt();

    /// <inheritdoc/>
    protected override bool HoldDeparture(Action leave) => _flow.HoldDeparture(leave);

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);

        // A question on screen takes every key: back answers it the cautious way, and the page's own keys
        // wait for it to be answered.
        if (_flow.Prompt is { } prompt)
        {
            if (nav.Back)
            {
                prompt.Back();
                return false;
            }

            return base.HandleInput(nav, input);
        }

        // A key-binding row that is listening swallows the keyboard until it captures a key.
        if (Focus?.Focused is KeyBindingRow { IsListening: true } row)
        {
            // Activation through Enter is itself queued as a key press, and navigation presses can have
            // been waiting in that queue since before the row was selected. Establish a fresh boundary on
            // the first update after activation: none of those presses is the binding the player was
            // prompted to choose.
            if (!ReferenceEquals(_armingBindingRow, row))
            {
                while (input.Keyboard.TryDequeuePress(out _))
                {
                    // The loop's condition does the work.
                }

                _armingBindingRow = row;

                // Quiet at arming means nothing is in motion, so the gate starts open; momentum keeps it
                // shut until a still frame (see _wheelGateOpen).
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

            // A scrolled wheel is a control like a pressed button; the scroll view stays still because the
            // listening branch returns before the base input scrolls it.
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

        if (input.Keyboard.WasPressed(KeyCode.R))
        {
            ResetCurrentTab();
        }

        // The keys either side of the categories step through them.
        if (_tabs is { } tabs && Focus?.Focused is not TextBox)
        {
            int step = input.Keyboard.WasPressed(KeyCode.RightBracket) ? 1 : input.Keyboard.WasPressed(KeyCode.LeftBracket) ? -1 : 0;
            if (step != 0)
            {
                tabs.SelectedIndex = (tabs.SelectedIndex + step + tabs.Tabs.Count) % tabs.Tabs.Count;
                return false;
            }
        }

        if (IsStagedTab && input.Keyboard.WasPressed(KeyCode.F))
        {
            _flow.Apply();
        }

        if (IsStagedTab && input.Keyboard.WasPressed(KeyCode.Backspace))
        {
            _flow.Discard();
        }

        return base.HandleInput(nav, input);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Up and down walk the category's rows in the order they were dealt into columns, so the foot of one
    /// column leads to the head of the next. At either end it falls through, so up from the first row still
    /// reaches the category bar.
    /// </remarks>
    protected override bool MoveFocus(NavDirection direction)
    {
        // A question holds focus among its own answers, which sit in a row.
        if (_flow.TryMoveFocus(direction))
        {
            return true;
        }

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
        // The Controls tab mutates the live profile (rebinds, sensitivity, invert). Settings alone only
        // persist engine CVars, so the profile must be saved here too, or every remap is lost (issue #230).
        Context.Host.SaveSettings();
        Context.Host.SaveProfiles();
        ReleaseBindings();

        // Whatever was not applied is dropped with the page; it never reached the game.
        _flow.Exit();
    }

    /// <summary>Unhooks every row from the variables it shows.</summary>
    private void ReleaseBindings()
    {
        foreach (IDisposable binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        _flow.Update(elapsedSeconds);

        Control? focused = Focus?.Focused as Control;
        (string title, string text, ICVar? variable) = focused is null ? (string.Empty, string.Empty, null) : Describe(focused);

        if (_about is not null)
        {
            (IReadOnlyList<string> options, int current) = Choices(focused);
            _about.Show(title, text, Tags(focused, variable), options, current);
            return;
        }

        // No panel: the row in focus says what it does under its own caption, and the one before gives the line back.
        if (!ReferenceEquals(_hinted, focused))
        {
            if (_hinted is not null)
            {
                _hinted.Hint = string.Empty;
                _hinted = null;
            }

            if (focused is { Hint.Length: 0 } && text.Length > 0 && focused is Toggle or Slider or OptionCycler or Dropdown)
            {
                focused.Hint = FirstLine(text);
                _hinted = focused;
            }
        }
    }

    // What a row is called and what it does: its variable's help where it is one variable, and otherwise what the
    // pointer would be told, less the variable's name at the foot of that.
    private (string Title, string Text, ICVar? Variable) Describe(Control control)
    {
        string title = control switch
        {
            Toggle toggle => toggle.Label,
            Slider slider => slider.Label,
            OptionCycler cycler => cycler.Caption,
            Dropdown dropdown => dropdown.Label,
            TabStrip strip when !ReferenceEquals(strip, _tabs) => "Quality preset",
            KeyBindingRow binding => binding.Action,
            ActionRow action => action.Caption,
            Button button => button.Text,
            _ => string.Empty,
        };

        _boundCvars.TryGetValue(control, out ICVar? cvar);
        string text = control.Tooltip.Length > 0 ? control.Tooltip : cvar?.Help ?? string.Empty;
        int foot = text.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (foot >= 0 && text.IndexOf(' ', foot + 2) < 0)
        {
            text = text[..foot];
        }

        if (control is KeyBindingRow && text.Length == 0)
        {
            text = "Select the row, then press the new key, mouse button or wheel direction. Escape cancels.";
        }

        return (title, text.Trim(), cvar);
    }

    // What a row chooses between, and which it holds: for the panel beside the rows to list. Nothing for a row that
    // chooses between nothing, or between more than a panel should list.
    private static (IReadOnlyList<string> Options, int Current) Choices(Control? control) => control switch
    {
        OptionCycler { Options.Count: > 1 and <= 8 } cycler => (cycler.Options, cycler.SelectedIndex),
        Dropdown { Options.Count: > 1 and <= 8 } dropdown => (dropdown.Options, dropdown.SelectedIndex),
        TabStrip { Tabs.Count: > 1 and <= 8 } strip when !ReferenceEquals(strip, null) => (strip.Tabs, strip.SelectedIndex),
        _ => ([], -1),
    };

    // When a change to the row takes hold, and what the setting started as.
    private List<(string Text, OpenIGI.Core.Graphics.Bgra32 Ink)> Tags(Control? control, ICVar? variable)
    {
        List<(string, OpenIGI.Core.Graphics.Bgra32)> tags = [];
        if (control is null || control is Button)
        {
            return tags;
        }

        UiTheme theme = Context.Theme;
        if (!control.IsEnabled)
        {
            tags.Add(("UNAVAILABLE", theme.Warning));
        }
        else if (variable is not null && variable.Flags.HasFlag(CVarOptions.RequiresRestart))
        {
            tags.Add(("FROM THE NEXT LEVEL", theme.Warning));
        }
        else if (IsStagedTab)
        {
            tags.Add(("NEEDS APPLY", theme.Accent));
        }
        else
        {
            tags.Add(("APPLIES INSTANTLY", theme.Success));
        }

        if (variable is { DefaultText.Length: > 0 and < 24 })
        {
            tags.Add(($"DEFAULT: {variable.DefaultText.ToUpperInvariant()}", theme.TextSecondary));
        }

        return tags;
    }

    private void ShowTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _tabPanels.Count; i++)
        {
            _tabPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        // The panel that explains a row stands down for the controls, whose rows take its room.
        if (_about is not null)
        {
            _about.Visibility = index == _controlsTab ? Visibility.Collapsed : Visibility.Visible;
        }

        // The focused row belongs to the category that has just been collapsed, so it can no longer hold
        // focus; taking the first row of the new panel is what keeps the keyboard working.
        Focus?.Validate();
        Focus?.FocusFirst();
        _flow.UpdateApplyBar();
        RefreshHints();
    }

    /// <summary>Puts every variable on the current category back to its default.</summary>
    private void ResetCurrentTab()
    {
        if (_activeTab == _controlsTab)
        {
            _resetBindings?.Invoke();
            return;
        }

        if (_activeTab < 0 || _activeTab >= _tabCvars.Count)
        {
            return;
        }

        foreach (ICVar cvar in _tabCvars[_activeTab])
        {
            if (!cvar.IsDefault)
            {
                cvar.TrySetText(cvar.DefaultText, out _);
            }
        }
    }

    /// <summary>Deals a category's groups into columns of glass panels and returns the scrolling page holding them.</summary>
    /// <remarks>
    /// A group longer than a panel should be continues in the next one under the same heading, and each
    /// panel goes to whichever column is shortest so far, so the columns end near the same height. The focus
    /// walk registered here runs down each column and then on to the next.
    /// </remarks>
    private ScrollView BuildColumns(UiTheme theme, List<SettingsGroup> groups, int tab)
    {
        var panels = new List<SettingsGroup>();
        var bands = new List<SettingsGroup>();
        foreach (SettingsGroup group in groups)
        {
            if (group.Banner is not null)
            {
                bands.Add(group);
                continue;
            }

            for (int start = 0; start < group.Rows.Count; start += MaxRowsPerPanel)
            {
                panels.Add(new SettingsGroup(group.Title, group.Rows.GetRange(start, Math.Min(MaxRowsPerPanel, group.Rows.Count - start))));
            }
        }

        int most = tab == _controlsTab ? _controlsColumns : _columns;
        int columns = Math.Clamp(Math.Min(most, Math.Max(1, panels.Count)), 1, MaxColumns + 1);
        var stacks = new StackPanel[columns];
        var heights = new int[columns];
        var columnRows = new List<Control>[columns];
        bool compact = Size == ShellSize.Compact;
        var grid = new Grid { ColumnSpacing = 20f, Margin = new Thickness(0f, 0f, compact ? 10f : 18f, 16f), VerticalAlignment = VerticalAlignment.Top };
        grid.RowDefinitions.Add(GridLength.Auto);
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(GridLength.Star);
            stacks[c] = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 10f : 20f, VerticalAlignment = VerticalAlignment.Top };
            columnRows[c] = [];
            grid.Add(stacks[c], 0, c);
        }

        foreach (SettingsGroup panel in panels)
        {
            int shortest = 0;
            for (int c = 1; c < columns; c++)
            {
                if (heights[c] < heights[shortest])
                {
                    shortest = c;
                }
            }

            stacks[shortest].AddChild(Panel(theme, panel, compact));
            heights[shortest] += panel.Rows.Count + 2;
            columnRows[shortest].AddRange(panel.Rows);
        }

        var focusable = new List<Control>();
        foreach (SettingsGroup band in bands)
        {
            focusable.AddRange(band.Rows);
        }

        foreach (List<Control> rows in columnRows)
        {
            focusable.AddRange(rows);
        }

        while (_tabRows.Count <= tab)
        {
            _tabRows.Add([]);
        }

        _tabRows[tab] = focusable;

        var scroll = new ScrollView(theme);
        if (panels.Count == 0 && bands.Count == 0)
        {
            scroll.SetContent(new Label(theme.Body, "No active profile.", 17f, theme.TextMuted));
            return scroll;
        }

        if (bands.Count == 0)
        {
            scroll.SetContent(grid);
            return scroll;
        }

        // The bands across the top, the columns under them.
        var page = new StackPanel { Orientation = Orientation.Vertical, Spacing = compact ? 10f : 20f, VerticalAlignment = VerticalAlignment.Top };
        foreach (SettingsGroup band in bands)
        {
            band.Banner!.Margin = new Thickness(0f, 0f, compact ? 10f : 18f, 0f);
            page.AddChild(band.Banner);
        }

        page.AddChild(grid);
        scroll.SetContent(page);
        return scroll;
    }

    private static GlassPanel Panel(UiTheme theme, SettingsGroup group, bool compact)
    {
        // The group's name over its rows; the rows run to the panel's edges but for a margin the focus ring stands in.
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.AddChild(new GroupHeader(theme, group.Title)
        {
            Margin = compact ? new Thickness(16f, 12f, 16f, 4f) : new Thickness(20f, 28f, 20f, 14f),
        });
        foreach (Control row in group.Rows)
        {
            stack.AddChild(row);
        }

        var panel = new GlassPanel(theme)
        {
            Tint = OpenIGI.Core.Graphics.Bgra32.FromRgba(12, 14, 15, compact ? (byte)235 : (byte)199),
            Border = UiPalette.White(0.12f),
            Padding = compact ? new Thickness(0f, 0f, 0f, 4f) : new Thickness(8f, 0f, 8f, 16f),
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.AddChild(stack);
        return panel;
    }

    private ICVar? FindLive(string name) => Context.Cvars.TryGet(name, out ICVar? cvar) ? cvar : null;

    /// <summary>The first line of a tooltip, which is the sentence that says what the row is.</summary>
    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }
}
