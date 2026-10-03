using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.Input;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A row binding an action to a key or mouse button: it shows the current assignment, and on activation
/// listens for the next supported control press to rebind it.
/// </summary>
/// <remarks>
/// Activating the row puts it into a listening state that shows a prompt; the owning screen, seeing
/// <see cref="IsListening"/>, feeds it the next key press through <see cref="TryCaptureKey"/> (and an
/// escape to cancel). While listening it consumes every navigation direction so a stray stick nudge does
/// not move focus away mid-rebind. This is the Controls settings screen's row.
/// </remarks>
public class KeyBindingRow : Control
{
    private readonly Tween _pulse = new(0f, duration: 0.6f, Ease.InOutQuad);
    private string? _shownName;
    private string _shownCap = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="KeyBindingRow"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="action">The action name shown on the left.</param>
    /// <param name="boundKey">The initially bound key.</param>
    /// <param name="displayName">The player-facing binding name, including a mouse button when applicable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public KeyBindingRow(UiTheme theme, string action, KeyCode boundKey, string? displayName = null)
        : base(theme)
    {
        Action = action ?? string.Empty;
        BoundKey = boundKey;
        BoundKeyName = displayName ?? KeyName(boundKey);
    }

    /// <summary>Raised when the bound key changes, with the new key.</summary>
    public event Action<KeyCode>? BindingChanged;

    /// <summary>Raised when the action is rebound to a mouse button.</summary>
    public event Action<MouseButton>? MouseBindingChanged;

    /// <summary>Raised when the action is rebound to a mouse-wheel notch: +1 up, -1 down.</summary>
    public event Action<int>? MouseWheelBindingChanged;

    /// <summary>Gets or sets the action name.</summary>
    public string Action { get; set; }

    /// <summary>Gets the currently bound key.</summary>
    public KeyCode BoundKey { get; private set; }

    /// <summary>Gets the player-facing name of the current binding.</summary>
    /// <remarks>A retail mouse binding has no <see cref="KeyCode"/>, but still needs an honest label.</remarks>
    public string BoundKeyName { get; private set; }

    /// <summary>Refreshes the displayed assignment without raising <see cref="BindingChanged"/>.</summary>
    /// <param name="key">The keyboard key, or <see cref="KeyCode.None"/> for a non-keyboard binding.</param>
    /// <param name="displayName">The player-facing assignment name.</param>
    public void SetBinding(KeyCode key, string displayName)
    {
        BoundKey = key;
        BoundKeyName = displayName ?? string.Empty;
    }

    /// <summary>Gets a value indicating whether the row is waiting for a key to bind.</summary>
    public bool IsListening { get; private set; }

    /// <summary>Feeds a key press to a listening row, binding it, or cancels on escape.</summary>
    /// <param name="key">The key pressed.</param>
    /// <returns><see langword="true"/> when the row consumed the press.</returns>
    public bool TryCaptureKey(KeyCode key)
    {
        if (!IsListening)
        {
            return false;
        }

        IsListening = false;
        if (key == KeyCode.Escape)
        {
            return true; // cancelled, binding unchanged
        }

        if (key != BoundKey)
        {
            BoundKey = key;
            BoundKeyName = KeyName(key);
            BindingChanged?.Invoke(key);
        }

        return true;
    }

    /// <summary>Feeds a mouse-button press to a listening row.</summary>
    /// <param name="button">The button pressed.</param>
    /// <returns><see langword="true"/> when the row consumed the press.</returns>
    public bool TryCaptureMouseButton(MouseButton button)
    {
        if (!IsListening)
        {
            return false;
        }

        IsListening = false;
        BoundKey = KeyCode.None;
        BoundKeyName = $"Mouse Button {(int)button + 1}";
        MouseBindingChanged?.Invoke(button);
        return true;
    }

    /// <summary>Feeds mouse-wheel motion to a listening row, binding the scrolled direction.</summary>
    /// <param name="delta">This frame's wheel motion in notches: positive is up.</param>
    /// <returns><see langword="true"/> when the row consumed the motion.</returns>
    public bool TryCaptureMouseWheel(float delta)
    {
        if (!IsListening || delta == 0f)
        {
            return false;
        }

        IsListening = false;
        int direction = delta > 0f ? 1 : -1;
        BoundKey = KeyCode.None;
        BoundKeyName = direction > 0 ? "Mouse Wheel Up" : "Mouse Wheel Down";
        MouseWheelBindingChanged?.Invoke(direction);
        return true;
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction) => IsListening;

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        IsListening = true;
        _pulse.SnapTo(0f);
        _pulse.Retarget(1f);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (IsListening)
        {
            // Ping-pong the pulse so the prompt breathes while waiting.
            _pulse.Update(elapsedSeconds);
            if (_pulse.IsComplete)
            {
                _pulse.Retarget(_pulse.Value >= 0.5f ? 0f : 1f);
            }
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        UiRowStyle.Measure(this, context, available);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float highlight = UiRowStyle.Highlight(this);

        // The binding is a key cap, the same one the footer's hints use, so a binding reads as the key it
        // is rather than as a word.
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float sizePx = context.Px(13f);
        if (!ReferenceEquals(_shownName, BoundKeyName))
        {
            _shownName = BoundKeyName;
            _shownCap = BoundKeyName.ToUpperInvariant();
        }

        string value = IsListening ? "PRESS A KEY…" : _shownCap;
        float textWidth = UiPainter.MeasureLabel(Theme.Mono, value, sizePx, 0.08f);
        float padX = context.Px(10f);
        float height = context.Px(28f);
        float right = Bounds.Right - context.Px(UiRowStyle.Inset);
        var cap = new UiRect(right - textWidth - (padX * 2f), Bounds.Y + ((Bounds.Height - height) * 0.5f), textWidth + (padX * 2f), height);
        UiRowStyle.DrawFrame(painter, this, Action, highlight, cap.X);

        Bgra32 border = IsListening
            ? Interpolate.Lerp(UiPalette.AccentAt(0.35f), Theme.Accent, _pulse.Value)
            : Interpolate.Lerp(UiPalette.KeyBorder, UiPalette.AccentAt(0.45f), highlight);
        if (IsListening)
        {
            painter.FillRect(cap, UiPalette.Fade(UiPalette.AccentAt(0.08f), appear));
        }

        painter.StrokeRect(cap, context.Px(1f), UiPalette.Fade(border, appear));
        Bgra32 text = !IsEnabled ? Theme.TextDisabled : IsListening ? Theme.Accent : Theme.Text;
        painter.DrawLabel(
            Theme.Mono,
            value,
            new UiVec2(cap.X + padX, UiRowStyle.CenterY(Theme.Mono, sizePx, cap)),
            sizePx,
            UiPalette.Fade(text, appear),
            0.08f);
    }

    private static string KeyName(KeyCode key) => key.ToString();
}
