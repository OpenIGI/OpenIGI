using OpenIGI.Core.Graphics;
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
            _shownCap = Theme.Large ? FriendlyName(BoundKeyName) : BoundKeyName.ToUpperInvariant();
        }

        bool unbound = !IsListening && (_shownCap.Length == 0 || string.Equals(BoundKeyName, "Unbound", StringComparison.OrdinalIgnoreCase));
        string value = IsListening ? (Theme.Large ? "Press a key…" : "PRESS A KEY…") : unbound && Theme.Large ? "—" : _shownCap;
        float tracking = Theme.Large ? 0.02f : 0.08f;
        float textWidth = UiPainter.MeasureLabel(Theme.Mono, value, sizePx, tracking);
        float padX = context.Px(Theme.Large ? 12f : 10f);
        float height = context.Px(Theme.Large ? 36f : 28f);
        float width = MathF.Max(Theme.Large ? context.Px(104f) : 0f, textWidth + (padX * 2f));
        float right = Bounds.Right - context.Px(Theme.RowInset);
        var cap = new UiRect(right - width, Bounds.Y + ((Bounds.Height - height) * 0.5f), width, height);
        UiRowStyle.DrawFrame(painter, this, Action, highlight, cap.X);

        Bgra32 border = IsListening
            ? Interpolate.Lerp(UiPalette.AccentAt(0.35f), Theme.Accent, _pulse.Value)
            : Interpolate.Lerp(Theme.Large ? UiPalette.White(unbound ? 0.25f : 0.38f) : UiPalette.KeyBorder, UiPalette.AccentAt(0.45f), highlight);
        if (IsListening)
        {
            painter.FillRect(cap, UiPalette.Fade(UiPalette.AccentAt(0.08f), appear));
        }
        else if (Theme.Large && !unbound)
        {
            painter.FillRect(cap, UiPalette.White(0.06f * appear));
        }

        painter.StrokeRect(cap, context.Px(1f), UiPalette.Fade(border, appear));
        if (Theme.Large && !unbound)
        {
            // A key's heavier foot.
            painter.FillRect(new UiRect(cap.X, cap.Bottom - context.Px(2f), cap.Width, context.Px(2f)), UiPalette.Fade(border, appear));
        }

        Bgra32 text = !IsEnabled ? Theme.TextDisabled : IsListening ? Theme.Accent : unbound ? Theme.TextMuted : Theme.Text;
        painter.DrawLabel(
            Theme.Mono,
            value,
            new UiVec2(cap.X + ((cap.Width - textWidth) * 0.5f), UiRowStyle.CenterY(Theme.Mono, sizePx, cap)),
            sizePx,
            UiPalette.Fade(text, appear),
            tracking);
    }

    /// <summary>Names a binding as a keyboard's own caps do: <c>Left Ctrl</c>, <c>Mouse 1</c>, <c>Wheel up</c>, <c>1</c>.</summary>
    /// <param name="name">The binding's name, as the profile or a key's enumeration spells it.</param>
    /// <returns>The name to show on a key.</returns>
    public static string FriendlyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        if (name.StartsWith("Mouse Button ", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat("Mouse ", name.AsSpan("Mouse Button ".Length));
        }

        if (name.StartsWith("Mouse Wheel ", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat("Wheel ", name["Mouse Wheel ".Length..].ToLowerInvariant());
        }

        // A digit of the top row is D1 in a key's enumeration.
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
        {
            return name[1..];
        }

        string known = name switch
        {
            "LeftControl" or "ControlLeft" => "Left Ctrl",
            "RightControl" or "ControlRight" => "Right Ctrl",
            "LeftShift" or "ShiftLeft" => "Left Shift",
            "RightShift" or "ShiftRight" => "Right Shift",
            "LeftAlt" or "AltLeft" => "Left Alt",
            "RightAlt" or "AltRight" => "Right Alt",
            "Return" => "Enter",
            "Escape" => "Esc",
            "Equals" or "Equal" => "=",
            "Minus" => "-",
            "Comma" => ",",
            "Period" => ".",
            "Slash" => "/",
            "Backslash" or "BackSlash" => "\\",
            "Semicolon" => ";",
            "Apostrophe" => "'",
            "LeftBracket" => "[",
            "RightBracket" => "]",
            "GraveAccent" or "Grave" => "`",
            "CapsLock" => "Caps Lock",
            _ => string.Empty,
        };
        if (known.Length > 0)
        {
            return known;
        }

        // PageUp is Page Up, Keypad1 is Keypad 1: a space where the case or the kind of character changes.
        var spaced = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && name[i - 1] != ' ' && ((char.IsUpper(c) && char.IsLower(name[i - 1])) || (char.IsAsciiDigit(c) && char.IsLetter(name[i - 1]))))
            {
                spaced.Append(' ');
            }

            spaced.Append(c);
        }

        return spaced.ToString();
    }

    private static string KeyName(KeyCode key) => key.ToString();
}
