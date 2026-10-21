using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Config;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The manager's per-tick behaviour: input, selection, navigation and drawing.
/// </summary>
/// <remarks>
/// <para>
/// This is the class's update and render methods — <c>MenuManager_Update</c> (<c>0x418080</c>) and
/// <c>MenuManager_Render</c> (<c>0x418970</c>) — so it runs off the task tree's walk rather than from a
/// loop in the shell.
/// </para>
/// <para>
/// The manager is created by the menu script before it is initialised, so everything here tolerates an
/// uninitialised manager and does nothing until <see cref="Initialize"/> has run.
/// </para>
/// </remarks>
public partial class MenuManagerTask
{
    /// <summary>The sound a selection move or a control change plays.</summary>
    public const string MoveSoundName = "menu_high";

    /// <summary>The sound an entry plays when it is chosen and names no sound of its own.</summary>
    public const string ChooseSoundName = "menu_sel";

    private readonly MenuPointer _pointer = new();
    private IMenuHost? _host;
    private MenuSystem? _system;
    private readonly HashSet<string> _unknownNatives = new(StringComparer.Ordinal);
    private ScriptExpressionEvaluator? _evaluator;
    private MenuScreenTask? _current;
    private DialogWindowTask? _popup;
    private IMenuConfiguration? _configuration;
    private IOpenIgiLogger _logger = NullLogger.Instance;

    /// <summary>Gets the screen stack and fade this manager drives.</summary>
    public MenuNavigator Navigator { get; } = new();

    /// <summary>Gets the selection the pointer and the keys share.</summary>
    public MenuSelection Selection { get; } = new();

    /// <summary>Gets the script namespace menu actions are evaluated against.</summary>
    public MenuScriptContext? ScriptContext { get; private set; }

    /// <summary>Gets the turning green overlay every screen draws behind itself.</summary>
    /// <remarks>
    /// The manager owns it — field <c>+10208</c> — and <c>MenuScreen_Render</c> reaches back for it,
    /// which is why it lives here and not on the screen.
    /// </remarks>
    public BackgroundFxTask BackgroundFx { get; } = new();

    /// <summary>Gets the pointer state every control hit-tests against.</summary>
    /// <remarks>
    /// The original's controls read it out of the manager the same way — <c>MenuItem_HitTest</c>
    /// fetches the manager's pointer block rather than being handed one.
    /// </remarks>
    public MenuPointer Pointer => _pointer;

    /// <summary>Gets the screen currently being shown.</summary>
    public MenuScreenTask? CurrentScreen => _current;

    /// <summary>Gets the popup shown over the screen, <see langword="null"/> while there is none.</summary>
    /// <remarks>
    /// The manager's <c>+52</c>: set as the popup is taken in (<c>0x4188B0</c>) and cleared at the bottom of a fade
    /// (<c>0x418940</c>). While it is set it stands in for the screen: its controls take the input and the update, and the
    /// screen's do not.
    /// </remarks>
    public DialogWindowTask? CurrentPopup => _popup;

    // The window the pointer, the keys and the update reach: the popup while there is one, else the screen.
    private MenuWindowTask? Shown => (MenuWindowTask?)_popup ?? _current;

    /// <summary>Wires the manager to its shell and starts it on the screen the script named.</summary>
    /// <param name="system">The loaded menu, used to resolve screen ids.</param>
    /// <param name="host">The shell providing input, presentation and sound.</param>
    /// <param name="logger">Where to report menu activity.</param>
    /// <param name="variables">
    /// The namespace the menu's named variables are bound in. Defaults to
    /// <paramref name="system"/>'s, which is what a script loaded into a game loop has.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Initialize(MenuSystem system, IMenuHost host, IOpenIgiLogger? logger = null, ScriptVarRegistry? variables = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(host);

        _system = system;
        _host = host;
        _logger = logger ?? NullLogger.Instance;

        ScriptContext = new MenuScriptContext(Navigator, _logger)
        {
            Variables = variables ?? system.Variables,
            Fallback = _configuration is null ? null : Invoke,
            UpdateWindow = ForceUpdateWindow,
        };
        _evaluator = new ScriptExpressionEvaluator(ScriptContext);

        Selection.Moved += (_, _) => _host?.PlaySound(MoveSoundName);

        // A pop or a request that asks to commit writes every control on the screen back out first.
        Navigator.SaveRequested += (_, _) => _current?.SaveData();
        Navigator.Start(CurrentMenuScreen);
        EnterScreen(system.FindScreen(CurrentMenuScreen));
    }

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        BackgroundFx.Receive(TaskMessage.Update);
        if (_host is null || _system is null)
        {
            base.OnUpdate();
            return;
        }

        // 0x418080 updates no child of its own: in the states that take input (2 and 3) it updates the popup if there is one and
        // the screen otherwise, after its own work, so a key posted this tick is consumed by the control on the same tick. In
        // every other state neither runs.
        bool live = Navigator.State is MenuManagerState.Active or MenuManagerState.Inactive;
        Step();
        if (live && Shown is { } shown)
        {
            Tree.Send(shown, TaskMessage.Update);
        }
    }

    private void Step()
    {
        if (_host is null || _system is null)
        {
            return;
        }

        // Advance the fade first: the screen changes at the bottom of it.
        switch (Navigator.Tick())
        {
            case MenuStep.ActivateScreen:
                EnterScreen(_system.FindScreen(Navigator.CurrentScreen), takeData: true);
                break;
            case MenuStep.ReturnToScreen:
                EnterScreen(_system.FindScreen(Navigator.CurrentScreen), takeData: false);
                break;
            case MenuStep.ChangeLanguage when _current is not null:
                // 0x417590: the screen takes the language event, then is prepared and lined up again.
                _current.PrepareSubtree();
                _host.PrepareWindow(_current);
                _current.Arrange();
                break;
            case MenuStep.ActivatePopup:
                // 0x4188B0: the popup takes its data in, is prepared (0x417660) and lined up, and the selection is its own
                // (0x4187F0).
                if (_system.FindPopup(Navigator.PopupScreen) is { } popup)
                {
                    _popup = popup;
                    popup.LoadData();
                    popup.PrepareSubtree();
                    _host.PrepareWindow(popup);
                    popup.Arrange();
                    Selection.Rebuild(popup);
                }

                break;
            case MenuStep.Leave:
                // Clear it before the host runs: leaving is a one-shot request, and the handler for
                // StartGame loads a whole mission. Leaving the state set re-ran that load every tick.
                MenuManagerState leaving = Navigator.Left;
                Navigator.CompleteLeave();
                _host.LeaveMenus(leaving);
                return;
        }

        // 0x418940: at the bottom of a fade the popup goes, and the screen is prepared again and has the selection back.
        if (_popup is not null && !Navigator.PopupActive)
        {
            _popup = null;
            if (_current is not null)
            {
                _current.PrepareSubtree();
                _host.PrepareWindow(_current);
                _current.Arrange();
                Selection.Rebuild(_current);
            }
        }

        if (_current is null)
        {
            return;
        }

        // The hit-test runs on EVERY tick, not only while active. In the original each control calls
        // MenuItem_HitTest from its own update — MenuText_Update (0x4207E0) does it unconditionally —
        // so the flags are recomputed whatever the manager is doing.
        //
        // Skipping it during a fade latches them instead, and a latched WasClicked re-runs the entry's
        // On Click Script every tick: the push that started the fade fires again, restarts the fade,
        // and the menu never reaches Active again. That reads as a freeze, because the same Active
        // gate gates the keys, the pointer and the cursor.
        UpdatePointer();

        if (Navigator.State != MenuManagerState.Active || ScriptContext?.IsEnabled != true)
        {
            return;
        }

        HandleKeys();
        HandleTyping();
        MoveSelectionToHover();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The original (<c>0x418970</c>) submits the pointer, then the popup, then the screen, nearest first, setting the draw
    /// alpha before each: the screen at full while a popup fades in, out or has just gone, and the popup at the one fade. Drawn
    /// here back to front: the screen, the popup over it, the pointer over both.
    /// </remarks>
    protected override void OnRender()
    {
        base.OnRender();

        if (_current is not null)
        {
            _host?.DrawScreen(_current);
        }

        if (_popup is not null)
        {
            _host?.DrawPopup(_popup, Navigator.PopupShownAlpha);
        }

        // Only while Active, so the cursor disappears during a fade — that part is the original's.
        //
        // DIVERGENCE: MenuManager_Render draws the pointer *first*, before the popup and the screen.
        // Taken literally through a painter's-order 2D batch that buries it under the background, so
        // it is drawn last here. Why the original gets away with the other order is unresolved and is
        // listed in the re-note; the visible result is the same cursor in the same place.
        if (Navigator.State == MenuManagerState.Active)
        {
            _host?.DrawPointer(_pointer.X, _pointer.Y);
        }
    }

    /// <summary>Runs one menu action expression.</summary>
    /// <param name="source">The expression source, which may be empty.</param>
    /// <param name="self">The task <c>this.</c> refers to while it runs.</param>
    /// <remarks>Reports a failure to the log and carries on, as the original does.</remarks>
    public void RunAction(string source, GameTask? self = null)
    {
        if (_evaluator is null || ScriptContext is null || ScriptExpressionParser.IsBlank(source))
        {
            return;
        }

        // `this` binds to whatever task owns the expression, so a slider's Set Data Script writes its
        // own vValue and not some other control's. A list filler needs the same thing by a different
        // route: Config_Fill* writes into whichever control is running its collect script.
        ScriptContext.Self = self;
        if (_configuration is not null)
        {
            _configuration.Target = self as MenuWindowTask;
        }

        try
        {
            _evaluator.Evaluate(ScriptExpressionParser.Parse(source, ResolveConstant));
        }
        finally
        {
            ScriptContext.Self = null;
        }

        if (_evaluator.HasError)
        {
            _logger.Warning("Menu", $"{source}: {_evaluator.ErrorMessage}");
        }
    }

    /// <summary>Gets or sets the configuration functions menu expressions may call.</summary>
    /// <remarks>
    /// Wired in as the script context's fallback, so a <c>Config_*</c> name the menu evaluates reaches
    /// the real settings. Without one every configuration screen reads zero.
    /// </remarks>
    public IMenuConfiguration? Configuration
    {
        get => _configuration;
        set
        {
            _configuration = value;
            if (ScriptContext is not null)
            {
                ScriptContext.Fallback = value is null ? null : Invoke;
            }
        }
    }

    /// <summary>Gets or sets a resolver for natives outside the menu and configuration sets.</summary>
    /// <remarks>
    /// The menus call a few functions that belong to the game rather than to the menu system —
    /// <c>Game_SetMission</c> is the one the mission screen needs. They cannot go through the QVM symbol
    /// table, because a menu expression is parsed and evaluated by the menu's own evaluator, not by the
    /// script VM. Returning <see langword="null"/> means "not mine".
    /// </remarks>
    public Func<string, ScriptArguments, QvmValue?>? ExternalNatives { get; set; }

    private QvmValue Invoke(string name, ScriptArguments arguments)
    {
        if (_configuration is not null && _configuration.TryInvoke(name, arguments, out QvmValue result))
        {
            return result;
        }

        if (ExternalNatives?.Invoke(name, arguments) is { } external)
        {
            return external;
        }

        // An unknown native used to evaluate to zero in silence, which is how a mission started with id
        // zero rather than the one that was selected. Reporting it once per name keeps a missing native
        // visible without filling the log every tick.
        if (_unknownNatives.Add(name))
        {
            _logger.Warning("Menu", $"No native named '{name}'; it evaluates to zero.");
        }

        return QvmValue.Zero;
    }

    /// <summary>Plays a menu sound through the shell.</summary>
    /// <param name="name">The sound resource name.</param>
    /// <remarks>Controls announce their own changes, so they need this as much as the manager does.</remarks>
    public void PlaySound(string name) => _host?.PlaySound(name);

    /// <summary>Evaluates one expression as a number, with <c>this</c> bound to a task.</summary>
    /// <param name="source">The expression source, which may be empty.</param>
    /// <param name="self">The task <c>this.</c> refers to while it runs.</param>
    /// <returns>The value, or zero when there is nothing to evaluate.</returns>
    /// <remarks>
    /// A control's data expressions go through the float walk, because that is the only one that can
    /// carry a slider's carret position. An integer result is the caller's truncation, not this one's.
    /// </remarks>
    public double EvaluateReal(string source, GameTask? self = null)
    {
        if (_evaluator is null || ScriptContext is null || ScriptExpressionParser.IsBlank(source))
        {
            return 0.0;
        }

        ScriptContext.Self = self;
        if (_configuration is not null)
        {
            _configuration.Target = self as MenuWindowTask;
        }

        double result;
        try
        {
            result = _evaluator.EvaluateReal(ScriptExpressionParser.Parse(source, ResolveConstant));
        }
        finally
        {
            ScriptContext.Self = null;
        }

        if (_evaluator.HasError)
        {
            _logger.Warning("Menu", $"{source}: {_evaluator.ErrorMessage}");
        }

        return result;
    }

    /// <summary>Resolves the constants a menu expression may name.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The value, or <see langword="null"/> when it is not a constant.</returns>
    public static int? ResolveConstant(string name)
    {
        foreach ((string colour, int value) in MenuTaskTypes.TextColours)
        {
            if (string.Equals(colour, name, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return name switch
        {
            "TRUE" => 1,
            "FALSE" => 0,
            "MENUMANAGER_IDLE" => (int)MenuManagerState.Idle,
            "MENUMANAGER_QUIT" => (int)MenuManagerState.Quit,
            "MENUMANAGER_QUITTOMAINMENU" => (int)MenuManagerState.QuitToMainMenu,
            "MENUMANAGER_RESTARTLEVEL" => (int)MenuManagerState.RestartLevel,
            "MENUMANAGER_STARTGAME" => (int)MenuManagerState.StartGame,
            _ => null,
        };
    }

    // 0x418710: the screen asked for, prepared and lined up, its data taken in only when it is entered afresh: a pop returns to a
    // screen as its controls left it.
    private void EnterScreen(MenuScreenTask? screen, bool takeData = true)
    {
        if (screen is null)
        {
            return;
        }

        _current = screen;

        // Resources first, because the sizes the line-up needs come out of them.
        screen.PrepareSubtree();
        _host?.PrepareWindow(screen);
        screen.Arrange();
        Selection.Rebuild(screen);
        if (!takeData)
        {
            return;
        }

        RunAction(screen.OnInitData, screen);

        // Every control reads its current value out of the configuration before it is first drawn,
        // which is the 0x424870 slot run over the screen's subtree.
        // One pass, in creation order. A control that reads another's value — the Select Mission
        // screen's picture and description boxes are built before the list they key off — resolves it
        // from its own update instead, so the order does not matter and nothing has to reload.
        screen.LoadData();
    }

    // MenuManager_ForceUpdateWindow (0x417880): the window the id names takes the event a screen's activation sends it
    // (0x418790), so a screen runs its On Init Data and every window under it reads its value again. The main menu's
    // profile and options screens send it after changing what a list or a toggle shows.
    private void ForceUpdateWindow(int id)
    {
        if (Tree.FromId(id) is not MenuWindowTask window)
        {
            return;
        }

        if (window is MenuScreenTask screen)
        {
            RunAction(screen.OnInitData, screen);
        }

        window.LoadData();
    }

    private void HandleKeys()
    {
        // A field that owns the keyboard takes every key before anything else sees it, which is what
        // lets it use left, right, home and end for editing without the menu navigating away.
        if (Selection.Selected is InputBoxTask { IsEditing: true } editing)
        {
            foreach (MenuInputEvent key in _host!.DrainEvents())
            {
                editing.Edit(key);
            }

            return;
        }

        foreach (MenuInputEvent raw in _host!.DrainEvents())
        {
            // A popup that asks for it swaps the two direction pairs on the way in.
            MenuInputEvent input = Navigator.PopupActive && SwapKeysWanted() ? MenuSelection.SwapKeys(raw) : raw;

            switch (input)
            {
                // The screen's update acts on Escape (0x422110), and a popup's has no Escape: while one is up the key does
                // nothing.
                case MenuInputEvent.Escape when _popup is null:
                    RunAction(_current!.OnPressEscape, _current);
                    break;

                case MenuInputEvent.Escape:
                    break;

                // Left, right and enter are posted to the selected window and acted on by its own
                // update — the manager itself does nothing with them. See MenuWindowCommand.
                case MenuInputEvent.Enter:
                    Post(MenuWindowCommand.Choose);
                    break;

                case MenuInputEvent.Left:
                    Post(MenuWindowCommand.Left);
                    break;

                case MenuInputEvent.Right:
                    Post(MenuWindowCommand.Right);
                    break;

                default:
                    Selection.Handle(input);
                    break;
            }
        }
    }

    /// <summary>Routes typed characters to the selected field, if one is editing.</summary>
    /// <remarks>
    /// Only an <c>InputBox</c> takes text, and only while it owns the keyboard. A field that is merely
    /// selected does not swallow what the player types.
    /// </remarks>
    private void HandleTyping()
    {
        if (Selection.Selected is not InputBoxTask { IsEditing: true } field)
        {
            return;
        }

        foreach (char typed in _host!.DrainTypedText())
        {
            field.TypeCharacter(typed);
        }
    }

    /// <summary>Reads the pointer and refreshes every window's interaction flags.</summary>
    /// <remarks>
    /// The hit-test only sets the flags. Acting on a completed click is the control's own job, in its
    /// update, which is where <c>MenuText_Update</c> reads them.
    /// </remarks>
    private void UpdatePointer()
    {
        if (!_host!.TryGetPointer(out int x, out int y, out bool buttonDown))
        {
            return;
        }

        _pointer.Set(x, y, buttonDown);
        Shown!.HitTestSubtree(_pointer);
    }

    /// <summary>Moves the shared selection onto whatever the pointer is over.</summary>
    /// <remarks>This is the part that must not run during a fade: it is what lights an entry.</remarks>
    private void MoveSelectionToHover()
    {
        foreach (MenuWindowTask item in Selection.Items)
        {
            if (item.IsActive)
            {
                Selection.SelectItem(item);
                break;
            }
        }
    }

    private void Post(MenuWindowCommand command)
    {
        if (Selection.Selected is { } selected)
        {
            selected.PendingCommand = command;
        }
    }

    private bool SwapKeysWanted() =>
        _system?.FindPopup(Navigator.PopupScreen) is { SwapKeys: true };
}
