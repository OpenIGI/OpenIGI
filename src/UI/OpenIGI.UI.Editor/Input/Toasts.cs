using OpenIGI.Core.Graphics;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Input;

/// <summary>How a notification reads.</summary>
public enum ToastKind
{
    /// <summary>Something happened.</summary>
    Info,

    /// <summary>Something worked.</summary>
    Success,

    /// <summary>Something needs a look.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>
/// Notifications stacked in the window's bottom-right corner, newest at the bottom, each fading after a few seconds, as
/// scene editors report what an action did without taking the keyboard.
/// </summary>
public sealed class Toasts
{
    private const int MaxShown = 4;
    private const float FadeSeconds = 0.4f;

    private readonly List<(string Message, ToastKind Kind, float Left)> _shown = [];

    /// <summary>Gets or sets how far above the bottom edge the stack stands, such as a status bar's height.</summary>
    public float BottomInset { get; set; }

    /// <summary>Gets the notifications shown now, oldest first.</summary>
    public IEnumerable<string> Messages => _shown.Select(static t => t.Message);

    /// <summary>Shows a notification; the same message again restarts its time rather than stacking a copy.</summary>
    /// <param name="message">What to say.</param>
    /// <param name="kind">How it reads.</param>
    /// <param name="seconds">How long it stays.</param>
    public void Show(string message, ToastKind kind = ToastKind.Info, float seconds = 5f)
    {
        ArgumentNullException.ThrowIfNull(message);
        _shown.RemoveAll(t => t.Message == message);
        _shown.Add((message, kind, seconds));
        while (_shown.Count > MaxShown)
        {
            _shown.RemoveAt(0);
        }
    }

    /// <summary>Ages the notifications.</summary>
    /// <param name="elapsedSeconds">The frame time.</param>
    public void Update(float elapsedSeconds)
    {
        for (int i = _shown.Count - 1; i >= 0; i--)
        {
            float left = _shown[i].Left - elapsedSeconds;
            if (left <= 0f)
            {
                _shown.RemoveAt(i);
            }
            else
            {
                _shown[i] = _shown[i] with { Left = left };
            }
        }
    }

    /// <summary>Draws the stack.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="window">The window's rectangle.</param>
    public void Draw(UiPainter painter, EditorTheme theme, UiRect window)
    {
        ArgumentNullException.ThrowIfNull(theme);
        float pad = theme.Px(12f);
        float width = MathF.Min(theme.Px(380f), window.Width - (pad * 2f));
        float height = theme.Px(40f);
        float y = window.Bottom - BottomInset - pad;
        for (int i = _shown.Count - 1; i >= 0; i--)
        {
            (string message, ToastKind kind, float left) = _shown[i];
            y -= height;
            UiRect box = new(window.Right - pad - width, y, width, height);
            y -= theme.Px(8f);
            float alpha = Math.Clamp(left / FadeSeconds, 0f, 1f);
            painter.PushOpacity(alpha);
            EditorDraw.Shadow(painter, theme, box);
            EditorDraw.Frame(painter, theme, box, theme.Popup, theme.BorderStrong);
            Bgra32 ink = kind switch
            {
                ToastKind.Success => theme.Success,
                ToastKind.Warning => theme.Warning,
                ToastKind.Error => theme.Error,
                _ => theme.Accent,
            };
            EditorDraw.Fill(painter, theme, new UiRect(box.X, box.Y, theme.Px(3f), box.Height), ink, theme.Px(1.5f));
            string icon = kind switch
            {
                ToastKind.Success => "check_circle",
                ToastKind.Warning => "warning",
                ToastKind.Error => "error",
                _ => "info",
            };
            UiRect iconBox = new(box.X + theme.Px(10f), box.Y, theme.Px(20f), box.Height);
            EditorDraw.Icon(painter, theme, icon, iconBox, ink, theme.Px(18f));
            float textX = iconBox.Right + theme.Px(8f);
            string shown = EditorDraw.Fit(theme.Font, message, theme.TextSize, box.Right - textX - theme.Px(10f));
            EditorDraw.Label(painter, theme, shown, new UiRect(textX, box.Y, box.Right - textX - theme.Px(10f), box.Height), theme.Text);
            painter.PopOpacity();
        }
    }
}
