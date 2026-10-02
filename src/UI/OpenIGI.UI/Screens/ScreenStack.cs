using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Screens;

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
    private UiVec2 _pointer;
    private bool _pointerVisible;

    /// <summary>Gets or sets a value indicating whether something in front is drawing the pointer instead.</summary>
    /// <remarks>
    /// Exactly one thing may draw a cursor. The developer console is drawn after the shell and over it,
    /// so while it is up it owns the pointer; without this the reader sees two arrows, which is what
    /// happens the moment two layers each decide they are the topmost.
    /// </remarks>
    public bool PointerSuppressed { get; set; }

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
        // Screens leaving this way get their exit hooks: clearing without them skips e.g.
        // SettingsScreen's profile save when leaving via the top tabs (issue #230), and leaves the
        // campaign and extras screens subscribed to the cheat variables. One already on its way out
        // has run its hook and must not run it twice.
        foreach (Screen leaving in _screens)
        {
            if (leaving.Phase != ScreenPhase.Exiting)
            {
                leaving.BeginExit();
            }
        }

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

        bool consumeQueuedNavigation = TopIndex < 0
            || !_screens[TopIndex].CapturesQueuedNavigation;
        _navigation.Update(input, elapsedSeconds, consumeQueuedNavigation);

        // Track the pointer for the drawn cursor, and let the last input device decide whether it shows:
        // moving the mouse brings the cursor up, a directional or accept press from keyboard/pad hides it.
        _pointer = new UiVec2(input.Mouse.X, input.Mouse.Y);
        if (input.Mouse.DeltaX != 0f || input.Mouse.DeltaY != 0f)
        {
            _pointerVisible = true;
        }
        else if (_navigation.Direction is not null || _navigation.Accept)
        {
            _pointerVisible = false;
        }

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

        // A screen put on the stack from inside another screen's input — a tab press, which resets the
        // stack — arrives after the prepare pass above and has no widget tree yet. Preparing it here rather
        // than leaving it until the next frame is what stops the swap showing one frame of nothing: without
        // it, the whole shell blinked out and back every time a tab was clicked.
        //
        // The same goes for a screen whose input or update changed its own tree after this frame's layout (a tab
        // shown, a list refilled): it is laid out again here, or it would be drawn once at last frame's places.
        foreach (Screen screen in _screens)
        {
            if (screen.Root is null || screen.LayoutRequested)
            {
                screen.Prepare(context);
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

        if (_pointerVisible && !PointerSuppressed)
        {
            DrawCursor(painter);
        }
    }

    private void DrawCursor(UiPainter painter)
    {
        // A classic arrow: a white triangle over a slightly larger dark one for contrast, its tip at the
        // pointer. Sized gently with the viewport so it stays a sensible size from 480p to 4K.
        float size = Math.Clamp(painter.Context.Viewport.Y / 42f, 18f, 40f);
        DrawArrow(painter, _pointer, size + 3f, OpenIGI.Core.Graphics.Bgra32.FromRgba(0, 0, 0, 200));
        DrawArrow(painter, _pointer, size, OpenIGI.Core.Graphics.Bgra32.White);
    }

    private static void DrawArrow(UiPainter painter, UiVec2 tip, float size, OpenIGI.Core.Graphics.Bgra32 color)
    {
        var down = new UiVec2(tip.X, tip.Y + size);
        var elbow = new UiVec2(tip.X + (size * 0.36f), tip.Y + (size * 0.72f));
        var barb = new UiVec2(tip.X + (size * 0.72f), tip.Y + (size * 0.72f));
        painter.FillTriangle(tip, down, elbow, color);
        painter.FillTriangle(tip, elbow, barb, color);
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
