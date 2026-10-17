using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Screens;

/// <summary>Where a screen is in its enter/exit transition.</summary>
public enum ScreenPhase
{
    /// <summary>Animating in.</summary>
    Entering,

    /// <summary>Fully shown and interactive.</summary>
    Active,

    /// <summary>Shown but covered by a screen above; not interactive.</summary>
    Covered,

    /// <summary>Animating out, on its way off the stack.</summary>
    Exiting,
}

/// <summary>
/// One page of the cinematic shell: a widget tree, its own focus, and an enter/exit transition, driven
/// by the <see cref="ScreenStack"/>.
/// </summary>
/// <remarks>
/// <para>
/// A screen builds its widget tree once (rebuilding only when the viewport changes), lays it out each
/// frame against the current <see cref="LayoutContext"/> so it stays correct as the window resizes and
/// as aspect ratios change, and draws it through a <see cref="UiPainter"/>. Input arrives already folded
/// into navigation intents: the screen offers a pressed direction to the focused control first — a
/// slider steers, a tab strip switches — and only if the control declines does focus move.
/// </para>
/// <para>
/// The transition is a single <c>[0,1]</c> amount on real elapsed time: 1 is fully present, 0 is gone.
/// The stack fades and (optionally) offsets a screen by it, and a subclass reads <see cref="Transition"/>
/// to choreograph its own entrance — a title sliding in, rows cascading with a stagger.
/// </para>
/// </remarks>
public abstract class Screen
{
    /// <summary>
    /// Gets whether this screen is capturing the keyboard press queue for something other than
    /// navigation, so the stack must not drain Enter/Space/arrows first.
    /// </summary>
    public virtual bool CapturesQueuedNavigation => false;

    /// <summary>The delay added per control for the staggered entrance, in seconds.</summary>
    public const float EntranceStagger = 0.04f;

    /// <summary>The longest a control ever waits for its turn in the staggered entrance, in seconds.</summary>
    /// <remarks>
    /// Without a ceiling the delay is the control's index times the stagger, so a settings page with a
    /// hundred bound rows left the last of them invisible for four seconds — and, because the top bar is
    /// built after the page content, took the bar's captions with it. A third of a second is long enough
    /// for the cascade to read and short enough that nothing is ever missing.
    /// </remarks>
    public const float MaxEntranceDelay = 0.30f;

    /// <summary>How long the pointer has to rest on a control before its tooltip appears, in seconds.</summary>
    /// <remarks>
    /// Long enough that running the pointer down a list of settings shows nothing, short enough that
    /// stopping on one feels answered rather than waited on.
    /// </remarks>
    private const float TooltipDwellSeconds = 0.5f;

    /// <summary>How long the tooltip takes to fade up once the wait is over, in seconds.</summary>
    private const float TooltipFadeSeconds = 0.12f;

    private readonly Tween _transition = new(0f, duration: 0.35f, Ease.OutQuint);
    private UiVec2 _builtViewport = new(-1f, -1f);
    private Control? _hovered;

    // The control holding the pointer through a drag, or null; see Control.CapturesPointer.
    private Control? _captured;
    private Control? _tooltipTarget;
    private UiVec2 _tooltipAnchor;
    private float _tooltipDwell;

    /// <summary>Gets the root of the screen's widget tree, valid after the first <see cref="Prepare"/>.</summary>
    public UiElement? Root { get; private set; }

    /// <summary>Gets the screen's focus manager, valid after the first <see cref="Prepare"/>.</summary>
    public FocusManager? Focus { get; private set; }

    /// <summary>Gets the screen's phase in its transition.</summary>
    public ScreenPhase Phase { get; private set; } = ScreenPhase.Entering;

    /// <summary>Gets how present the screen is, 0 gone to 1 fully shown, on real time.</summary>
    public float Transition => _transition.Value;

    /// <summary>Gets a value indicating whether an exiting screen has fully faded and can be removed.</summary>
    public bool IsFinishedExiting => Phase == ScreenPhase.Exiting && _transition.IsComplete && _transition.Value <= 0f;

    /// <summary>Gets or sets a title for diagnostics and tests.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the control whose tooltip is currently showing, or <see langword="null"/> for none.</summary>
    /// <remarks>
    /// Non-null only once the pointer has rested on a control carrying a tooltip for the dwell. Reading it
    /// is how anything other than the draw can tell whether the page is currently explaining itself.
    /// </remarks>
    public Control? TooltipControl =>
        _tooltipDwell >= TooltipDwellSeconds && _tooltipTarget is { Tooltip.Length: > 0 } target ? target : null;

    /// <summary>Builds the widget tree, then lays it out for the current viewport.</summary>
    /// <param name="context">The layout context for this frame.</param>
    /// <param name="sounds">Where its feedback sounds go; the stack's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public void Prepare(LayoutContext context, IUiSoundSink? sounds = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Rebuild only when the viewport changes: the tree is retained across frames so animation, focus
        // and scroll survive, and a relayout each frame keeps it correct without discarding that state.
        bool rebuilt = false;
        if (Root is null || _builtViewport != context.Viewport)
        {
            Root = BuildRoot(context);
            Root.Sounds = sounds;
            Focus = new FocusManager(Root);
            _builtViewport = context.Viewport;
            rebuilt = true;
            OnBuilt();
        }

        Root.Measure(context, context.SafeRect.Size);
        Root.Arrange(context.SafeRect);
        LayoutRequested = false;

        if (rebuilt)
        {
            if (InitialFocus is { CanFocus: true } initial)
            {
                Focus!.Focus(initial);
            }
            else
            {
                Focus!.FocusFirst();
            }

            // Animate the controls in only on a real entrance; a rebuild from a resize while the screen
            // is already up snaps them present so the layout does not replay its animation.
            ApplyEntrance(Phase == ScreenPhase.Entering && !_transition.IsComplete);
        }

        Focus!.Validate();
    }

    /// <summary>Gets whether the screen changed its tree after this frame's layout, and must be laid out again before it is drawn.</summary>
    public bool LayoutRequested { get; private set; }

    /// <summary>Asks for the tree to be laid out again before this frame is drawn.</summary>
    /// <remarks>
    /// The frame lays out first and handles input after, so anything the input changes (a page shown or hidden,
    /// rows added) would otherwise be drawn once with last frame's positions before the next layout corrects it:
    /// a flash of content in the wrong place. A screen that changes its tree from <c>HandleInput</c> or
    /// <c>OnUpdate</c> calls this, and the stack lays it out again ahead of the draw.
    /// </remarks>
    protected void RequestLayout() => LayoutRequested = true;

    /// <summary>Asks for the tree to be rebuilt on the next <see cref="Prepare"/>, for a page whose content has changed shape.</summary>
    /// <remarks>Focus returns to the first control, and nothing replays its entrance.</remarks>
    protected void Invalidate() => _builtViewport = new UiVec2(-1f, -1f);

    /// <summary>Begins the entrance transition.</summary>
    public void BeginEnter()
    {
        Phase = ScreenPhase.Entering;
        _transition.Retarget(1f);
        OnEnter();
    }

    /// <summary>Snaps the screen fully present without animating, e.g. for the boot screen.</summary>
    public void SnapPresent()
    {
        Phase = ScreenPhase.Active;
        _transition.SnapTo(1f);
        OnEnter();
    }

    /// <summary>Marks the screen covered by one above it; it stays drawn but stops taking input.</summary>
    public void Cover() => Phase = ScreenPhase.Covered;

    /// <summary>Restores an uncovered screen to active once the screen above it has left.</summary>
    public void Uncover()
    {
        Phase = ScreenPhase.Active;
        Focus?.FocusFirst();
    }

    private void ApplyEntrance(bool animate)
    {
        if (Root is null)
        {
            return;
        }

        UiElement? chrome = Chrome;
        int index = 0;
        foreach (UiElement element in Root.Descendants())
        {
            if (element is not Control control)
            {
                continue;
            }

            // The chrome belongs to the shell, not to this page, so it is already there: it never plays
            // an entrance and never waits its turn behind the page's own controls.
            if (!animate || (chrome is not null && control.IsWithin(chrome)))
            {
                control.Visual.AppearInstantly();
                continue;
            }

            control.PlayEnter(MathF.Min(index * EntranceStagger, MaxEntranceDelay));
            index++;
        }
    }

    /// <summary>Begins the exit transition.</summary>
    public void BeginExit()
    {
        Phase = ScreenPhase.Exiting;
        _transition.Retarget(0f);
        OnExit();
    }

    /// <summary>Advances the screen's animation and, when active, its input.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    /// <param name="nav">The frame's navigation intents.</param>
    /// <param name="input">The frame's raw input, for anything beyond navigation.</param>
    /// <returns><see langword="true"/> when the screen asked to be popped (a back press it did not handle).</returns>
    public bool Update(float elapsedSeconds, UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        _transition.Update(elapsedSeconds);
        Root?.Update(elapsedSeconds);

        if (_transition.IsComplete && Phase == ScreenPhase.Entering)
        {
            Phase = ScreenPhase.Active;
        }

        bool popRequested = false;
        if (Phase == ScreenPhase.Active)
        {
            popRequested = HandleInput(nav, input);
        }

        UpdateTooltip(elapsedSeconds, input);
        OnUpdate(elapsedSeconds);
        return popRequested;
    }

    /// <summary>Times how long the pointer has rested where it is, which is what a tooltip waits on.</summary>
    /// <remarks>
    /// Any movement at all restarts the wait, and moves the anchor with it: a tooltip is the answer to
    /// stopping on something, not to passing over it, and it has to appear where the pointer came to rest
    /// rather than where it entered the control. A covered or exiting screen stops counting, so the page
    /// underneath a transition never explains itself.
    /// </remarks>
    private void UpdateTooltip(float elapsedSeconds, InputState input)
    {
        MouseState mouse = input.Mouse;
        bool moved = mouse.DeltaX != 0f || mouse.DeltaY != 0f;

        if (moved || !ReferenceEquals(_hovered, _tooltipTarget) || Phase != ScreenPhase.Active)
        {
            _tooltipTarget = Phase == ScreenPhase.Active ? _hovered : null;
            _tooltipAnchor = new UiVec2(mouse.X, mouse.Y);
            _tooltipDwell = 0f;
            return;
        }

        if (_tooltipTarget is { Tooltip.Length: > 0 })
        {
            _tooltipDwell += elapsedSeconds;
        }
    }

    /// <summary>Draws the screen through a painter.</summary>
    /// <param name="painter">The painter to draw into.</param>
    public void Draw(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);

        // The chrome is the same frame on every page, so fading it in with whichever page happens to own
        // it is what made switching tabs flash the whole bar out and back. It is skipped inside the
        // transition and drawn after it at full opacity — a page changes underneath a bar that does not
        // move. A screen on its way out has no chrome to keep, so it fades as one.
        UiElement? chrome = Phase == ScreenPhase.Exiting ? null : Chrome;
        Visibility restore = chrome?.Visibility ?? Visibility.Visible;
        if (chrome is not null)
        {
            chrome.Visibility = Visibility.Hidden; // still laid out, simply not drawn in this pass
        }

        // The transition amount becomes a screen-wide fade, so entering and exiting screens cross-fade
        // without every widget needing to know about it.
        painter.PushOpacity(Transition);
        OnDrawBackground(painter);
        Root?.Draw(painter);
        OnDrawForeground(painter);
        painter.PopOpacity();

        if (chrome is not null)
        {
            chrome.Visibility = restore;
            chrome.Draw(painter);
        }

        // Last, and outside the screen's fade: a tooltip is an overlay on the whole page. Drawn from
        // inside the widget tree it would be clipped by whatever viewport its owner scrolls in, and would
        // be painted over by every sibling that came after it.
        DrawTooltip(painter);
    }

    /// <summary>Draws the resting control's tooltip near the pointer, once the wait is over.</summary>
    private void DrawTooltip(UiPainter painter)
    {
        if (_tooltipDwell < TooltipDwellSeconds || _tooltipTarget is not { Tooltip.Length: > 0 } target)
        {
            return;
        }

        LayoutContext context = painter.Context;
        UiTheme theme = target.Theme;
        float sizePx = context.Px(theme.BodyTextSize * 0.9f);
        float pad = context.Px(12f);

        // Wrapped to a readable measure rather than to the pointer's room, so the box is the same shape
        // wherever on the page it opens.
        string text = theme.Body.WrapText(target.Tooltip, sizePx, context.Px(360f));
        UiVec2 size = theme.Body.Measure(text, sizePx);
        float width = size.X + (pad * 2f);
        float height = size.Y + (pad * 2f);

        // Clear of the ROW, not of the pointer. Offsetting from the pointer put the box inside the control
        // whenever the pointer was in its upper half — a settings row is taller than any offset worth
        // using — so the thing it was explaining was covered by the explanation, highlight and value and
        // all. Vertically it clears the control's own box; horizontally it follows the pointer, which is
        // what keeps it near what the eye is already on.
        float gapX = context.Px(18f);
        float gapY = context.Px(8f);
        UiRect row = target.Bounds;
        UiVec2 viewport = context.Viewport;

        float x = _tooltipAnchor.X + gapX;
        if (x + width > viewport.X)
        {
            x = MathF.Max(0f, _tooltipAnchor.X - gapX - width);
        }

        // Under the row, or over it when the page has run out of room below — never across it.
        float y = row.Bottom + gapY;
        if (y + height > viewport.Y)
        {
            y = MathF.Max(0f, row.Y - gapY - height);
        }

        var box = new UiRect(x, y, width, height);
        float appear = Math.Clamp((_tooltipDwell - TooltipDwellSeconds) / TooltipFadeSeconds, 0f, 1f);

        painter.PushOpacity(appear);

        // Near-opaque, unlike the page's panels: this one is read at a glance over whatever it lands on,
        // so it cannot afford to let the picture behind it through.
        painter.FillRect(box, theme.Panel.WithAlpha(246));
        painter.StrokeRect(box, context.Px(1f), theme.Accent.WithAlpha(110));
        painter.DrawText(theme.Body, text, new UiVec2(x + pad, y + pad), sizePx, theme.Text);
        painter.PopOpacity();
    }

    /// <summary>Gets the control focus starts on when the tree is built, or <see langword="null"/> for the first one.</summary>
    /// <remarks>Read after <see cref="OnBuilt"/>, so a page can point it at a control it has just made.</remarks>
    protected virtual Control? InitialFocus => null;

    /// <summary>Gets the part of the tree that is shell chrome rather than page content.</summary>
    /// <remarks>
    /// Chrome is drawn outside the enter/exit fade and never plays the staggered entrance, because it is
    /// the same object on the page being left and the page being arrived at. The shell's header and footer
    /// are the chrome; a screen with none returns <see langword="null"/> and fades as a whole.
    /// </remarks>
    protected virtual UiElement? Chrome => null;

    /// <summary>Builds the screen's widget tree.</summary>
    /// <param name="context">The layout context, for sizing decisions made at build time.</param>
    /// <returns>The root element.</returns>
    protected abstract UiElement BuildRoot(LayoutContext context);

    /// <summary>Handles a frame's navigation while the screen is active.</summary>
    /// <param name="nav">The navigation intents.</param>
    /// <param name="input">The raw input.</param>
    /// <returns><see langword="true"/> to request the screen be popped.</returns>
    /// <remarks>
    /// The default routes accept to the focused control's activation, offers a direction to the focused
    /// control before moving focus, and treats an unhandled back as a pop request. Screens that need more
    /// — a tab strip that owns left/right at the top level — override this.
    /// </remarks>
    protected virtual bool HandleInput(UiNavigation nav, InputState input)
    {
        if (Focus is not { } focus)
        {
            return nav.Back;
        }

        // Text is decoded by the platform, not inferred from scan codes.  Only the focused field drains
        // the queue, so opening a profile form cannot leak its name into another screen or the console.
        if (focus.Focused is TextBox textBox)
        {
            while (input.Keyboard.TryDequeueCharacter(out char typed))
            {
                textBox.Append(typed);
            }

            if (input.Keyboard.WasPressed(KeyCode.Backspace))
            {
                textBox.Backspace();
            }
        }

        MouseState mouse = input.Mouse;
        var pointer = new UiVec2(mouse.X, mouse.Y);

        // A drag in progress owns the pointer until the button comes up: no hover, no focus-follows-
        // pointer, no activation. Back abandons it rather than leaving the screen.
        if (_captured is { } held)
        {
            if (nav.Back)
            {
                _captured = null;
                held.PointerReleased(pointer, cancelled: true);
                return false;
            }

            if (mouse.IsDown(MouseButton.Left))
            {
                held.PointerDragged(pointer);
            }
            else
            {
                _captured = null;
                held.PointerReleased(pointer, cancelled: false);
            }

            if (mouse.WheelDelta != 0f)
            {
                ScrollUnderPointer(pointer, mouse.WheelDelta);
            }

            return false;
        }

        // Hover: the control under the pointer is told the pointer is on it (and where), so a tab strip or
        // a list can light the one target the click is about to land on rather than the whole widget.
        UpdateHover(pointer);

        // Moving the pointer focuses whatever is under it, a left click activates it at that point. Only
        // on a real move, so the pointer resting on a row does not fight keyboard or gamepad focus.
        if (mouse.DeltaX != 0f || mouse.DeltaY != 0f)
        {
            focus.FocusAt(pointer);
        }

        if (mouse.WasPressed(MouseButton.Left) && focus.FocusAt(pointer) is Control clicked)
        {
            if (clicked.IsEnabled && clicked.CapturesPointer(pointer))
            {
                _captured = clicked;
            }
            else
            {
                clicked.ActivateAt(pointer);
            }
        }

        // The wheel belongs to whichever viewport the pointer is inside, not to the focused widget — the
        // reading every desktop interface has.
        if (mouse.WheelDelta != 0f)
        {
            ScrollUnderPointer(pointer, mouse.WheelDelta);
        }

        if (nav.Direction is { } direction)
        {
            // The focused control gets first refusal; only if it declines does focus move.
            if (focus.Focused is Control control && control.HandleNavigate(direction))
            {
                // handled by the control
            }
            else if (MoveFocus(direction))
            {
                RevealFocused();
            }
        }

        if (nav.Accept && focus.Focused is Control activated)
        {
            activated.Activate();
        }

        return nav.Back;
    }

    /// <summary>Gets a value indicating whether a control is holding the pointer through a drag.</summary>
    /// <remarks>A screen that adds its own keys checks this, so a drag is not interrupted by a page change.</remarks>
    protected bool IsPointerCaptured => _captured is not null;

    /// <summary>Moves focus in a direction, by default by spatial traversal.</summary>
    /// <param name="direction">The direction pressed.</param>
    /// <returns><see langword="true"/> when focus moved.</returns>
    /// <remarks>
    /// Overridden by a screen whose widgets have an order the geometry does not express — a form dealt into
    /// columns, where the foot of one column leads to the head of the next and nothing on screen says so.
    /// Left and right cannot carry that, because the controls consume them for their own values.
    /// </remarks>
    protected virtual bool MoveFocus(NavDirection direction) => Focus?.Move(direction) ?? false;

    /// <summary>Scrolls the viewport the newly focused widget is inside so that it is on screen.</summary>
    /// <remarks>
    /// This is what makes the keyboard and the pad scroll a long settings panel: focus walks down the rows
    /// as usual, and the viewport follows it. A screen calls it after moving focus by any means.
    /// </remarks>
    protected void RevealFocused()
    {
        if (Focus?.Focused is UiElement focused
            && focused.FindAncestor<ScrollView>() is { } viewport)
        {
            viewport.EnsureVisible(focused.Bounds);
        }
    }

    private void UpdateHover(UiVec2 pointer)
    {
        // Through the focus model rather than the tree walk: a widget may answer for a region outside its
        // own layout box — an open menu's list — and the walk descends only into elements whose bounds
        // contain the point, so it would never reach one. The walk is still the fallback for anything the
        // focus model does not know about.
        Control? hovered = Focus?.At(pointer) as Control
            ?? Root?.HitTest(pointer)?.FindAncestor<Control>();
        if (!ReferenceEquals(hovered, _hovered))
        {
            _hovered?.SetPointerOver(false);
            _hovered = hovered;
            _hovered?.SetPointerOver(true);
        }

        hovered?.PointerMoved(pointer);
    }

    private void ScrollUnderPointer(UiVec2 pointer, float notches)
    {
        // The innermost viewport under the pointer gets the wheel; if it has nothing left to scroll the
        // one enclosing it takes over, so a nested list does not swallow the page's own scrolling.
        for (UiElement? element = Root?.HitTest(pointer); element is not null; element = element.Parent)
        {
            if (element is IWheelScrollable viewport && viewport.ScrollByWheel(notches))
            {
                return;
            }
        }
    }

    /// <summary>Called once after the widget tree is (re)built.</summary>
    protected virtual void OnBuilt()
    {
    }

    /// <summary>Called when the entrance begins.</summary>
    protected virtual void OnEnter()
    {
    }

    /// <summary>Called when the exit begins.</summary>
    protected virtual void OnExit()
    {
    }

    /// <summary>Called each frame after animation and input.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last update.</param>
    protected virtual void OnUpdate(float elapsedSeconds)
    {
    }

    /// <summary>Draws behind the widget tree — a backdrop or vignette.</summary>
    /// <param name="painter">The painter to draw into.</param>
    protected virtual void OnDrawBackground(UiPainter painter)
    {
    }

    /// <summary>Draws over the widget tree — a footer hint bar, a transition wipe.</summary>
    /// <param name="painter">The painter to draw into.</param>
    protected virtual void OnDrawForeground(UiPainter painter)
    {
    }
}
