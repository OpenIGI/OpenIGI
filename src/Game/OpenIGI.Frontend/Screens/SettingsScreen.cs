using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Rendering.Presets;
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
/// that was applied has to be kept within <see cref="KeepSeconds"/> or it is put back, and leaving with
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

    /// <summary>How long a newly applied display mode waits to be kept before it is put back.</summary>
    private const float KeepSeconds = 15f;

    /// <summary>The variables a change of which can leave the player with no picture they can use, and so has to be kept.</summary>
    private static readonly string[] DisplayModeVariables = ["r_window_mode", "r_resolution"];

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

    // The Graphics and Display categories' unapplied changes. Made with the page and kept across its
    // rebuilds -- a resize, a resolution just applied -- so a half-made choice survives them; dropped on leaving.
    private SettingsDraft? _draft;

    // Which categories write to the draft rather than straight to their variables.
    private readonly HashSet<int> _stagedTabs = [];

    // How the rows being built find their variables: through the draft on a staged category.
    private Func<string, ICVar?> _find = static _ => null;

    private UiElement? _page;
    private UiElement? _applyBar;
    private Label? _applyNote;
    private Button? _apply;
    private Button? _discard;

    // The question on screen, if any. Kept as data, so it survives the page being rebuilt under it -- which
    // is exactly what applying a resolution does.
    private Prompt? _prompt;
    private Panel? _promptLayer;
    private Label? _promptMessage;
    private readonly List<Button> _promptButtons = [];

    /// <summary>Initializes a new instance of the <see cref="SettingsScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public SettingsScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    public override bool CapturesQueuedNavigation =>
        Focus?.Focused is KeyBindingRow { IsListening: true };

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Settings;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints =>
        _prompt is { } prompt ? [new("ENTER", "Choose"), new("ESC", prompt.BackCaption)]
        : IsStagedTab && _draft?.HasChanges == true ? [new("CLICK", "Adjust"), new("F", "Apply"), new("BACKSPACE", "Discard"), new("R", "Reset defaults"), new("ESC", "Back")]
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
        _draft ??= CreateDraft();

        UiTheme theme = Context.Theme;

        // How many columns the page will hold, in DESIGN units so it follows the display's shape rather
        // than its resolution: 16:9 takes the design's three, 4:3 two.
        float usable = (context.SafeRect.Width / context.Scale) - (SideMargin * 2f);
        _columns = Math.Clamp((int)(usable / ColumnWidth), 1, MaxColumns);

        // Five categories and no sixth: the console reaches every variable by name, which is what an
        // advanced interface should be. A build locked to one preset has no Graphics category at all.
        List<(string Title, Func<List<SettingsGroup>> Build)> categories =
        [
            ("Display", () => [SettingsGroup.Of("Display", BuildDisplay(theme))]),
            ("Audio", () => CvarGroups(theme, NamesWithPrefix("snd_"), "Audio")),
            ("Controls", () => BuildControls(theme)),
            ("Gameplay", () => BuildGameplay(theme)),
        ];

        if (Context.Presets.LockedPreset is null)
        {
            categories.Insert(0, ("Graphics", () => BuildGraphics(theme)));
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
            _tabCvars.Add([]);
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

            _find = staged ? _draft.Find : FindLive;
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
        footer.Add(BuildApplyBar(theme), 0, 1);

        var layout = new Grid { RowSpacing = 36f, Padding = new Thickness(SideMargin, 48f, SideMargin, 0f) };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto); // the title and the categories
        layout.RowDefinitions.Add(GridLength.Star); // the current category, filling the rest
        layout.RowDefinitions.Add(GridLength.Auto); // the per-row help line, and Apply on a staged category
        layout.Add(header, 0, 0);
        layout.Add(content, 1, 0);
        layout.Add(footer, 2, 0);
        _page = layout;

        _promptLayer = new Panel();
        var root = new Panel();
        root.AddChild(layout);
        root.AddChild(_promptLayer);
        UpdateApplyBar();
        ShowPrompt(_prompt);
        return root;
    }

    /// <inheritdoc/>
    protected override void OnBuilt()
    {
        // A rebuild makes a new focus manager, which starts on the page's first row: a question still on
        // screen has to keep it.
        if (_prompt is not null && _promptButtons.Count > 0)
        {
            Focus?.Focus(_promptButtons[0]);
        }
    }

    /// <inheritdoc/>
    protected override bool HoldDeparture(Action leave)
    {
        switch (_prompt)
        {
            case { Kind: PromptKind.Keep } keep:
                // Leaving is as good an answer as any that the new display mode works.
                keep.Choices[0].Chosen();
                return false;

            case not null:
                return true;
        }

        if (_draft is not { HasChanges: true })
        {
            return false;
        }

        ShowPrompt(new Prompt(
            PromptKind.Leave,
            "Unapplied changes",
            "Apply your changes?",
            _ => "Graphics or display settings were changed and not applied.",
            "Cancel",
            [
                new("Apply", ButtonAccent.Solid, () => Apply(then: leave)),
                new("Discard", ButtonAccent.Ghost, () =>
                {
                    ShowPrompt(null);
                    _draft?.Discard();
                    leave();
                }),
                new("Cancel", ButtonAccent.Ghost, () => ShowPrompt(null)),
            ]));
        return true;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);

        // A question on screen takes every key: back answers it the cautious way, and the page's own keys
        // wait for it to be answered.
        if (_prompt is { } prompt)
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
            Apply();
        }

        if (IsStagedTab && input.Keyboard.WasPressed(KeyCode.Backspace))
        {
            _draft?.Discard();
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
        if (_prompt is not null && _promptButtons.Count > 0)
        {
            int at = Focus?.Focused is Button focusedButton ? _promptButtons.IndexOf(focusedButton) : -1;
            int step = direction is NavDirection.Left or NavDirection.Up ? -1 : 1;
            Focus?.Focus(_promptButtons[at < 0 ? 0 : (at + step + _promptButtons.Count) % _promptButtons.Count]);
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
        _draft?.Dispose();
        _draft = null;
        _prompt = null;
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
        if (_prompt is { } prompt)
        {
            prompt.Elapsed += elapsedSeconds;
            if (_promptMessage is not null)
            {
                _promptMessage.Text = prompt.Message(prompt.Elapsed);
            }

            if (prompt.Expires is { } expires && prompt.Elapsed >= expires)
            {
                prompt.Back();
            }
        }

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
        UpdateApplyBar();
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

    /// <summary>The graphics category: the preset first, then a group per subject.</summary>
    /// <remarks>
    /// A row's tooltip still names the variables it drives, because that is the handle somebody needs to go
    /// further with it from the console, and the console is the only advanced interface there is.
    /// </remarks>
    private List<SettingsGroup> BuildGraphics(UiTheme theme)
    {
        List<SettingsGroup> groups = [SettingsGroup.Of("General", [BuildPresetControl(theme)])];

        foreach (GraphicsSection section in GraphicsMenu.Sections)
        {
            var rows = new List<Control>();
            foreach (QualityGroup group in section.Groups)
            {
                if (QualityBinding.CreateControl(theme, _find, group, _bindings) is not { } control)
                {
                    continue;
                }

                // A group can write a variable that only takes effect on the next level load. The player has
                // to be told which, and the help line shows only the tooltip's first line, so the note goes
                // there rather than at the end.
                string restart = NamesRequiringRestart(group.Variables) ? "   [takes effect on the next level]" : string.Empty;
                string unavailable = Unavailable(group.Variables);
                control.IsEnabled = unavailable.Length == 0;
                control.Tooltip =
                    $"{unavailable}{group.Help}{restart}\n\nSets: {string.Join(", ", group.Variables)}\n\nReachable from the console by name.";
                rows.Add(control);
                TrackForReset(group.Variables);
            }

            rows.AddRange(SettingRows(theme, section.Settings));
            groups.Add(SettingsGroup.Of(section.Title, rows));
        }

        return groups;
    }

    /// <summary>The gameplay category, with the interface and effect options that are not quality levels.</summary>
    private List<SettingsGroup> BuildGameplay(UiTheme theme)
    {
        List<SettingsGroup> groups = CvarGroups(theme, GameplayNames(), "General");
        groups.Add(SettingsGroup.Of("Interface and effects", SettingRows(theme, GraphicsMenu.InterfaceSettings)));
        return groups;
    }

    /// <summary>Why the first of these variables that cannot take effect here cannot, as a tooltip lead.</summary>
    /// <param name="names">The variables a row drives.</param>
    /// <returns>"Unavailable: reason." and a blank line, or empty when every one of them works here.</returns>
    private string Unavailable(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            string reason = Context.Host.SettingUnavailable(name);
            if (reason.Length > 0)
            {
                return $"Unavailable: {reason}.\n\n";
            }
        }

        return string.Empty;
    }

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

    /// <summary>A row per setting, captioned and explained in the page's own words.</summary>
    /// <remarks>
    /// Deliberately not <see cref="CvarRows"/>, which captions from the variable name and explains from the
    /// variable's own help — text written for the console. The variable name still ends the tooltip, as the
    /// handle for going further from the console.
    /// </remarks>
    private List<Control> SettingRows(UiTheme theme, IReadOnlyList<GraphicsSetting> settings)
    {
        var rows = new List<Control>();
        foreach (GraphicsSetting setting in settings)
        {
            if (_find(setting.Variable) is not { } cvar || !CVarBinding.CanBind(cvar))
            {
                continue;
            }

            if (CVarBinding.CreateControl(theme, cvar, setting.Caption, _bindings, setting.Choices) is not { } control)
            {
                continue;
            }

            DescribeRow(control, cvar, setting);
            rows.Add(control);
            CurrentTabCvars.Add(cvar);
        }

        return rows;
    }

    /// <summary>Greys a row out where its setting cannot take effect, and gives it its help.</summary>
    private void DescribeRow(Control control, ICVar cvar, GraphicsSetting setting)
    {
        string restart = cvar.Flags.HasFlag(CVarOptions.RequiresRestart)
            ? "   [takes effect on the next level]"
            : string.Empty;

        string unavailable = Unavailable([setting.Variable]);
        control.IsEnabled = unavailable.Length == 0;
        control.Tooltip = $"{unavailable}{setting.Help}{restart}\n\n{setting.Variable}";
    }

    /// <summary>The Display page's rows, in <see cref="GraphicsMenu.DisplaySettings"/>'s order.</summary>
    /// <remarks>
    /// Two of them are lists this page builds, because their variables' types do not spell the choices out:
    /// the resolutions the display offers, and the frame-rate caps.
    /// </remarks>
    private List<Control> BuildDisplay(UiTheme theme)
    {
        var rows = new List<Control>();
        foreach (GraphicsSetting setting in GraphicsMenu.DisplaySettings)
        {
            switch (setting.Variable)
            {
                case "r_resolution":
                    if (ChoiceRow(theme, setting, ResolutionChoices()) is ({ } resolution, { } cvar))
                    {
                        rows.Add(resolution);

                        // Borderless has no resolution of its own to choose, so the row follows the window mode.
                        WhileShown("r_window_mode", () => DescribeRow(resolution, cvar, setting));
                    }

                    break;

                case "r_frame_limit":
                    if (ChoiceRow(theme, setting, FrameRateCapChoices()) is ({ } cap, _))
                    {
                        rows.Add(cap);
                    }

                    break;

                default:
                    rows.AddRange(SettingRows(theme, [setting]));
                    break;
            }
        }

        return rows;
    }

    /// <summary>A row choosing among a list of a variable's values, and the variable; nulls where it is not registered.</summary>
    private (Dropdown? Control, ICVar? Cvar) ChoiceRow(
        UiTheme theme,
        GraphicsSetting setting,
        IReadOnlyList<(string Value, string Caption)> choices)
    {
        if (_find(setting.Variable) is not { } cvar)
        {
            return (null, null);
        }

        Dropdown control = CVarBinding.CreateChoiceControl(theme, cvar, setting.Caption, choices, _bindings);
        DescribeRow(control, cvar, setting);
        CurrentTabCvars.Add(cvar);
        return (control, cvar);
    }

    /// <summary>The desktop's own resolution, then every one the display offers, largest first.</summary>
    private List<(string Value, string Caption)> ResolutionChoices()
    {
        List<(string Value, string Caption)> choices = [(string.Empty, "Desktop")];
        foreach ((int width, int height) in Context.Host.DisplayResolutions())
        {
            choices.Add((
                ResolutionText.Format(width, height),
                string.Create(CultureInfo.InvariantCulture, $"{width} × {height}")));
        }

        return choices;
    }

    /// <summary>No cap, then the caps <see cref="GraphicsMenu.FrameRateCaps"/> offers.</summary>
    private static List<(string Value, string Caption)> FrameRateCapChoices() =>
        [
            .. GraphicsMenu.FrameRateCaps.Select(static fps => fps <= 0
                ? ("0", "Unlimited")
                : (fps.ToString(CultureInfo.InvariantCulture), string.Create(CultureInfo.InvariantCulture, $"{fps} fps"))),
        ];

    /// <summary>Runs an action whenever a variable changes, for as long as this build of the page is shown.</summary>
    private void WhileShown(string variable, Action action)
    {
        if (!Context.Cvars.TryGet(variable, out ICVar? cvar) || cvar is null)
        {
            return;
        }

        EventHandler<CVarChangedEventArgs> changed = (_, _) => action();
        cvar.Changed += changed;
        _bindings.Add(new Unsubscribe(() => cvar.Changed -= changed));
    }

    /// <summary>The variables a list of names binds to, grouped by the word after their prefix.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="names">The variable names, in the order the rows are wanted.</param>
    /// <param name="fallback">The group a variable goes in when no other shares its word.</param>
    private List<SettingsGroup> CvarGroups(UiTheme theme, IReadOnlyList<string> names, string fallback)
    {
        var byWord = new Dictionary<string, List<Control>>(StringComparer.Ordinal);
        var order = new List<string>();
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            string word = GroupWord(name);
            words[word] = words.GetValueOrDefault(word) + 1;
        }

        foreach ((ICVar cvar, Control control) in CvarRows(theme, names))
        {
            string word = GroupWord(cvar.Name);
            string title = words.GetValueOrDefault(word) >= 2 ? Humanize(word) : fallback;
            if (!byWord.TryGetValue(title, out List<Control>? rows))
            {
                rows = [];
                byWord[title] = rows;
                order.Add(title);
            }

            rows.Add(control);
        }

        // The catch-all leads, as General does in the design; the named groups follow in the order they met.
        order.Sort((a, b) => (a == fallback ? 0 : 1).CompareTo(b == fallback ? 0 : 1));
        var groups = new List<SettingsGroup>(order.Count);
        foreach (string title in order)
        {
            groups.Add(SettingsGroup.Of(title, byWord[title]));
        }

        return groups;
    }

    private List<(ICVar Cvar, Control Control)> CvarRows(UiTheme theme, IReadOnlyList<string> names)
    {
        var rows = new List<(ICVar, Control)>();
        foreach (string name in names)
        {
            if (Context.Cvars.TryGet(name, out ICVar? cvar) && cvar is not null && CVarBinding.CanBind(cvar)
                && CVarBinding.CreateControl(theme, cvar, lifetime: _bindings) is { } control)
            {
                // The variable already carries the sentence that explains it — the same one the console
                // prints — so the tooltip is the help text the CVar was declared with.
                control.Tooltip = cvar.Flags.HasFlag(CVarOptions.RequiresRestart)
                    ? $"{cvar.Help}\n\nTakes effect after a restart.\n\n{name}"
                    : $"{cvar.Help}\n\n{name}";

                _boundCvars[control] = cvar;
                CurrentTabCvars.Add(cvar);
                rows.Add((cvar, control));
            }
        }

        return rows;
    }

    /// <summary>The preset row: stages a whole preset in the draft, and reads which one the draft amounts to.</summary>
    private Dropdown BuildPresetControl(UiTheme theme)
    {
        SettingsDraft draft = _draft!;
        string[] presetNames = Enum.GetNames<GraphicsPreset>();
        int Shown() => Math.Max(0, Array.IndexOf(presetNames, Context.Presets.Recognise(draft.Holds).ToString()));

        var presets = new Dropdown(theme, "Preset", presetNames, Shown())
        {
            Tooltip = "Sets every graphics variable at once, when you apply. Custom is what the settings become "
                + "as soon as you change any of them yourself — it is a reading, not a choice.",
        };

        // Choosing a preset stages its values, each of which the draft reports; the row reads the preset back
        // once, at the end, rather than at every one of them.
        bool updating = false;
        void ShowPreset()
        {
            updating = true;
            try
            {
                presets.SelectedIndex = Shown();
            }
            finally
            {
                updating = false;
            }
        }

        presets.SelectionChanged += i =>
        {
            if (updating || !Enum.TryParse(presetNames[i], out GraphicsPreset preset))
            {
                return;
            }

            updating = true;
            try
            {
                foreach ((ICVar variable, string value) in Context.Presets.ValuesOf(preset))
                {
                    draft.Stage(variable).TrySetText(value, out _);
                }
            }
            finally
            {
                updating = false;
            }

            ShowPreset();
        };

        EventHandler changed = (_, _) =>
        {
            if (!updating)
            {
                ShowPreset();
            }
        };

        draft.Changed += changed;
        _bindings.Add(new Unsubscribe(() => draft.Changed -= changed));
        return presets;
    }

    private List<SettingsGroup> BuildControls(UiTheme theme)
    {
        PlayerProfile? profile = Context.Config.Active;
        if (profile is null)
        {
            return [];
        }

        // The mouse comes first: it is the setting a player reaches for on the way into their first mission,
        // and unlike a binding it has no default anyone is happy with.
        var sensitivity = new Slider(theme, "Mouse sensitivity", 0f, 1f, profile.MouseSensitivity, 0.05f)
        {
            // What a player is actually choosing is how far the mouse travels for a full turn, so that is
            // what the readout says — in centimetres at a stated DPI.
            Format = v => string.Create(
                CultureInfo.InvariantCulture,
                $"{MouseLookSettings.FromSensitivity(v, invertPitch: false).TurnDistanceCentimetres():0} cm/360°"),
            Tooltip = "How far the mouse turns the view.\n\nThe readout is how much desk a full turn takes on "
                + "an 800 DPI mouse — halve it for 1600 DPI. Lower is slower and more precise.",
        };
        sensitivity.ValueChanged += value => profile.MouseSensitivity = value;

        var invert = new Toggle(theme, "Invert mouse", profile.InvertMouse)
        {
            Tooltip = "Pushing the mouse forward looks down instead of up.",
        };
        invert.ValueChanged += value => profile.InvertMouse = value;

        var bindingRows = new List<KeyBindingRow>(profile.Bindings.Count);
        for (int i = 0; i < profile.Bindings.Count; i++)
        {
            int index = i;
            InputBinding binding = profile.Bindings[index];
            KeyCode current = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed) ? parsed : KeyCode.None;
            var row = new KeyBindingRow(theme, binding.Action, current, InputBindingKeyNames.DisplayName(binding))
            {
                Tooltip = $"Press this row, then a key, mouse button or wheel scroll, to rebind {binding.Action}.",
            };

            row.BindingChanged += key => Rebound(profile.RebindKeyboard(index, key));
            row.MouseBindingChanged += button => Rebound(profile.RebindMouseButton(index, button));
            row.MouseWheelBindingChanged += direction => Rebound(profile.RebindMouseWheel(index, direction));
            bindingRows.Add(row);

            void Rebound(int conflict)
            {
                RefreshBindingRow(bindingRows[index], profile.Bindings[index]);
                if (conflict >= 0)
                {
                    RefreshBindingRow(bindingRows[conflict], profile.Bindings[conflict]);
                }
            }
        }

        _resetBindings = () =>
        {
            profile.ResetBindingsToDefaults();
            for (int i = 0; i < bindingRows.Count; i++)
            {
                RefreshBindingRow(bindingRows[i], profile.Bindings[i]);
            }
        };

        // R does the same from the keyboard; the button is there for the pointer, and says what R would.
        var resetBindings = new Button(theme, "RESET BINDINGS TO DEFAULTS")
        {
            Accent = ButtonAccent.Ghost,
            TextSize = 16f,
            Padding = new Thickness(18f, 10f),
            HorizontalAlignment = HorizontalAlignment.Left,
            Tooltip = "Restore the standard WASD keyboard and mouse assignments.",
        };
        resetBindings.Activated += _ => _resetBindings?.Invoke();

        List<SettingsGroup> groups = [SettingsGroup.Of("Mouse", [sensitivity, invert, resetBindings])];
        groups.Add(SettingsGroup.Of("Key bindings", [.. bindingRows]));
        return groups;
    }

    private static void RefreshBindingRow(KeyBindingRow row, InputBinding binding)
    {
        KeyCode key = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed)
            ? parsed
            : KeyCode.None;
        row.SetBinding(key, InputBindingKeyNames.DisplayName(binding));
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

    private List<ICVar> CurrentTabCvars => _tabCvars.Count > 0 ? _tabCvars[^1] : [];

    /// <summary>The draft the staged categories write to, which keeps the Apply bar's state.</summary>
    private SettingsDraft CreateDraft()
    {
        var draft = new SettingsDraft(Context.Cvars);
        draft.Changed += (_, _) => UpdateApplyBar();
        return draft;
    }

    private ICVar? FindLive(string name) => Context.Cvars.TryGet(name, out ICVar? cvar) ? cvar : null;

    /// <summary>The note and the two buttons beside the help line on a staged category.</summary>
    /// <remarks>
    /// Sized as the category tabs are, not as the page's call-to-action buttons: it is a quiet status that
    /// appears with a change and goes with it, rather than two greyed boxes standing by on every page.
    /// </remarks>
    private FlexPanel BuildApplyBar(UiTheme theme)
    {
        _applyNote = new Label(theme.Heading, "UNAPPLIED CHANGES", 14f, theme.Accent)
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        _discard = new Button(theme, "Discard") { Accent = ButtonAccent.Ghost, TextSize = 14f, Tracking = theme.LabelTracking, Padding = new Thickness(20f, 8f) };
        _discard.Activated += _ => _draft?.Discard();
        _apply = new Button(theme, "Apply") { Accent = ButtonAccent.Solid, CutCorner = 8f, TextSize = 14f, Tracking = theme.LabelTracking, Padding = new Thickness(22f, 8f) };
        _apply.Activated += _ => Apply();

        var bar = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, VerticalAlignment = VerticalAlignment.Center };
        bar.Add(_applyNote);
        bar.Add(_discard);
        bar.Add(_apply);
        _applyBar = bar;
        return bar;
    }

    /// <summary>Shows the Apply bar while a staged category has something to apply, and its keys in the footer.</summary>
    private void UpdateApplyBar()
    {
        bool shown = IsStagedTab && _draft?.HasChanges == true;
        if (_applyBar is not null)
        {
            _applyBar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        Focus?.Validate();
        if (_prompt is null)
        {
            RefreshHints();
        }
    }

    /// <summary>Writes the draft to the game, then asks for a new display mode to be kept.</summary>
    /// <param name="then">What to do once it is applied -- leave the page -- unless a display mode now has to be kept.</param>
    private void Apply(Action? then = null)
    {
        ShowPrompt(null);
        if (_draft is not { HasChanges: true } draft)
        {
            then?.Invoke();
            return;
        }

        // The graphics API last: changing it rebuilds the game, which should come back with everything else
        // already in force and saved.
        bool switchesApi = draft.Pending.Any(static change => change.Variable.Name == "r_renderer");
        IReadOnlyList<(ICVar Variable, string Previous)> written =
            draft.Commit(static variable => variable.Name == "r_renderer");

        // The writes each read as the player taking over from the preset; the preset is whatever they add up to.
        Context.Presets.Detect();
        Context.Host.SaveSettings();

        // A rebuilt game starts with the new API and window both, and nothing on screen to answer with.
        List<(ICVar Variable, string Previous)> display =
            [.. written.Where(static change => DisplayModeVariables.Contains(change.Variable.Name))];
        if (display.Count > 0 && !switchesApi)
        {
            AskToKeep(display);
            return;
        }

        then?.Invoke();
    }

    /// <summary>Asks for a display mode just applied to be kept, and puts it back if it is not.</summary>
    /// <param name="previous">Each display variable written, with what it held before.</param>
    private void AskToKeep(List<(ICVar Variable, string Previous)> previous)
    {
        void Revert()
        {
            ShowPrompt(null);
            foreach ((ICVar variable, string text) in previous)
            {
                variable.TrySetText(text, out _);
            }

            Context.Host.SaveSettings();
        }

        ShowPrompt(new Prompt(
            PromptKind.Keep,
            "Display",
            "Keep these display settings?",
            elapsed => string.Create(
                CultureInfo.InvariantCulture,
                $"The previous ones come back in {Math.Max(0, (int)MathF.Ceiling(KeepSeconds - elapsed))} s."),
            "Revert",
            [
                new("Keep", ButtonAccent.Solid, () => ShowPrompt(null)),
                new("Revert", ButtonAccent.Ghost, Revert),
            ],
            KeepSeconds));
    }

    /// <summary>Puts a question on screen over the page, or takes it away with <see langword="null"/>.</summary>
    private void ShowPrompt(Prompt? prompt)
    {
        _prompt = prompt;
        _promptButtons.Clear();
        _promptMessage = null;
        if (_page is not null)
        {
            // The page behind a question is not there to be clicked.
            _page.IsHitTestVisible = prompt is null;
        }

        if (_promptLayer is null)
        {
            return;
        }

        _promptLayer.ClearChildren();
        if (prompt is null)
        {
            Focus?.Validate();
            Focus?.FocusFirst();
            RefreshHints();
            return;
        }

        UiTheme theme = Context.Theme;
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14f };
        column.AddChild(new SectionTitle(theme, prompt.Eyebrow, prompt.Title) { HorizontalAlignment = HorizontalAlignment.Left, TitleSize = 34f, Margin = new Thickness(0f, 0f, 0f, 4f) });
        _promptMessage = new Label(theme.Body, prompt.Message(prompt.Elapsed), 17f, UiPalette.TextBody) { Wrap = true };
        column.AddChild(_promptMessage);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 12f, Margin = new Thickness(0f, 10f, 0f, 0f) };
        foreach (PromptChoice choice in prompt.Choices)
        {
            var button = new Button(theme, choice.Caption) { Accent = choice.Accent, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
            if (choice.Accent == ButtonAccent.Solid)
            {
                button.CutCorner = 10f;
            }

            button.Activated += _ => choice.Chosen();

            // Made after the page's entrance, so it plays its own: a control starts transparent until one has.
            button.PlayEnter(_promptButtons.Count * EntranceStagger);
            actions.Add(button);
            _promptButtons.Add(button);
        }

        column.AddChild(actions);
        var panel = new GlassPanel(theme)
        {
            Padding = new Thickness(44f, 32f),
            Width = 640f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.AddChild(column);
        _promptLayer.AddChild(panel);

        Focus?.Focus(_promptButtons[0]);
        RefreshHints();
    }

    private void TrackForReset(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (_find(name) is { } cvar)
            {
                CurrentTabCvars.Add(cvar);
            }
        }
    }

    /// <summary>The word that groups a variable: <c>g_ragdoll_force</c> groups under <c>ragdoll</c>.</summary>
    private static string GroupWord(string name)
    {
        string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[1] : string.Empty;
    }

    private static string Humanize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

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

    /// <summary>What a question on the page is about.</summary>
    private enum PromptKind
    {
        /// <summary>Leaving with changes not applied.</summary>
        Leave,

        /// <summary>Keeping a display mode just applied.</summary>
        Keep,
    }

    /// <summary>One answer to a question: its button, and what choosing it does.</summary>
    private sealed record PromptChoice(string Caption, ButtonAccent Accent, Action Chosen);

    /// <summary>A question over the page, and how long it has been asked.</summary>
    /// <param name="Kind">What it is about.</param>
    /// <param name="Eyebrow">The line above its title.</param>
    /// <param name="Title">The question.</param>
    /// <param name="Message">What it says under the title, given the seconds it has been on screen.</param>
    /// <param name="BackCaption">What back does, for the footer: the last answer, the cautious one.</param>
    /// <param name="Choices">The answers, the one wanted first.</param>
    /// <param name="Expires">After how many seconds back answers it on the player's behalf, or never.</param>
    private sealed record Prompt(
        PromptKind Kind,
        string Eyebrow,
        string Title,
        Func<float, string> Message,
        string BackCaption,
        IReadOnlyList<PromptChoice> Choices,
        float? Expires = null)
    {
        public float Elapsed { get; set; }

        public void Back() => Choices[^1].Chosen();
    }

    /// <summary>A titled run of rows: one glass panel's worth, or the start of one that continues.</summary>
    private sealed record SettingsGroup(string Title, List<Control> Rows)
    {
        public static SettingsGroup Of(string title, IEnumerable<Control> rows) => new(title, [.. rows]);
    }

    // Runs its action once, on the first dispose.
    private sealed class Unsubscribe(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
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
