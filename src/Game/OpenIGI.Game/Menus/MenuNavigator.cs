namespace OpenIGI.Game.Menus;

/// <summary>
/// The menu manager's state machine: its screen stack, its popup, its fades, and the states its natives set.
/// </summary>
/// <remarks>
/// <para>
/// Both games' managers run it. Project I.G.I.'s is <c>MenuManager_Update</c> (<c>0x418080</c>) with the natives its constructor
/// (<c>0x417260</c>) registers; I.G.I. 2's is <c>MenuManager_RunHandler</c> with the same natives (menumanager.c). What differs is
/// in <see cref="MenuManagerDialect"/>.
/// </para>
/// <para>
/// The navigation natives all do the same three things: set the pending screen, set the state to return to once the fade
/// completes, and drop into <see cref="MenuManagerState.FadingOut"/>. The screen does not change until the fade reaches zero.
/// What separates them is what they do to the stack:
/// </para>
/// <list type="bullet">
/// <item><description><b>Push</b> saves the current screen and returns with <see cref="MenuManagerState.EnterScreen"/>.</description></item>
/// <item><description><b>Pop</b> restores the saved one and returns with <see cref="MenuManagerState.ReturnToScreen"/>, so the screen
/// returned to keeps what its controls held rather than taking its data in again.</description></item>
/// <item><description><b>Request</b> replaces the current screen and touches the stack not at all, so a screen reached by request
/// cannot be popped back out of.</description></item>
/// </list>
/// <para>
/// A popup is opened without a fade out: <c>MenuManager_ActivatePopupScreen</c> enters <see cref="MenuManagerState.ActivatePopup"/>
/// directly, and the popup fades in over the screen. Closing it fades the popup out to <see cref="MenuManagerState.PopupGone"/>.
/// </para>
/// </remarks>
public sealed class MenuNavigator
{
    /// <summary>The alpha the fade runs between; the manager treats 16 as fully faded in.</summary>
    public const int FullAlpha = 16;

    /// <summary>The amount the fade moves per tick.</summary>
    /// <remarks>The original subtracts <c>2 - (flag != 0)</c>, so a flag halves the speed; the flag is unread.</remarks>
    public const int FadeStep = 2;

    private readonly List<int> _stack = [];

    /// <summary>Initializes a new instance of the <see cref="MenuNavigator"/> class.</summary>
    /// <param name="dialect">The game's manager; Project I.G.I.'s when <see langword="null"/>.</param>
    public MenuNavigator(MenuManagerDialect? dialect = null) => Dialect = dialect ?? MenuManagerDialect.Igi1;

    /// <summary>Raised when a native asks for the open screen's data to be given back before it leaves.</summary>
    /// <remarks>
    /// The manager answers by running the change-data event over the screen's subtree (<c>0x424880</c> in Project I.G.I.,
    /// <c>MenuManager_ReadDataFromActiveWindow</c> in I.G.I. 2), which writes each control back through its <c>Set Data Script</c>.
    /// </remarks>
    public event EventHandler? SaveRequested;

    /// <summary>Gets the game's manager this machine runs as.</summary>
    public MenuManagerDialect Dialect { get; }

    /// <summary>Gets the screen the manager is on, or about to move to.</summary>
    public int CurrentScreen { get; private set; }

    /// <summary>Gets the state the manager is in.</summary>
    public MenuManagerState State { get; private set; } = MenuManagerState.EnterScreen;

    /// <summary>Gets the state the manager enters once a fade-out completes.</summary>
    public MenuManagerState ReturnState { get; private set; }

    /// <summary>Gets the screen's fade alpha, between zero and <see cref="FullAlpha"/>.</summary>
    public int Alpha { get; private set; }

    /// <summary>Gets the popup's fade alpha, between zero and <see cref="FullAlpha"/>.</summary>
    public int PopupAlpha { get; private set; }

    /// <summary>Gets the depth of the saved-screen stack.</summary>
    public int Depth => _stack.Count;

    /// <summary>Gets the saved screens, oldest first.</summary>
    public IReadOnlyList<int> SavedScreens => _stack;

    /// <summary>Gets a value indicating whether a popup is asked for or showing over the current screen.</summary>
    public bool PopupActive { get; private set; }

    /// <summary>Gets the popup screen, meaningful only while <see cref="PopupActive"/>.</summary>
    public int PopupScreen { get; private set; } = -1;

    /// <summary>Gets the state the menus last asked the game for, with <see cref="MenuStep.Leave"/>.</summary>
    public MenuManagerState Left { get; private set; }

    /// <summary>Gets a value indicating whether the manager is asking the game to leave the menus.</summary>
    public bool IsLeaving =>
        State is MenuManagerState.Quit
            or MenuManagerState.QuitToMainMenu
            or MenuManagerState.RestartLevel
            or MenuManagerState.StartGame
            or MenuManagerState.JoinGame;

    /// <summary>Starts on a screen already shown and taking input, as the front end's menus begin.</summary>
    /// <param name="screen">The screen.</param>
    public void Start(int screen)
    {
        Reset(screen, MenuManagerState.Active);
        Alpha = FullAlpha;
        PopupAlpha = FullAlpha;
    }

    /// <summary>Puts the machine back to a state with nothing stacked and no popup, as a manager is made.</summary>
    /// <param name="screen">The screen it names.</param>
    /// <param name="state">The state, also the one a fade would return to.</param>
    public void Reset(int screen, MenuManagerState state)
    {
        CurrentScreen = screen;
        (State, ReturnState) = (state, state);
        (Alpha, PopupAlpha) = (0, 0);
        _stack.Clear();
        ClosePopup();
    }

    /// <summary>Opens the menus on a screen, taking its data in, as Escape does while the game plays.</summary>
    /// <param name="screen">The screen.</param>
    public void Open(int screen)
    {
        CurrentScreen = screen;
        State = MenuManagerState.EnterScreen;
    }

    /// <summary>Saves the current screen and moves to another, <c>MenuManager_PushScreen</c> (<c>0x417690</c>).</summary>
    /// <param name="screen">The screen to move to.</param>
    public void Push(int screen)
    {
        if (_stack.Count < Dialect.StackDepth)
        {
            _stack.Add(CurrentScreen);
        }

        CurrentScreen = screen;
        FadeOutTo(MenuManagerState.EnterScreen);
    }

    /// <summary>Returns to the screen saved last, <c>MenuManager_PopScreen</c> (<c>0x417710</c>).</summary>
    /// <param name="save">
    /// Whether to commit the screen's controls before leaving it, which the native's argument selects.
    /// </param>
    /// <returns><see langword="false"/> when nothing is saved.</returns>
    /// <remarks>
    /// <para>
    /// <b>Correction.</b> The argument was read here as "reset the screen's data" and ignored. It is the opposite:
    /// <c>MenuManager_PopScreen</c> opens with <c>if (Arg_GetInt(args, 0)) MenuManager_SaveScreenData(manager)</c>, so it decides
    /// whether the controls are written back through their <c>Set Data Script</c>s. It is the OK-versus-Cancel distinction, and
    /// dropping it made every options screen commit.
    /// </para>
    /// <para>
    /// Both games decrement the depth unconditionally and would read below the base of the stack; refusing is the one place this
    /// diverges, and it cannot change behaviour on data that balances its pushes and pops.
    /// </para>
    /// </remarks>
    public bool Pop(bool save)
    {
        if (save)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_stack.Count == 0)
        {
            return false;
        }

        CurrentScreen = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        FadeOutTo(MenuManagerState.ReturnToScreen);
        return true;
    }

    /// <summary>Replaces the current screen, <c>MenuManager_RequestScreen</c> (<c>0x4174A0</c>).</summary>
    /// <param name="screen">The screen to move to.</param>
    /// <param name="save">
    /// Whether to commit the current screen's controls before leaving it, as in <see cref="Pop"/>. The native tests its
    /// <em>second</em> argument for this and takes the target screen from its first.
    /// </param>
    public void Request(int screen, bool save)
    {
        if (save)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        CurrentScreen = screen;
        FadeOutTo(MenuManagerState.EnterScreen);
    }

    /// <summary>Empties the stack, I.G.I. 2's <c>MenuManager_ClearStack</c>.</summary>
    public void ClearStack() => _stack.Clear();

    /// <summary>Opens a popup over the current screen, <c>MenuManager_ActivatePopupScreen</c> (<c>0x417900</c>).</summary>
    /// <param name="screen">The popup.</param>
    /// <remarks>
    /// <b>Correction.</b> This faded the screen out first and returned through state 4. The native sets the popup and enters
    /// <see cref="MenuManagerState.ActivatePopup"/> directly; the screen stays as it is beneath the popup's own fade.
    /// </remarks>
    public void ActivatePopup(int screen)
    {
        PopupScreen = screen;
        PopupActive = true;
        State = MenuManagerState.ActivatePopup;
    }

    /// <summary>Fades the popup out, <c>MenuManager_DeactivatePopuScreen</c> (<c>0x417960</c>).</summary>
    /// <remarks>
    /// The original's spelling of the native is <c>DeactivatePopuScreen</c>, missing a <c>p</c>. The popup goes at the bottom of
    /// its fade, then <see cref="MenuManagerState.PopupGone"/> gives the screen its input back.
    /// </remarks>
    public void DeactivatePopup()
    {
        (State, ReturnState) = (MenuManagerState.PopupFadingOut, MenuManagerState.PopupGone);
    }

    /// <summary>Fades out to change the menus' language, <c>MenuManager_SetLanguage</c> (<c>0x417810</c>).</summary>
    public void SetLanguage() => FadeOutTo(MenuManagerState.ChangeLanguage);

    /// <summary>Starts leaving the menus, fading out first, <c>MenuManager_LeaveMenus</c> (<c>0x417790</c>).</summary>
    /// <param name="screen">The screen the manager names as it leaves, the native's first argument.</param>
    /// <param name="next">The state to enter once the fade completes.</param>
    /// <param name="save">Whether to commit the current screen's controls first, I.G.I. 2's third argument.</param>
    /// <param name="levelFailed">Whether the level has failed, which keeps the menus open rather than going idle.</param>
    /// <remarks>
    /// <para>
    /// The native does not enter the state directly: it fades out and parks the state beside it, the same
    /// <see cref="ReturnState"/> a push uses. A level that has failed refuses <see cref="MenuManagerState.Idle"/>, so its menu
    /// cannot be closed back into the game.
    /// </para>
    /// <para>
    /// I.G.I. 2's <c>MenuManager_LeaveMenues</c> goes back a pushed screen first, while there is one, and leaves only from the
    /// bottom of the stack.
    /// </para>
    /// </remarks>
    public void Leave(int screen, MenuManagerState next, bool save = false, bool levelFailed = false)
    {
        if (save)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        if (Dialect.LeavePopsStack && _stack.Count > 0)
        {
            CurrentScreen = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            FadeOutTo(MenuManagerState.ReturnToScreen);
            return;
        }

        if (levelFailed && next == MenuManagerState.Idle)
        {
            return;
        }

        CurrentScreen = screen;
        FadeOutTo(next);
    }

    /// <summary>Marks a leave as delivered, so it is acted on once rather than every tick.</summary>
    /// <remarks>
    /// The original asks its flow for the leave on every tick it stays in the state, which its flow takes once. A front end that
    /// acts on the leave itself has to be told only once: the handler for <see cref="MenuManagerState.StartGame"/> loads a whole
    /// mission. The manager is disabled instead.
    /// </remarks>
    public void CompleteLeave()
    {
        if (IsLeaving)
        {
            State = MenuManagerState.Disabled;
        }
    }

    /// <summary>Takes the machine a tick, the state switch of <c>MenuManager_Update</c>.</summary>
    /// <returns>What the menu that owns the machine has to do.</returns>
    /// <remarks>
    /// Opening a screen resets the fade and fades in; fading in reaches <see cref="MenuManagerState.Active"/>; fading out enters
    /// <see cref="ReturnState"/> at zero and takes any popup with it (<c>0x418940</c>); a leave asks the game on every tick, and the
    /// main-menu and restart leaves go idle at once.
    /// </remarks>
    public MenuStep Tick()
    {
        switch (State)
        {
            case MenuManagerState.EnterScreen or MenuManagerState.ReturnToScreen:
                MenuStep opened = State == MenuManagerState.EnterScreen ? MenuStep.ActivateScreen : MenuStep.ReturnToScreen;
                (Alpha, State) = (0, MenuManagerState.FadingIn);
                return opened;
            case MenuManagerState.EnterScreenShown:
                (Alpha, State) = (FullAlpha, MenuManagerState.FadingIn);
                return MenuStep.ActivateScreen;
            case MenuManagerState.ChangeLanguage:
                (Alpha, State) = (0, MenuManagerState.FadingIn);
                return MenuStep.ChangeLanguage;
            case MenuManagerState.FadingIn:
                Alpha = Math.Min(FullAlpha, Alpha + FadeStep);
                if (Alpha == FullAlpha)
                {
                    State = MenuManagerState.Active;
                }

                return MenuStep.None;
            case MenuManagerState.PopupFadingIn:
                PopupAlpha = Math.Min(FullAlpha, PopupAlpha + FadeStep);
                if (PopupAlpha == FullAlpha)
                {
                    State = MenuManagerState.Active;
                }

                return MenuStep.None;
            case MenuManagerState.FadingOut:
                Alpha = Math.Max(0, Alpha - FadeStep);
                return Alpha == 0 ? FadedOut() : MenuStep.None;
            case MenuManagerState.PopupFadingOut:
                PopupAlpha = Math.Max(0, PopupAlpha - FadeStep);
                return PopupAlpha == 0 ? FadedOut() : MenuStep.None;
            case MenuManagerState.ActivatePopup:
                (PopupAlpha, State) = (0, MenuManagerState.PopupFadingIn);
                return MenuStep.ActivatePopup;
            case MenuManagerState.PopupGone:
                (Alpha, PopupAlpha, State) = (FullAlpha, FullAlpha, MenuManagerState.Active);
                return MenuStep.None;
            case MenuManagerState.Enabled:
                State = MenuManagerState.Idle;
                return MenuStep.None;
            case MenuManagerState.Quit or MenuManagerState.StartGame or MenuManagerState.JoinGame:
                Left = State;
                return MenuStep.Leave;
            case MenuManagerState.QuitToMainMenu or MenuManagerState.RestartLevel:
                (Left, State) = (State, MenuManagerState.Idle);
                return MenuStep.Leave;
            default:
                return MenuStep.None;
        }
    }

    private MenuStep FadedOut()
    {
        State = ReturnState;
        ClosePopup();
        return MenuStep.FadedOut;
    }

    private void FadeOutTo(MenuManagerState next) => (State, ReturnState) = (MenuManagerState.FadingOut, next);

    private void ClosePopup()
    {
        PopupActive = false;
        PopupScreen = -1;
    }
}
