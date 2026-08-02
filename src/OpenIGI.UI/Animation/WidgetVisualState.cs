namespace OpenIGI.UI.Animation;

/// <summary>The lifecycle phase a widget's visuals are in.</summary>
public enum WidgetVisualState
{
    /// <summary>Animating in from nothing.</summary>
    Enter,

    /// <summary>Present and at rest, neither focused nor pressed.</summary>
    Idle,

    /// <summary>Present and holding focus.</summary>
    Focused,

    /// <summary>Present, focused, and being activated.</summary>
    Pressed,

    /// <summary>Animating out.</summary>
    Exit,
}

/// <summary>
/// The per-widget animation state machine: three continuous amounts — appear, focus, press — that a
/// widget renders from, driven by the discrete <see cref="WidgetVisualState"/> transitions.
/// </summary>
/// <remarks>
/// <para>
/// The five states of the specification map onto three overlapping tweens rather than one, because a
/// widget is often in two at once — a button can be fading in <em>while</em> gaining focus, or pressed
/// <em>while</em> focused. Decomposing into <see cref="Appear"/> (Enter/Exit), <see cref="Focus"/>
/// (Idle↔Focused) and <see cref="Press"/> (Focused↔Pressed) lets each run on its own curve and settle
/// independently, which is what makes the composited result read as one physical object rather than a
/// set of snapping states.
/// </para>
/// <para>
/// Every amount is in <c>[0,1]</c>; a widget scales, tints, offsets or glows in proportion to them.
/// Advanced with real elapsed time, never a simulation tick.
/// </para>
/// </remarks>
public sealed class WidgetVisualStateMachine
{
    private readonly Tween _appear = new(0f, duration: 0.28f, Ease.OutQuint);
    private readonly Tween _focus = new(0f, duration: 0.16f, Ease.OutCubic);
    private readonly Tween _press = new(0f, duration: 0.08f, Ease.OutQuad);

    private bool _focused;
    private bool _pressed;
    private bool _exiting;

    /// <summary>Gets how far the widget has animated in, 0 hidden to 1 fully present.</summary>
    public float Appear => _appear.Value;

    /// <summary>Gets how strongly the widget is focused, 0 to 1.</summary>
    public float Focus => _focus.Value;

    /// <summary>Gets how strongly the widget is pressed, 0 to 1.</summary>
    public float Press => _press.Value;

    /// <summary>Gets the discrete state the amounts currently represent.</summary>
    public WidgetVisualState State
    {
        get
        {
            if (_exiting)
            {
                return WidgetVisualState.Exit;
            }

            if (_appear.Value < 1f && !_focused && !_pressed)
            {
                return WidgetVisualState.Enter;
            }

            if (_pressed)
            {
                return WidgetVisualState.Pressed;
            }

            return _focused ? WidgetVisualState.Focused : WidgetVisualState.Idle;
        }
    }

    /// <summary>Gets a value indicating whether an exiting widget has fully faded and can be removed.</summary>
    public bool IsGone => _exiting && _appear.IsComplete && _appear.Value <= 0f;

    /// <summary>Begins the entrance animation, optionally after a stagger delay.</summary>
    /// <param name="delaySeconds">How long to wait before the widget starts appearing.</param>
    public void Enter(float delaySeconds = 0f)
    {
        _exiting = false;
        _appear.Retarget(1f, delay: delaySeconds);
    }

    /// <summary>Snaps the widget to fully present without playing the entrance.</summary>
    public void AppearInstantly()
    {
        _exiting = false;
        _appear.SnapTo(1f);
    }

    /// <summary>Begins the exit animation.</summary>
    /// <param name="delaySeconds">How long to wait before the widget starts leaving.</param>
    public void Exit(float delaySeconds = 0f)
    {
        _exiting = true;
        _focused = false;
        _pressed = false;
        _focus.Retarget(0f);
        _press.Retarget(0f);
        _appear.Retarget(0f, delay: delaySeconds);
    }

    /// <summary>Sets whether the widget currently holds focus.</summary>
    /// <param name="focused">Whether the widget is focused.</param>
    public void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
        _focus.Retarget(focused ? 1f : 0f);
        if (!focused && _pressed)
        {
            SetPressed(false);
        }
    }

    /// <summary>Sets whether the widget is being pressed.</summary>
    /// <param name="pressed">Whether the widget is pressed.</param>
    public void SetPressed(bool pressed)
    {
        if (_pressed == pressed)
        {
            return;
        }

        _pressed = pressed;
        _press.Retarget(pressed ? 1f : 0f);
    }

    /// <summary>Advances every amount by a real-time delta.</summary>
    /// <param name="elapsedSeconds">The seconds since the last update.</param>
    public void Update(float elapsedSeconds)
    {
        _appear.Update(elapsedSeconds);
        _focus.Update(elapsedSeconds);
        _press.Update(elapsedSeconds);
    }
}
