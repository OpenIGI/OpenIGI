
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
    /// <param name="save">
    /// Whether to commit the screen's controls before leaving it, which the native's argument selects.
    /// </param>
    /// <returns><see langword="false"/> when the stack was empty, which leaves the manager where it is.</returns>
    /// <remarks>
    /// <para>
    /// <b>Correction.</b> The argument was read here as "reset the screen's data" and ignored. It is
    /// the opposite: <c>MenuManager_PopScreen</c> (<c>0x417710</c>) opens with
    /// <c>if (Arg_GetInt(args, 0)) MenuManager_SaveScreenData(manager)</c>, so it decides whether the
    /// controls are written back through their <c>Set Data Script</c>s. It is the OK-versus-Cancel
    /// distinction, and dropping it made every options screen commit.
    /// </para>
    /// <para>
    /// The original decrements its depth unconditionally and would read below the base of the stack;
    /// refusing is the one place this diverges, and it cannot change behaviour on data that balances
    /// its pushes and pops.
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
        ReturnState = MenuManagerState.ReturnToScreen;
        State = MenuManagerState.FadingOut;
        return true;
    }

    /// <summary>Replaces the current screen without touching the stack. <c>MenuManager_RequestScreen</c>.</summary>
    /// <param name="screen">The screen id to move to.</param>
    /// <param name="save">
    /// Whether to commit the current screen's controls before leaving it, as in <see cref="Pop"/>.
    /// <c>MenuManager_RequestScreen</c> (<c>0x4174A0</c>) tests its <em>second</em> argument for this
    /// and takes the target screen from its first.
    /// </param>
    public void Request(int screen, bool save)
    {
        if (save)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        CurrentScreen = screen;
        ReturnState = MenuManagerState.EnterScreen;
        State = MenuManagerState.FadingOut;
    }

    /// <summary>Raised when a navigation asks for the current screen to be committed first.</summary>
    /// <remarks>
    /// The manager answers by running the <c>0x424880</c> slot over the screen's subtree, which is what
    /// writes each control back through its <c>Set Data Script</c>.
    /// </remarks>
    public event EventHandler? SaveRequested;

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

    /// <summary>Marks the leave as delivered, so it is acted on once rather than every tick.</summary>
    /// <remarks>
    /// A leave state is a <em>request</em>: the manager hands it to the host and the menus are done. It
    /// has to be cleared, because <see cref="Tick"/> has no transition out of one — while a leave that
    /// quits hid that (the process was going away anyway), one that starts a mission does not, and the
    /// mission loaded again on every tick.
    /// </remarks>
    public void CompleteLeave()
    {
        if (IsLeaving)
        {
            State = MenuManagerState.Halted;
        }
    }

    /// <summary>Gets a value indicating whether the manager is asking the host to leave the menus.</summary>
    public bool IsLeaving =>
        State is MenuManagerState.Quit
            or MenuManagerState.QuitToMainMenu
            or MenuManagerState.RestartLevel
            or MenuManagerState.StartGame;

    /// <summary>Gets the value the script passed alongside the leave state.</summary>
    /// <remarks>
    /// The original stores it on the manager (<c>+32</c>) next to the pending state. Retail scripts pass
    /// <c>-1</c> when starting a game and <c>0</c> when quitting; nothing observed reads it back, so it
    /// is carried rather than interpreted.
    /// </remarks>
    public int LeaveParameter { get; private set; }

    /// <summary>Starts leaving the menus, fading out first.</summary>
    /// <param name="state">The state to enter once the fade completes.</param>
    /// <param name="parameter">The value the script passed alongside it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a leave state.</exception>
    /// <remarks>
    /// <c>MenuManager_LeaveMenus</c> (<c>0x417790</c>) does not enter the leave state directly: it sets
    /// the manager's state to <c>6</c> — <see cref="MenuManagerState.FadingOut"/> — and parks the
    /// requested state beside it, so the menus fade before play begins. That is the same
    /// <see cref="ReturnState"/> mechanism a screen push uses.
    /// </remarks>
    public void Leave(MenuManagerState state, int parameter = 0)
    {
        if (state is not (MenuManagerState.Quit
            or MenuManagerState.QuitToMainMenu
            or MenuManagerState.RestartLevel
            or MenuManagerState.StartGame))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Not a leave state.");
        }

        LeaveParameter = parameter;
        ReturnState = state;
        State = MenuManagerState.FadingOut;
    }
}
