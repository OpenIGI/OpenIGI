using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.Frontend;

/// <summary>
/// The shell's navigation spine: a stack of <see cref="Screen"/>s with animated push and pop, where the
/// screen on top is the one that takes input and the ones beneath stay drawn for the transition.
/// </summary>
/// <remarks>
/// <para>
/// Pushing a screen covers the current one and animates the new one in; input is locked to nothing until
/// the entrance finishes, so a press during the slide never lands on a half-shown screen. Popping fades
/// the top out while the screen it uncovers becomes live at once, so the player is never waiting on an
/// exit animation to act. A screen requests its own pop by returning a back press it did not otherwise
/// handle, which is what makes the B button walk back up the stack everywhere without each screen wiring
/// it.
/// </para>
/// <para>
/// One <see cref="UiNavigation"/> reader serves the whole stack, so key-repeat and the stick dead zone
/// behave identically on every screen. The stack does not touch <see cref="InputState.Captured"/>; the
/// composition root sets that after the shell has read the frame, the same contract the console uses.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The type is deliberately a stack of screens; 'ScreenStack' is the domain term the spec uses.")]
public sealed class ScreenStack
{
    private readonly List<Screen> _screens = [];
    private readonly UiNavigation _navigation = new();

    /// <summary>Gets the number of screens currently on the stack, including one that is exiting.</summary>
    public int Count => _screens.Count;

    /// <summary>Gets the screen currently taking input, or <see langword="null"/> when the stack is empty.</summary>
    public Screen? Top => TopIndex >= 0 ? _screens[TopIndex] : null;

    /// <summary>Gets the navigation reader shared by every screen.</summary>
    public UiNavigation Navigation => _navigation;

    /// <summary>Pushes a screen, covering the current top and animating the new one in.</summary>
    /// <param name="screen">The screen to push.</param>
    /// <param name="animate">Whether to animate the entrance; <see langword="false"/> snaps it present.</param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> is <see langword="null"/>.</exception>
    public void Push(Screen screen, bool animate = true)
    {
        ArgumentNullException.ThrowIfNull(screen);
        Top?.Cover();
        _screens.Add(screen);

        if (animate)
        {
            screen.BeginEnter();
        }
        else
        {
            screen.SnapPresent();
        }

        _navigation.Reset();
    }

    /// <summary>Replaces the whole stack with a single screen.</summary>
    /// <param name="screen">The screen to make the only one.</param>
    /// <param name="animate">Whether to animate the entrance.</param>
    public void Reset(Screen screen, bool animate = true)
    {
        _screens.Clear();
        Push(screen, animate);
    }

    /// <summary>Begins popping the top screen; the screen it uncovers becomes live at once.</summary>
    /// <returns><see langword="false"/> when there is nothing to pop or only one screen remains.</returns>
    public bool Pop()
    {
        int leavingIndex = TopIndex;
        if (leavingIndex <= 0)
        {
            return false; // never pop the last screen out from under the player
        }

        // Capture the index before BeginExit: once the top is marked Exiting, TopIndex moves to the
        // screen below, so re-reading it here would start the uncover search from the wrong place.
        _screens[leavingIndex].BeginExit();

        // Uncover the screen beneath so it takes input immediately, rather than after the fade.
        for (int i = leavingIndex - 1; i >= 0; i--)
        {
            if (_screens[i].Phase != ScreenPhase.Exiting)
            {
                _screens[i].Uncover();
                break;
            }
        }

        _navigation.Reset();
        return true;
    }

    /// <summary>Lays out, animates and drives every present screen for a frame.</summary>
    /// <param name="context">The layout context for this frame.</param>
    /// <param name="input">The frame's input.</param>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Update(LayoutContext context, InputState input, float elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        _navigation.Update(input, elapsedSeconds);

        foreach (Screen screen in _screens)
        {
            screen.Prepare(context);
        }

        // Only the live top screen is handed navigation; the rest advance their animation with a neutral
        // reader so a covered or exiting screen keeps settling without stealing input.
        int top = TopIndex;
        for (int i = 0; i < _screens.Count; i++)
        {
            Screen screen = _screens[i];
            bool popRequested = i == top
                ? screen.Update(elapsedSeconds, _navigation, input)
                : screen.Update(elapsedSeconds, NeutralNavigation, input);

            if (popRequested && i == top)
            {
                Pop();
            }
        }

        _screens.RemoveAll(s => s.IsFinishedExiting);
    }

    /// <summary>Draws every present screen from the bottom of the stack up.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="painter"/> is <see langword="null"/>.</exception>
    public void Draw(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        foreach (Screen screen in _screens)
        {
            screen.Draw(painter);
        }
    }

    private int TopIndex
    {
        get
        {
            // The topmost screen that is not on its way out is the one in charge.
            for (int i = _screens.Count - 1; i >= 0; i--)
            {
                if (_screens[i].Phase != ScreenPhase.Exiting)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    // A reader that reports nothing, for advancing covered screens without giving them input.
    private static UiNavigation NeutralNavigation { get; } = new();
}
