using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.Input;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A row binding an action to a key: it shows the current key, and on activation listens for the next
/// key press to rebind it.
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

    /// <summary>Initializes a new instance of the <see cref="KeyBindingRow"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="action">The action name shown on the left.</param>
    /// <param name="boundKey">The initially bound key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public KeyBindingRow(UiTheme theme, string action, KeyCode boundKey)
        : base(theme)
    {
        Action = action ?? string.Empty;
        BoundKey = boundKey;
    }

    /// <summary>Raised when the bound key changes, with the new key.</summary>
    public event Action<KeyCode>? BindingChanged;

    /// <summary>Gets or sets the action name.</summary>
    public string Action { get; set; }

    /// <summary>Gets the currently bound key.</summary>
    public KeyCode BoundKey { get; private set; }

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
            BindingChanged?.Invoke(key);
        }

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
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(Theme.BodyTextSize);
        return new UiVec2(available.X, Theme.Body.LineHeight(sizePx) + context.Px(12f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float focus = Visual.Focus;
        if (focus > 0.001f)
        {
            painter.FillRect(Bounds, Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * focus)), BlendMode.AlphaBlend);
        }

        float sizePx = painter.Context.Px(Theme.BodyTextSize);
        float pad = painter.Context.Px(8f);
        float textY = Bounds.Y + ((Bounds.Height - Theme.Body.LineHeight(sizePx)) * 0.5f);
        Bgra32 labelColor = IsEnabled ? Interpolate.Lerp(Theme.Text, Theme.Accent, focus) : Theme.TextDisabled;
        painter.DrawText(Theme.Body, Action, new UiVec2(Bounds.X + pad, textY), sizePx, labelColor);

        string value = IsListening ? "Press a key..." : KeyName(BoundKey);
        Bgra32 valueColor = IsListening
            ? Interpolate.Lerp(Theme.TextMuted, Theme.Accent, _pulse.Value)
            : Theme.Text;
        float valueWidth = Theme.Body.MeasureWidth(value, sizePx);
        painter.DrawText(Theme.Body, value, new UiVec2(Bounds.Right - pad - valueWidth, textY), sizePx, valueColor);
    }

    private static string KeyName(KeyCode key) => key.ToString();
}
