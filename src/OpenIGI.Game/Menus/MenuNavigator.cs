namespace OpenIGI.Game.Menus;

/// <summary>
/// The manager's screen stack and the state machine that moves between screens.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from the four navigation natives — <c>MenuManager_PushScreen</c> (<c>0x417690</c>),
/// <c>MenuManager_PopScreen</c> (<c>0x417710</c>), <c>MenuManager_RequestScreen</c> (<c>0x4174A0</c>)
/// and the popup pair — together with the fade branches of <c>MenuManager_Update</c>
/// (<c>0x418080</c>).
/// </para>
/// <para>
/// All three navigation natives do the same three things: set the pending screen, set the state to
/// return to once the fade completes, and drop into <see cref="MenuManagerState.FadingOut"/>. The
/// screen does not change until the fade reaches zero. What separates them is only what they do to
/// the stack:
/// </para>
/// <list type="bullet">
/// <item><description><b>Push</b> saves the current screen and returns with <see cref="MenuManagerState.EnterScreen"/>.</description></item>
/// <item><description><b>Pop</b> restores the saved one and returns with <see cref="MenuManagerState.ReturnToScreen"/>.</description></item>
/// <item><description><b>Request</b> replaces the current screen and touches the stack not at all — so a
/// screen reached by request cannot be popped back out of.</description></item>
/// </list>
/// </remarks>
public sealed class MenuNavigator
{
    /// <summary>The alpha the fade runs between; the manager treats 16 as fully faded in.</summary>
    public const int FullAlpha = 16;

    /// <summary>The amount the fade moves per tick.</summary>
    /// <remarks>The original subtracts <c>2 - (flag != 0)</c>, so a flag halves the speed; the flag is unread.</remarks>
    public const int FadeStep = 2;

    private readonly List<int> _stack = [];

    /// <summary>Gets the screen the manager is on, or about to move to.</summary>
    public int CurrentScreen { get; private set; }

    /// <summary>Gets the state the manager is in.</summary>
    public MenuManagerState State { get; private set; } = MenuManagerState.EnterScreen;

    /// <summary>Gets the state the manager enters once a fade-out completes.</summary>
    public MenuManagerState ReturnState { get; private set; }

    /// <summary>Gets the fade alpha, between zero and <see cref="FullAlpha"/>.</summary>
    public int Alpha { get; private set; }

    /// <summary>Gets the depth of the saved-screen stack.</summary>
    public int Depth => _stack.Count;

    /// <summary>Gets the saved screens, oldest first.</summary>
    public IReadOnlyList<int> SavedScreens => _stack;

    /// <summary>Gets a value indicating whether a popup is showing over the current screen.</summary>
    public bool PopupActive { get; private set; }

    /// <summary>Gets the popup screen, meaningful only while <see cref="PopupActive"/>.</summary>
    public int PopupScreen { get; private set; }

    /// <summary>Starts the manager on a screen, fully faded in.</summary>
    /// <param name="screen">The screen id.</param>
    public void Start(int screen)
    {
        CurrentScreen = screen;
        State = MenuManagerState.Active;
        Alpha = FullAlpha;
        _stack.Clear();
    }

    /// <summary>Saves the current screen and moves to another. <c>MenuManager_PushScreen</c>.</summary>
    /// <param name="screen">The screen id to move to.</param>
    public void Push(int screen)
    {
        _stack.Add(CurrentScreen);
        CurrentScreen = screen;
        ReturnState = MenuManagerState.EnterScreen;
        State = MenuManagerState.FadingOut;
    }

    /// <summary>Returns to the saved screen. <c>MenuManager_PopScreen</c>.</summary>
    /// <param name="reset">Whether to reset the screen's data, which the native's argument selects.</param>
    /// <returns><see langword="false"/> when the stack was empty, which leaves the manager where it is.</returns>
    /// <remarks>
    /// The original decrements its depth unconditionally and would read below the base of the stack;
    /// refusing is the one place this diverges, and it cannot change behaviour on data that balances
    /// its pushes and pops.
    /// </remarks>
    public bool Pop(bool reset)
    {
        _ = reset;

        if (_stack.Count == 0)
        {
            return false;
        }

        CurrentScreen = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        ReturnState = MenuManagerState.ReturnToScreen;
        State = MenuManagerState.FadingOut;
        return true;
    }

    /// <summary>Replaces the current screen without touching the stack. <c>MenuManager_RequestScreen</c>.</summary>
    /// <param name="screen">The screen id to move to.</param>
    /// <param name="reset">Whether to reset the screen's data, which the native's second argument selects.</param>
    public void Request(int screen, bool reset)
    {
        _ = reset;

        CurrentScreen = screen;
        ReturnState = MenuManagerState.EnterScreen;
        State = MenuManagerState.FadingOut;
    }

    /// <summary>Shows a popup over the current screen. <c>MenuManager_ActivatePopupScreen</c>.</summary>
    /// <param name="screen">The popup screen id.</param>
    public void ActivatePopup(int screen)
    {
        PopupScreen = screen;
        PopupActive = true;
        ReturnState = MenuManagerState.EnterPopup;
        State = MenuManagerState.FadingOut;
    }

    /// <summary>Dismisses the popup. <c>MenuManager_DeactivatePopuScreen</c>.</summary>
    /// <remarks>The original's spelling of the native is <c>DeactivatePopuScreen</c>, missing a <c>p</c>.</remarks>
    public void DeactivatePopup()
    {
        PopupActive = false;
        State = MenuManagerState.LeavePopup;
    }

    /// <summary>Advances the fade by one tick.</summary>
    /// <returns><see langword="true"/> when the state changed.</returns>
    /// <remarks>
    /// Fading out runs the alpha to zero and then enters <see cref="ReturnState"/>; fading in runs it
    /// to <see cref="FullAlpha"/> and then goes <see cref="MenuManagerState.Active"/>. The screen
    /// itself changes at the bottom of the fade, which is why a menu never cuts.
    /// </remarks>
    public bool Tick()
    {
        switch (State)
        {
            case MenuManagerState.FadingOut or MenuManagerState.PopupFadingOut:
                if (Alpha > 0)
                {
                    Alpha = Math.Max(0, Alpha - FadeStep);
                }

                if (Alpha == 0)
                {
                    State = ReturnState;
                    return true;
                }

                return false;

            case MenuManagerState.FadingIn or MenuManagerState.PopupFadingIn:
                if (Alpha < FullAlpha)
                {
                    Alpha = Math.Min(FullAlpha, Alpha + FadeStep);
                }

                if (Alpha == FullAlpha)
                {
                    State = MenuManagerState.Active;
                    return true;
                }

                return false;

            case MenuManagerState.EnterScreen or MenuManagerState.ReturnToScreen or MenuManagerState.EnterPopup:
                State = MenuManagerState.FadingIn;
                return true;

            case MenuManagerState.LeavePopup:
                State = MenuManagerState.PopupFadingIn;
                return true;

            case MenuManagerState.EnterImmediate:
                Alpha = FullAlpha;
                State = MenuManagerState.Active;
                return true;

            case MenuManagerState.ReturnToIdle:
                State = MenuManagerState.Idle;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Gets a value indicating whether the manager is asking the host to leave the menus.</summary>
    public bool IsLeaving =>
        State is MenuManagerState.Quit
            or MenuManagerState.QuitToMainMenu
            or MenuManagerState.RestartLevel
            or MenuManagerState.StartGame;

    /// <summary>Puts the manager into one of the leave states.</summary>
    /// <param name="state">The state, which must be one <see cref="IsLeaving"/> recognises.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a leave state.</exception>
    public void Leave(MenuManagerState state)
    {
        if (state is not (MenuManagerState.Quit
            or MenuManagerState.QuitToMainMenu
            or MenuManagerState.RestartLevel
            or MenuManagerState.StartGame))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Not a leave state.");
        }

        State = state;
    }
}
