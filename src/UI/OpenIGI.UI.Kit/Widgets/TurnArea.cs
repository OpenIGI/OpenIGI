using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// An empty stretch of the page something behind it is turned by: dragged across with the mouse or a finger, or
/// stepped with left and right while it has focus. It draws nothing but a hint in its bottom right corner while the
/// pointer is on it or it has focus.
/// </summary>
public sealed class TurnArea : Control
{
    // A drag across the area's whole width turns a full circle; a step from the keys or pad is a twelfth of one.
    private const float StepRadians = MathF.Tau / 12f;

    private float? _lastX;

    /// <summary>Initializes a new instance of the <see cref="TurnArea"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="hint">The words in its corner, for example <c>Drag to turn</c>.</param>
    public TurnArea(UiTheme theme, string hint)
        : base(theme)
    {
        HintText = (hint ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Raised as it is turned, with how far: positive to the right.</summary>
    public event Action<float>? Turned;

    /// <summary>Gets the words in its corner.</summary>
    public string HintText { get; }

    /// <inheritdoc/>
    public override bool CapturesPointer(UiVec2 point)
    {
        _lastX = point.X;
        return true;
    }

    /// <inheritdoc/>
    public override void PointerDragged(UiVec2 point)
    {
        if (_lastX is { } last && Bounds.Width > 0f && point.X != last)
        {
            Turned?.Invoke((point.X - last) / Bounds.Width * MathF.Tau);
        }

        _lastX = point.X;
    }

    /// <inheritdoc/>
    public override void PointerReleased(UiVec2 point, bool cancelled) => _lastX = null;

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        float step = direction switch
        {
            NavDirection.Left => -StepRadians,
            NavDirection.Right => StepRadians,
            _ => 0f,
        };
        if (step == 0f)
        {
            return false;
        }

        Turned?.Invoke(step);
        return true;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float shown = Math.Max(Visual.Focus, IsPointerOver || _lastX is not null ? 1f : 0f);
        if (shown <= 0f || HintText.Length == 0)
        {
            return;
        }

        LayoutContext context = painter.Context;
        float size = context.Px(11f);
        float width = UiPainter.MeasureLabel(Theme.Mono, HintText, size, 0.28f);
        var at = new UiVec2(Bounds.Right - width, Bounds.Bottom - Theme.Mono.LineHeight(size) - context.Px(14f));
        painter.DrawLabel(Theme.Mono, HintText, at, size, Theme.TextMuted.WithAlpha((byte)(Theme.TextMuted.A * shown)), 0.28f);
    }
}
