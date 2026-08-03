using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend;

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
    /// <summary>The delay added per control for the staggered entrance, in seconds.</summary>
    public const float EntranceStagger = 0.04f;

    private readonly Tween _transition = new(0f, duration: 0.35f, Ease.OutQuint);
    private UiVec2 _builtViewport = new(-1f, -1f);

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

    /// <summary>Builds the widget tree, then lays it out for the current viewport.</summary>
    /// <param name="context">The layout context for this frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public void Prepare(LayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Rebuild only when the viewport changes: the tree is retained across frames so animation, focus
        // and scroll survive, and a relayout each frame keeps it correct without discarding that state.
        bool rebuilt = false;
        if (Root is null || _builtViewport != context.Viewport)
        {
            Root = BuildRoot(context);
            Focus = new FocusManager(Root);
            _builtViewport = context.Viewport;
            rebuilt = true;
            OnBuilt();
        }

        Root.Measure(context, context.SafeRect.Size);
        Root.Arrange(context.SafeRect);

        if (rebuilt)
        {
            Focus!.FocusFirst();

            // Animate the controls in only on a real entrance; a rebuild from a resize while the screen
            // is already up snaps them present so the layout does not replay its animation.
            ApplyEntrance(Phase == ScreenPhase.Entering && !_transition.IsComplete);
        }

        Focus!.Validate();
    }

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

        int index = 0;
        foreach (UiElement element in Root.Descendants())
        {
            if (element is Control control)
            {
                if (animate)
                {
                    control.PlayEnter(index * EntranceStagger);
                    index++;
                }
                else
                {
                    control.Visual.AppearInstantly();
                }
            }
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

        OnUpdate(elapsedSeconds);
        return popRequested;
    }

    /// <summary>Draws the screen through a painter.</summary>
    /// <param name="painter">The painter to draw into.</param>
    public void Draw(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);

        // The transition amount becomes a screen-wide fade, so entering and exiting screens cross-fade
        // without every widget needing to know about it.
        painter.PushOpacity(Transition);
        OnDrawBackground(painter);
        Root?.Draw(painter);
        OnDrawForeground(painter);
        painter.PopOpacity();
    }

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

        if (nav.Direction is { } direction)
        {
            // The focused control gets first refusal; only if it declines does focus move.
            if (focus.Focused is Control control && control.HandleNavigate(direction))
            {
                // handled by the control
            }
            else
            {
                focus.Move(direction);
            }
        }

        if (nav.Accept && focus.Focused is Control activated)
        {
            activated.Activate();
        }

        return nav.Back;
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
