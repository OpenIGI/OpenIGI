using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// The base of every interactive widget: focusable, enable-able, carrying the
/// <see cref="WidgetVisualStateMachine"/> that animates its enter / idle / focused / pressed look.
/// </summary>
/// <remarks>
/// <para>
/// A control receives input in two steps the screen drives. When it holds focus it first gets a chance
/// to consume a direction through <see cref="HandleNavigate"/> — a slider steers its value, a tab strip
/// changes tab — and only if it does not does the <see cref="FocusManager"/> move focus away. Pressing
/// accept, or clicking, calls <see cref="Activate"/>, which raises <see cref="Activated"/>.
/// </para>
/// <para>
/// The visuals never snap: focus, press and appearance are the three continuous amounts of the state
/// machine, advanced on real time in <see cref="UiElement.OnUpdate"/>, so a widget eases between states
/// even as focus flicks quickly across a list.
/// </para>
/// </remarks>
public abstract class Control : UiElement, IFocusable
{
    /// <summary>Initializes a new instance of the <see cref="Control"/> class.</summary>
    /// <param name="theme">The theme the control draws from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    protected Control(UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
    }

    /// <summary>Raised when the control is activated by accept or a click.</summary>
    public event Action<Control>? Activated;

    /// <summary>Gets the theme the control draws from.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets a value indicating whether the control responds to input.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets what to explain when the pointer rests on the control; empty for nothing.</summary>
    /// <remarks>
    /// The control only carries the text. Where and when it appears belongs to the screen, which is the
    /// only thing that knows the whole page — a tooltip has to be drawn over every other widget and must
    /// not be clipped by the viewport its owner happens to be scrolling inside. Line feeds are honoured,
    /// so a first line can state what the setting is and a later one can add a caveat.
    /// </remarks>
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>Gets or sets a short line of explanation a row shows under its caption, or an empty string for none.</summary>
    public string Hint { get; set; } = string.Empty;

    /// <summary>Gets the visual state machine driving the control's animation.</summary>
    public WidgetVisualStateMachine Visual { get; } = new();

    /// <summary>Gets a value indicating whether the control can take focus at all when enabled.</summary>
    protected virtual bool IsFocusable => true;

    /// <summary>Gets a value indicating whether the pointer is currently over the control.</summary>
    /// <remarks>Set by the screen that routes the pointer; a widget reads it to light a hovered row or tab.</remarks>
    public bool IsPointerOver { get; private set; }

    /// <inheritdoc/>
    public UiRect FocusBounds => Bounds;

    /// <inheritdoc/>
    /// <remarks>
    /// Overridden by a widget that draws outside its own layout box and expects clicks there — a menu that
    /// opens a list below itself. Everything else is exactly where it was laid out.
    /// </remarks>
    public virtual UiRect PointerBounds => VisibleBounds;

    /// <inheritdoc/>
    public bool CanFocus =>
        IsFocusable && IsEnabled && Visibility == Visibility.Visible && !Bounds.IsEmpty;

    /// <inheritdoc/>
    public void SetFocused(bool focused) => Visual.SetFocused(focused && IsEnabled);

    /// <summary>Tells the control whether the pointer is over it.</summary>
    /// <param name="over">Whether the pointer is inside the control.</param>
    public void SetPointerOver(bool over)
    {
        if (IsPointerOver == over)
        {
            return;
        }

        IsPointerOver = over;
        OnPointerOverChanged(over);
    }

    /// <summary>Tells the control where the pointer is while it is over it.</summary>
    /// <param name="point">The pointer position in pixels.</param>
    /// <remarks>
    /// Only a widget that is several targets in one — a tab strip, a list of rows — needs this; a button
    /// is wholly one target and ignores it.
    /// </remarks>
    public virtual void PointerMoved(UiVec2 point)
    {
    }

    /// <summary>Activates the control from a click at a point.</summary>
    /// <param name="point">Where the pointer was clicked, in pixels.</param>
    /// <remarks>
    /// The default ignores the point and behaves exactly like <see cref="Activate"/>, which is right for a
    /// button. A widget whose surface holds several targets overrides it so that clicking a particular tab
    /// selects <em>that</em> tab, a click on a slider's track jumps to that value, and a click on a row
    /// picks that row — the behaviour a pointer expects and that accept-on-the-whole-widget cannot give.
    /// </remarks>
    public virtual void ActivateAt(UiVec2 point) => Activate();

    /// <summary>Sets whether the control is being pressed, for its press animation.</summary>
    /// <param name="pressed">Whether the control is pressed.</param>
    public void SetPressed(bool pressed) => Visual.SetPressed(pressed && IsEnabled);

    /// <summary>Plays the control's entrance animation.</summary>
    /// <param name="delaySeconds">A stagger delay before it begins.</param>
    public void PlayEnter(float delaySeconds = 0f) => Visual.Enter(delaySeconds);

    /// <summary>Offers a navigation direction to the control before focus would move.</summary>
    /// <param name="direction">The direction pressed.</param>
    /// <returns><see langword="true"/> when the control consumed it and focus should stay.</returns>
    public virtual bool HandleNavigate(NavDirection direction) => false;

    /// <summary>Activates the control, raising <see cref="Activated"/>.</summary>
    public virtual void Activate()
    {
        if (IsEnabled)
        {
            UiSounds.Chosen();
            OnActivate();
            Activated?.Invoke(this);
        }
    }

    /// <summary>Raises <see cref="Activated"/> without running <see cref="OnActivate"/>.</summary>
    /// <remarks>
    /// For a widget whose <see cref="ActivateAt"/> has already done the work the activation stands for —
    /// a dropdown that stepped to the option the pointer clicked — so the listeners still hear about it
    /// without the default step happening a second time.
    /// </remarks>
    protected void RaiseActivated()
    {
        if (IsEnabled)
        {
            UiSounds.Chosen();
            Activated?.Invoke(this);
        }
    }

    /// <summary>Called when the control is activated, before <see cref="Activated"/> is raised.</summary>
    protected virtual void OnActivate()
    {
    }

    /// <summary>Called when the pointer enters or leaves the control.</summary>
    /// <param name="over">Whether the pointer is now inside the control.</param>
    protected virtual void OnPointerOverChanged(bool over)
    {
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds) => Visual.Update(elapsedSeconds);
}
