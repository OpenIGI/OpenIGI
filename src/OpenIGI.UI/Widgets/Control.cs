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

    /// <summary>Gets the visual state machine driving the control's animation.</summary>
    public WidgetVisualStateMachine Visual { get; } = new();

    /// <summary>Gets a value indicating whether the control can take focus at all when enabled.</summary>
    protected virtual bool IsFocusable => true;

    /// <inheritdoc/>
    public UiRect FocusBounds => Bounds;

    /// <inheritdoc/>
    public bool CanFocus =>
        IsFocusable && IsEnabled && Visibility == Visibility.Visible && !Bounds.IsEmpty;

    /// <inheritdoc/>
    public void SetFocused(bool focused) => Visual.SetFocused(focused && IsEnabled);

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
            OnActivate();
            Activated?.Invoke(this);
        }
    }

    /// <summary>Called when the control is activated, before <see cref="Activated"/> is raised.</summary>
    protected virtual void OnActivate()
    {
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds) => Visual.Update(elapsedSeconds);
}
