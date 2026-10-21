namespace OpenIGI.Game.Menus;

/// <summary>
/// The states <c>MenuManager_Update</c> switches on, named from what each branch does.
/// </summary>
/// <remarks>
/// <para>
/// The values are Project I.G.I.'s (<c>0x418080</c>), which its menu scripts see through the five constants the manager's
/// constructor (<c>0x417260</c>) registers. I.G.I. 2's manager runs the same machine with two states more,
/// <see cref="EnterScreenShown"/> and <see cref="JoinGame"/>, numbered in among these; <see cref="MenuManagerDialect"/> holds each
/// game's numbering.
/// </para>
/// <para>
/// <b>Correction.</b> Five of these were named from guesses that the callbacks disprove. State 4 is the language change
/// <c>MenuManager_SetLanguage</c> (<c>0x417810</c>) fades out to; 14 is the popup's activation, which
/// <c>MenuManager_ActivatePopupScreen</c> (<c>0x417900</c>) enters directly; 15 is where <c>MenuManager_DeactivatePopuScreen</c>
/// (<c>0x417960</c>) lands once the popup has faded out; 16 and 17 are what <c>MenuManager_SetEnabled</c> (<c>0x4179B0</c>) sets.
/// </para>
/// </remarks>
public enum MenuManagerState
{
    /// <summary>Open the pending screen, taking its data in, then fade in.</summary>
    EnterScreen = 0,

    /// <summary>Open the pending screen without taking its data in, as a pop returns to it, then fade in.</summary>
    ReturnToScreen = 1,

    /// <summary>Fully faded in and taking input.</summary>
    Active = 2,

    /// <summary>Faded in, its pointer let go.</summary>
    Inactive = 3,

    /// <summary>The language has changed: the screen takes its text again, then fades in.</summary>
    ChangeLanguage = 4,

    /// <summary>Fading in.</summary>
    FadingIn = 5,

    /// <summary>Fading out, after which <see cref="MenuNavigator.ReturnState"/> is entered.</summary>
    FadingOut = 6,

    /// <summary>Fading a popup in.</summary>
    PopupFadingIn = 7,

    /// <summary>Fading a popup out, after which <see cref="MenuNavigator.ReturnState"/> is entered.</summary>
    PopupFadingOut = 8,

    /// <summary>No menu shows: the game plays. <c>MENUMANAGER_IDLE</c>.</summary>
    Idle = 9,

    /// <summary>Leave the menus and quit. <c>MENUMANAGER_QUIT</c>.</summary>
    Quit = 10,

    /// <summary>Leave the current game and return to the main menu. <c>MENUMANAGER_QUITTOMAINMENU</c>.</summary>
    QuitToMainMenu = 11,

    /// <summary>Restart the current level. <c>MENUMANAGER_RESTARTLEVEL</c>.</summary>
    RestartLevel = 12,

    /// <summary>Leave the menus and start play. <c>MENUMANAGER_STARTGAME</c>.</summary>
    StartGame = 13,

    /// <summary>Open the pending popup, taking its data in, then fade it in.</summary>
    ActivatePopup = 14,

    /// <summary>The popup has gone: the screen takes input again, fully faded in.</summary>
    PopupGone = 15,

    /// <summary>The menu is disabled: nothing runs.</summary>
    Disabled = 16,

    /// <summary>The menu is enabled again: <see cref="Idle"/> on the next tick.</summary>
    Enabled = 17,

    /// <summary>Open the pending screen, taking its data in, already faded in. I.G.I. 2's alone.</summary>
    EnterScreenShown = 18,

    /// <summary>Leave the menus and join a network game. I.G.I. 2's alone, <c>MENUMANAGER_JOINGAME</c>.</summary>
    JoinGame = 19,
}

/// <summary>What a game's menu manager does differently with the shared state machine.</summary>
/// <param name="Statuses">The states in the order the game numbers them: a script's constant indexes it.</param>
/// <param name="LeavePopsStack">Whether leaving the menus goes back a pushed screen first, while there is one.</param>
/// <param name="StackDepth">The most screens the stack holds; a push past it changes the screen without saving the old one.</param>
public sealed record MenuManagerDialect(IReadOnlyList<MenuManagerState> Statuses, bool LeavePopsStack, int StackDepth)
{
    /// <summary>Gets Project I.G.I.'s manager (<c>0x418080</c>): its states in their own order, its stack left alone by a leave.</summary>
    public static MenuManagerDialect Igi1 { get; } = new(
        [.. Enumerable.Range(0, (int)MenuManagerState.Enabled + 1).Select(static v => (MenuManagerState)v)],
        LeavePopsStack: false,
        StackDepth: int.MaxValue);

    /// <summary>
    /// Gets I.G.I. 2's manager (menumanager.c): an already-shown activation after the two others and the join after the start, so
    /// every later state numbered one or two higher; <c>MenuManager_LeaveMenues</c> pops a pushed screen before it leaves.
    /// </summary>
    public static MenuManagerDialect Igi2 { get; } = new(
        [
            MenuManagerState.EnterScreen, MenuManagerState.ReturnToScreen, MenuManagerState.EnterScreenShown, MenuManagerState.Active,
            MenuManagerState.Inactive, MenuManagerState.ChangeLanguage, MenuManagerState.FadingIn, MenuManagerState.FadingOut,
            MenuManagerState.PopupFadingIn, MenuManagerState.PopupFadingOut, MenuManagerState.Idle, MenuManagerState.Quit,
            MenuManagerState.QuitToMainMenu, MenuManagerState.RestartLevel, MenuManagerState.StartGame, MenuManagerState.JoinGame,
            MenuManagerState.ActivatePopup, MenuManagerState.PopupGone, MenuManagerState.Disabled, MenuManagerState.Enabled,
        ],
        LeavePopsStack: true,
        StackDepth: 64);

    /// <summary>Gets the state a script value names.</summary>
    /// <param name="value">The value, as the game numbers it.</param>
    /// <returns>The state; <see cref="MenuManagerState.Disabled"/> for a value the game does not number.</returns>
    public MenuManagerState StateOf(int value) =>
        value >= 0 && value < Statuses.Count ? Statuses[value] : MenuManagerState.Disabled;

    /// <summary>Gets the value the game numbers a state with.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The value; -1 for a state the game does not have.</returns>
    public int ValueOf(MenuManagerState state)
    {
        for (int i = 0; i < Statuses.Count; i++)
        {
            if (Statuses[i] == state)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>What a tick of the state machine asks of the menu that owns it.</summary>
public enum MenuStep
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Open <see cref="MenuNavigator.CurrentScreen"/>, taking its data in.</summary>
    ActivateScreen,

    /// <summary>Open <see cref="MenuNavigator.CurrentScreen"/> without taking its data in.</summary>
    ReturnToScreen,

    /// <summary>The open screen takes its text again in the new language.</summary>
    ChangeLanguage,

    /// <summary>Open <see cref="MenuNavigator.PopupScreen"/>, taking its data in.</summary>
    ActivatePopup,

    /// <summary>A fade out has finished, and any popup with it: <see cref="MenuNavigator.State"/> is the state it led to.</summary>
    FadedOut,

    /// <summary>The menus ask the game for <see cref="MenuNavigator.Left"/>.</summary>
    Leave,
}
