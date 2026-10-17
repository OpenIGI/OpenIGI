
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// The states <c>MenuManager_Update</c> switches on.
/// </summary>
/// <remarks>
/// Five of these are registered as script constants by the manager's constructor (<c>0x417260</c>) and
/// so appear by name in menu scripts; the rest are internal to the state machine at <c>0x418080</c>
/// and are named here from what each branch does.
/// </remarks>
public enum MenuManagerState
{
    /// <summary>Load the pending screen as a fresh entry, then fade in.</summary>
    EnterScreen = 0,

    /// <summary>Load the pending screen as a return from a pop, then fade in.</summary>
    ReturnToScreen = 1,

    /// <summary>Fully faded in and taking input.</summary>
    Active = 2,

    /// <summary>Faded in but not taking input.</summary>
    Inactive = 3,

    /// <summary>Activate the pending popup, then fade in.</summary>
    EnterPopup = 4,

    /// <summary>Fading in.</summary>
    FadingIn = 5,

    /// <summary>Fading out, after which the pending screen is applied.</summary>
    FadingOut = 6,

    /// <summary>Fading a popup in.</summary>
    PopupFadingIn = 7,

    /// <summary>Fading a popup out.</summary>
    PopupFadingOut = 8,

    /// <summary>The attract state the manager sits in outside the menus. <c>MENUMANAGER_IDLE</c>.</summary>
    Idle = 9,

    /// <summary>Leave the menus and quit. <c>MENUMANAGER_QUIT</c>.</summary>
    Quit = 10,

    /// <summary>Leave the current game and return to the main menu. <c>MENUMANAGER_QUITTOMAINMENU</c>.</summary>
    QuitToMainMenu = 11,

    /// <summary>Restart the current level. <c>MENUMANAGER_RESTARTLEVEL</c>.</summary>
    RestartLevel = 12,

    /// <summary>Leave the menus and start play. <c>MENUMANAGER_STARTGAME</c>.</summary>
    StartGame = 13,

    /// <summary>Deactivate the popup and fade back out to the screen beneath.</summary>
    LeavePopup = 14,

    /// <summary>Enter the menus fully faded in, skipping the fade.</summary>
    EnterImmediate = 15,

    /// <summary>Do nothing.</summary>
    Halted = 16,

    /// <summary>Return to <see cref="Idle"/> on the next tick.</summary>
    ReturnToIdle = 17,
}
