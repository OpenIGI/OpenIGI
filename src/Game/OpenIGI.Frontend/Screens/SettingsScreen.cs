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
    private const int MaxColumns = 3;

    /// <summary>The design-unit width a column wants before another one is worth having.</summary>
    private const float ColumnWidth = 520f;

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

    private Label? _help;
    private int _columns = MaxColumns;
    private int _activeTab;
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
        _flow.Prompt is { } prompt ? [new("ENTER", "Choose"), new("ESC", prompt.BackCaption)]
        : IsStagedTab && _flow.HasChanges ? [new("CLICK", "Adjust"), new("F", "Apply"), new("BACKSPACE", "Discard"), new("R", "Reset defaults"), new("ESC", "Back")]
        : [new("CLICK", "Adjust"), new("R", "Reset defaults"), new("ESC", "Back")];

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
        float usable = (context.SafeRect.Width / context.Scale) - (SideMargin * 2f);
        _columns = Math.Clamp((int)(usable / ColumnWidth), 1, MaxColumns);

        // Five categories, and a sixth only for the mods' own: the console reaches every variable by name, which is
        // what an advanced interface should be. A build locked to one preset has no Graphics category at all.
        List<(string Title, Func<List<SettingsGroup>> Build)> categories =
        [
            ("Display", () => [SettingsGroup.Of("Display", DisplaySettingsTab.Build(_rows, theme))]),
            ("Audio", () => CvarGroupBuilder.CvarGroups(_rows, theme, CvarGroupBuilder.NamesWithPrefix(Context.Cvars, "snd_"), "Audio")),
            ("Controls", () => ControlsSettingsTab.Build(Context, theme, out _resetBindings)),
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

        var tabs = new TabStrip(theme, tabTitles) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        tabs.SelectionChanged += ShowTab;

        var header = new Grid { ColumnSpacing = 24f };
        header.ColumnDefinitions.Add(GridLength.Star);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.RowDefinitions.Add(GridLength.Auto);
        header.Add(new SectionTitle(theme, "Configuration", "Settings") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        header.Add(tabs, 0, 1);

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

        // Two lines, wrapped, and always two: it shares its row with the Apply bar now, and a row whose
        // height followed the focused setting's help would nudge the whole page up and down.
        _help = new Label(theme.Body, string.Empty, 16f, theme.TextMuted)
        {
            Margin = new Thickness(0f, 12f, 0f, 0f),
            Wrap = true,
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Clear of the footer's rule, which the Apply bar's buttons otherwise sit on.
        var footer = new Grid { ColumnSpacing = 24f, Margin = new Thickness(0f, 0f, 0f, 20f) };
        footer.ColumnDefinitions.Add(GridLength.Star);
        footer.ColumnDefinitions.Add(GridLength.Auto);
        footer.RowDefinitions.Add(GridLength.Auto);
        footer.Add(_help, 0, 0);
        footer.Add(_flow.BuildApplyBar(theme), 0, 1);

        var layout = new Grid { RowSpacing = 36f, Padding = new Thickness(SideMargin, 48f, SideMargin, 0f) };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto); // the title and the categories
        layout.RowDefinitions.Add(GridLength.Star); // the current category, filling the rest
        layout.RowDefinitions.Add(GridLength.Auto); // the per-row help line, and Apply on a staged category
        layout.Add(header, 0, 0);
        layout.Add(content, 1, 0);
        layout.Add(footer, 2, 0);
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
            // pointer would be told, cut to the line's length. The rest is in the tooltip.
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
        foreach (SettingsGroup group in groups)
        {
            for (int start = 0; start < group.Rows.Count; start += MaxRowsPerPanel)
            {
                panels.Add(new SettingsGroup(group.Title, group.Rows.GetRange(start, Math.Min(MaxRowsPerPanel, group.Rows.Count - start))));
            }
        }

        int columns = Math.Clamp(Math.Min(_columns, panels.Count), 1, MaxColumns);
        var stacks = new StackPanel[columns];
        var heights = new int[columns];
        var columnRows = new List<Control>[columns];
        var grid = new Grid { ColumnSpacing = 24f, Margin = new Thickness(0f, 0f, 22f, 0f), VerticalAlignment = VerticalAlignment.Top };
        grid.RowDefinitions.Add(GridLength.Auto);
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(GridLength.Star);
            stacks[c] = new StackPanel { Orientation = Orientation.Vertical, Spacing = 24f, VerticalAlignment = VerticalAlignment.Top };
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

            stacks[shortest].AddChild(Panel(theme, panel));
            heights[shortest] += panel.Rows.Count + 2;
            columnRows[shortest].AddRange(panel.Rows);
        }

        var focusable = new List<Control>();
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
        if (panels.Count == 0)
        {
            scroll.SetContent(new Label(theme.Body, "No active profile.", 17f, theme.TextMuted));
            return scroll;
        }

        scroll.SetContent(grid);
        return scroll;
    }

    private static GlassPanel Panel(UiTheme theme, SettingsGroup group)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.AddChild(new GroupHeader(theme, group.Title));
        foreach (Control row in group.Rows)
        {
            // Widened into the panel's padding, so the hover wash reaches the panel's edges as the design's does.
            row.Margin = new Thickness(-10f, 0f);
            stack.AddChild(row);
        }

        var panel = new GlassPanel(theme) { Padding = new Thickness(28f, 28f, 28f, 20f), VerticalAlignment = VerticalAlignment.Top };
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
