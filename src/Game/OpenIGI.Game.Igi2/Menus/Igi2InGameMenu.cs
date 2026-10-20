using OpenIGI.Game.Menus;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Menus;

/// <summary>Where the in-game menu's manager is, <c>MenuManager_s.eStatus</c>.</summary>
public enum Igi2MenuStatus
{
    /// <summary>The current screen is opened, taking its data in, then fades in.</summary>
    Activate = 0,

    /// <summary>The current screen is opened without taking its data in, then fades in.</summary>
    ActivateNoData = 1,

    /// <summary>The current screen is opened, taking its data in, already faded in.</summary>
    ActivateShown = 2,

    /// <summary>The screen takes input.</summary>
    Running = 3,

    /// <summary>The screen takes input, its pointer let go.</summary>
    Released = 4,

    /// <summary>The menu's language changes.</summary>
    ChangeLanguage = 5,

    /// <summary>The screen fades in.</summary>
    FadeIn = 6,

    /// <summary>The screen fades out, then the next status is taken.</summary>
    FadeOut = 7,

    /// <summary>A popup fades in.</summary>
    PopupFadeIn = 8,

    /// <summary>A popup fades out, then the next status is taken.</summary>
    PopupFadeOut = 9,

    /// <summary>No menu shows: the game plays (<c>MENUMANAGER_IDLE</c>).</summary>
    Idle = 10,

    /// <summary>The game is quit (<c>MENUMANAGER_QUIT</c>).</summary>
    Quit = 11,

    /// <summary>The game goes back to the main menu (<c>MENUMANAGER_QUITTOMAINMENU</c>).</summary>
    QuitToMainMenu = 12,

    /// <summary>The level starts again (<c>MENUMANAGER_RESTARTLEVEL</c>).</summary>
    RestartLevel = 13,

    /// <summary>A game starts (<c>MENUMANAGER_STARTGAME</c>).</summary>
    StartGame = 14,

    /// <summary>A network game is joined (<c>MENUMANAGER_JOINGAME</c>).</summary>
    JoinGame = 15,

    /// <summary>A popup is opened.</summary>
    ActivatePopup = 16,

    /// <summary>The popup is gone: the screen takes input again.</summary>
    PopupGone = 17,

    /// <summary>The menu is disabled.</summary>
    Disabled = 18,

    /// <summary>The menu is enabled: the game plays.</summary>
    Enabled = 19,
}

/// <summary>What the in-game menu asks of the game when it leaves: <c>Flow_s.eRequestedEvent</c>.</summary>
public enum Igi2MenuRequest
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Quit the game.</summary>
    Quit,

    /// <summary>Go back to the main menu.</summary>
    QuitToMainMenu,

    /// <summary>Start the level again.</summary>
    RestartLevel,

    /// <summary>Start a game.</summary>
    StartGame,

    /// <summary>Join a network game.</summary>
    JoinGame,
}

/// <summary>
/// I.G.I. 2's in-game menu, the <c>MenuManager</c> <c>GameScreen_CreateHandler</c> makes of <c>LOCAL:menusystem/ingamemenu.qsc</c>
/// (gamescreen.c, menumanager.c): its screens, the stack it pushes them on, its popup, and the status it runs through, with every native
/// its script calls.
/// </summary>
/// <remarks>
/// <para>
/// The OpenIGI shell presents the menu, so drawing, layout and keyboard handling are not kept: a screen's fade takes the eight ticks it
/// would take to draw, and the shell calls <see cref="Open"/>, <see cref="PressEscape"/> and the windows' own methods where the retail
/// menu would read the keyboard.
/// </para>
/// <para>
/// <c>MenuManager_RunHandler</c>: opening a screen takes its data in (<c>MenuWindow_eInitDataEvent</c>) unless it is opened without; a
/// fade in leads to input, a fade out to the status asked for; idle, a failed level whose player still lives opens the menu by itself
/// after five seconds, pausing the game; the quit, main menu and restart statuses ask the game for them.
/// </para>
/// </remarks>
public sealed class Igi2InGameMenu
{
    /// <summary>The menu script, <c>LOCAL:menusystem/ingamemenu.qsc</c>, as compiled.</summary>
    public const string Script = "LOCAL:menusystem/ingamemenu.qvm";

    /// <summary>The ticks a failed level waits before the menu opens by itself.</summary>
    public const int FailedOpenTicks = 150;

    /// <summary>The fade's full value: a fade steps two a tick.</summary>
    public const int FadeSteps = 16;

    /// <summary>The most screens the stack holds.</summary>
    public const int StackDepth = 64;

    private object? _active;
    private int _failedTicks;

    /// <summary>Initializes a new instance of the <see cref="Igi2InGameMenu"/> class, idle.</summary>
    public Igi2InGameMenu()
    {
        Navigator.Reset(-1, MenuManagerState.Idle);
        Navigator.SaveRequested += (_, _) => ReadDataFromActiveWindow();
    }

    /// <summary>Gets the manager's state machine, the one Project I.G.I.'s menus run, as I.G.I. 2 numbers and extends it.</summary>
    public MenuNavigator Navigator { get; } = new(MenuManagerDialect.Igi2);

    /// <summary>Gets the manager the script made, once loaded.</summary>
    public Igi2MenuManagerTask? Manager { get; private set; }

    /// <summary>Gets the screen asked for, <c>nActiveMenuScreenID</c>.</summary>
    public int CurrentScreenId => Navigator.CurrentScreen;

    /// <summary>Gets the screen open, <c>ptActiveMenuScreen</c>.</summary>
    public Igi2MenuScreenTask? CurrentScreen { get; private set; }

    /// <summary>Gets the popup asked for, <c>nActivePopupScreenID</c>.</summary>
    public int PopupScreenId => Navigator.PopupActive ? Navigator.PopupScreen : -1;

    /// <summary>Gets the popup open, <c>ptActivePopupScreen</c>: a dialog window.</summary>
    public MenuWindowTask? PopupScreen { get; private set; }

    /// <summary>Gets the status, <c>eStatus</c>, as I.G.I. 2 numbers it.</summary>
    public Igi2MenuStatus Status => (Igi2MenuStatus)MenuManagerDialect.Igi2.ValueOf(Navigator.State);

    /// <summary>Gets the status a fade out leads to, <c>eNextStatus</c>, as I.G.I. 2 numbers it.</summary>
    public Igi2MenuStatus NextStatus => (Igi2MenuStatus)MenuManagerDialect.Igi2.ValueOf(Navigator.ReturnState);

    /// <summary>Gets the screens pushed, the most recent first.</summary>
    public IReadOnlyCollection<int> Stack => [.. Navigator.SavedScreens.Reverse()];

    /// <summary>Gets the language the script last set, <c>MenuManager_SetLanguage</c>.</summary>
    public int Language { get; private set; }

    /// <summary>Gets a value indicating whether a menu shows: anything but idle.</summary>
    public bool IsOpen => Status != Igi2MenuStatus.Idle && Status != Igi2MenuStatus.Enabled;

    /// <summary>Gets what the menu last asked of the game, until the game takes it with <see cref="TakeRequest"/>.</summary>
    public Igi2MenuRequest Request { get; private set; }

    /// <summary>Gets or sets whether the level has failed, <c>LevelFlow_LevelFailed</c>.</summary>
    public Func<bool> LevelFailed { get; set; } = static () => false;

    /// <summary>Gets or sets whether the player is dead.</summary>
    public Func<bool> PlayerDead { get; set; } = static () => false;

    /// <summary>Gets or sets what pauses the game while the menu shows, <c>Game_SetPaused</c>.</summary>
    public Action<bool>? SetPaused { get; set; }

    /// <summary>Gets the list, picture box or input box whose script is running, which the filling natives write to.</summary>
    public object? Active => _active;

    /// <summary>Registers the natives the menu's scripts call, and its constants.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The function table the menu's level expressions call.</param>
    public void Register(NativeRegistry natives, Scripting.Igi2ScriptFunctions? functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        void Fn(string name, Scripting.Igi2ScriptFunction function, int arguments, QvmValueKind kind = QvmValueKind.None) =>
            Scripting.Igi2ScriptCall.Register(natives, functions, name, function, arguments);

        Fn("MenuManager_RequestScreen", c => Do(() => Navigator.Request(c.GetInt32(0), c.GetInt32(1) != 0)), 2);
        Fn("MenuManager_LeaveMenus", c => Do(() => Navigator.Leave(
            c.GetInt32(0), MenuManagerDialect.Igi2.StateOf(c.GetInt32(1)), c.GetInt32(2) != 0, LevelFailed())), 3);
        Fn("MenuManager_ClearStack", _ => Do(Navigator.ClearStack), 1);
        Fn("MenuManager_SetLanguage", c => Do(() =>
        {
            Language = c.GetInt32(0);
            Navigator.SetLanguage();
        }), 1);
        Fn("MenuManager_PushScreen", c => Do(() => Navigator.Push(c.GetInt32(0))), 1);
        Fn("MenuManager_PopScreen", c => Do(() => Navigator.Pop(c.GetInt32(0) != 0)), 1);
        Fn("MenuManager_ForceUpdateWindow", c => Do(() => ForceUpdateWindow(c.GetInt32(0))), 1);
        // MenuManager_SetEnabled is the flow's (Igi2FlowState), which the shell reads.
        Fn("MenuManager_ActivatePopupScreen", c => Do(() => Navigator.ActivatePopup(c.GetInt32(0))), 1);
        Fn("MenuManager_DeactivatePopuScreen", _ => Do(Navigator.DeactivatePopup), 0);
        natives.RegisterConstant("MENUMANAGER_IDLE", MenuManagerDialect.Igi2.ValueOf(MenuManagerState.Idle));
        natives.RegisterConstant("MENUMANAGER_QUIT", MenuManagerDialect.Igi2.ValueOf(MenuManagerState.Quit));
        natives.RegisterConstant("MENUMANAGER_QUITTOMAINMENU", MenuManagerDialect.Igi2.ValueOf(MenuManagerState.QuitToMainMenu));
        natives.RegisterConstant("MENUMANAGER_RESTARTLEVEL", MenuManagerDialect.Igi2.ValueOf(MenuManagerState.RestartLevel));
        natives.RegisterConstant("MENUMANAGER_STARTGAME", MenuManagerDialect.Igi2.ValueOf(MenuManagerState.StartGame));
        natives.RegisterConstant("MENUMANAGER_JOINGAME", MenuManagerDialect.Igi2.ValueOf(MenuManagerState.JoinGame));

        // LevelFlow_LevelFailedCB.
        Fn("LevelFlow_LevelFailed", _ => QvmValue.FromInt32(LevelFailed() ? 1 : 0), 0);

        // ColumnView_ScriptSortFunction: the view, the column, and whether descending.
        Fn("ColumnView_Sort", Sort, 3);

        // ControlsMenu_ConvertFloatFromOffsetCB and ControlsMenu_ConvertOffsetFromFloatCB: the scroll bar shows a forty-third of the
        // rows a step.
        Fn(
            "ControlsMenu_ConvertFloatFromOffset", c => QvmValue.FromDouble(c.GetInt32(0) * 0.023255813866853714), 1, QvmValueKind.Float);
        Fn(
            "ControlsMenu_ConvertOffsetFromFloat", c => QvmValue.FromInt32((int)((c.GetSingle(0) * 43.0) + 0.5)), 1);

        static QvmValue Do(Action action)
        {
            action();
            return QvmValue.Zero;
        }
    }

    /// <summary>
    /// Makes the menu as a level starts, <c>MenuManager_New</c>: the script run with its tasks made under a container, every window
    /// handed the menu, and the manager set on the screen it starts on.
    /// </summary>
    /// <param name="container">The task the menu is made under.</param>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <c>Task_New</c>.</param>
    /// <returns><see langword="false"/> when there is no menu script.</returns>
    public bool Load(GameTask container, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        if (!resources.TryLoad(Script, out _) || !natives.TryResolve(TaskScriptCommands.TaskNew, out QvmSymbol? taskNew))
        {
            return false;
        }

        object? previous = taskNew.Context;
        natives.SetSymbolContext(TaskScriptCommands.TaskNew, container);
        try
        {
            Scripting.Igi2SessionScript.Run(resources, natives, Script);
        }
        finally
        {
            natives.SetSymbolContext(TaskScriptCommands.TaskNew, previous);
        }

        Manager = container.Descendants().OfType<Igi2MenuManagerTask>().FirstOrDefault();
        foreach (GameTask task in container.Descendants())
        {
            switch (task)
            {
                case Igi2MenuManagerTask manager:
                    manager.Menu = this;
                    break;
                case Igi2ListBoxTask list:
                    list.Menu = this;
                    break;
                case Igi2ScrollListBoxTask list:
                    list.Menu = this;
                    break;
                case Igi2PictureBoxTask pictures:
                    pictures.Menu = this;
                    break;
                case Igi2InputBoxTask input:
                    input.Menu = this;
                    break;
            }
        }

        Navigator.Reset(Manager?.CurrentScreen ?? -1, MenuManagerState.Idle);
        (Request, PopupScreen, CurrentScreen) = (Igi2MenuRequest.None, null, null);
        return Manager is not null;
    }

    /// <summary>Gets a screen of the menu by its id, among the menu's own tasks.</summary>
    /// <param name="id">The screen's id.</param>
    /// <returns>The screen, or <see langword="null"/>.</returns>
    public Igi2MenuScreenTask? Screen(int id) =>
        Manager is null || id < 0 ? null : Manager.Descendants().OfType<Igi2MenuScreenTask>().FirstOrDefault(s => s.Id == id);

    /// <summary>Gets a window of the menu by its id, among the menu's own tasks: a screen, or a popup's dialog window.</summary>
    /// <param name="id">The window's id.</param>
    /// <returns>The window, or <see langword="null"/>.</returns>
    public MenuWindowTask? Window(int id) =>
        Manager is null || id < 0 ? null : Manager.Descendants().OfType<MenuWindowTask>().FirstOrDefault(w => w.Id == id);

    /// <summary>Opens the menu on its default screen, pausing the game, as Escape does while the game plays.</summary>
    /// <param name="level">The level the default screen's expression is evaluated in.</param>
    public void Open(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Manager is null || Status != Igi2MenuStatus.Idle)
        {
            return;
        }

        CurrentScreen = null;
        Navigator.Open(Manager.DefaultScreen(level));
        SetPaused?.Invoke(true);
    }

    /// <summary>Presses Escape on the open screen: its Escape script runs.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    public void PressEscape(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Status is Igi2MenuStatus.Running or Igi2MenuStatus.Released)
        {
            (PopupScreen as Igi2MenuScreenTask ?? CurrentScreen)?.PressEscape(level);
        }
    }

    /// <summary>Takes what the menu asked of the game, leaving nothing asked.</summary>
    /// <returns>The request.</returns>
    public Igi2MenuRequest TakeRequest()
    {
        Igi2MenuRequest request = Request;
        Request = Igi2MenuRequest.None;
        return request;
    }

    /// <summary>Runs a window's script while it is the one the filling natives write to.</summary>
    /// <param name="window">The list, picture box or input box.</param>
    /// <param name="script">The script.</param>
    public void Collect(object window, Action script)
    {
        ArgumentNullException.ThrowIfNull(script);
        object? previous = _active;
        _active = window;
        try
        {
            script();
        }
        finally
        {
            _active = previous;
        }
    }

    /// <summary>Adds an item to the active list, <c>ListBox_AddItem</c> and <c>ScrollListBox_AddItemEx</c>.</summary>
    /// <param name="item">The item.</param>
    public void AddItem(Igi2MenuItem item) => (_active as IIgi2MenuItemList)?.Add(item);

    /// <summary>Writes the active input box's text, as <c>NetManager_GetNameCB</c> does.</summary>
    /// <param name="text">The text.</param>
    public void SetInputText(string text)
    {
        if (_active is Igi2InputBoxTask input)
        {
            input.Data = text;
        }
    }

    /// <summary>Colours the active picture box, as <c>Config_GetRGBA</c> does.</summary>
    /// <param name="colour">The colour.</param>
    public void SetPictureColour((float R, float G, float B, float A) colour)
    {
        if (_active is Igi2PictureBoxTask pictures)
        {
            pictures.Colour = colour;
        }
    }

    /// <summary>Takes the menu a tick, <c>MenuManager_RunHandler</c>.</summary>
    /// <param name="level">The level its scripts are evaluated in.</param>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Level = level;
        if (Navigator.State != MenuManagerState.Idle)
        {
            _failedTicks = 0;
        }
        else if (LevelFailed() && !PlayerDead() && ++_failedTicks > FailedOpenTicks)
        {
            // A failed level whose player still lives opens the menu by itself after five seconds.
            _failedTicks = 0;
            Open(level);
            return;
        }

        MenuStep step = Navigator.Tick();
        switch (step)
        {
            case MenuStep.ActivateScreen or MenuStep.ReturnToScreen:
                // MenuManager_ActivateScreen: the screen asked for, its data taken in when asked.
                CurrentScreen = Screen(Navigator.CurrentScreen);
                if (step == MenuStep.ActivateScreen)
                {
                    CurrentScreen?.InitData(level);
                }

                break;
            case MenuStep.ActivatePopup:
                PopupScreen = Window(Navigator.PopupScreen);
                (PopupScreen as IIgi2MenuData)?.InitData(level);
                break;
            case MenuStep.FadedOut:
                // MenuManager_DeactivatePopupScreen goes with the fade; a fade into idle closes the menu and lets the game play.
                PopupScreen = null;
                if (Navigator.State == MenuManagerState.Idle)
                {
                    CurrentScreen = null;
                    SetPaused?.Invoke(false);
                }

                break;
            case MenuStep.Leave:
                Request = Navigator.Left switch
                {
                    MenuManagerState.Quit => Igi2MenuRequest.Quit,
                    MenuManagerState.QuitToMainMenu => Igi2MenuRequest.QuitToMainMenu,
                    MenuManagerState.RestartLevel => Igi2MenuRequest.RestartLevel,
                    MenuManagerState.StartGame => Igi2MenuRequest.StartGame,
                    _ => Igi2MenuRequest.JoinGame,
                };
                break;
        }
    }

    /// <summary>Gets the level the menu last ran in, which the natives' data exchanges are evaluated in.</summary>
    private Igi2LevelRun? Level { get; set; }

    // MenuManager_ReadDataFromActiveWindow: the open screen gives its data back.
    private void ReadDataFromActiveWindow()
    {
        if (Level is { } level)
        {
            CurrentScreen?.ChangeData(level);
        }
    }

    // MenuManager_ForceUpdateWindowCB: the window takes its data in again.
    private void ForceUpdateWindow(int id)
    {
        if (Manager is not null && Level is { } level
            && Window(id) is IIgi2MenuData window)
        {
            window.InitData(level);
        }
    }

    private QvmValue Sort(Scripting.Igi2ScriptCall call)
    {
        if (call.Count == 3 && Manager is not null
            && Manager.Descendants().OfType<Igi2ColumnViewTask>().FirstOrDefault(v => v.Id == call.GetInt32(0)) is { } view)
        {
            view.Sort(call.GetInt32(1), call.GetInt32(2) != 0);
        }

        return QvmValue.Zero;
    }
}
