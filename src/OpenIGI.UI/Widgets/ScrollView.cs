using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A viewport onto a single taller child, scrolled vertically, its content clipped to the viewport and
/// a thumb shown when there is more than fits.
/// </summary>
/// <remarks>
/// The content is measured with unbounded height and arranged offset upwards by the scroll amount; the
/// viewport clips it (on the CPU, see <see cref="UiPainter"/>). A spring carries the visible offset so
/// a page jump glides rather than snapping. This is the console scrollback's viewport and, in the shell,
/// a long settings panel's.
/// </remarks>
public class ScrollView : UiElement
{
    private readonly Spring _offset = new(0f, smoothTime: 0.12f);
    private readonly UiTheme _theme;
    private float _contentHeight;

    /// <summary>Initializes a new instance of the <see cref="ScrollView"/> class.</summary>
    /// <param name="theme">The theme, for the scrollbar colour.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public ScrollView(UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    /// <summary>Gets or sets the target scroll offset in pixels, clamped to the scrollable range.</summary>
    public float ScrollOffset
    {
        get => _offset.Target;
        set => _offset.Target = Math.Clamp(value, 0f, MaxScroll);
    }

    /// <summary>Gets the largest scroll offset in pixels.</summary>
    public float MaxScroll => MathF.Max(0f, _contentHeight - Bounds.Height);

    /// <summary>Sets the single child shown in the viewport, replacing any existing content.</summary>
    /// <param name="content">The content element.</param>
    public void SetContent(UiElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ClearChildren();
        AddChild(content);
    }

    /// <summary>Scrolls by a number of pixels, positive downwards.</summary>
    /// <param name="deltaPixels">The pixels to scroll by.</param>
    public void ScrollBy(float deltaPixels) => ScrollOffset += deltaPixels;

    /// <summary>Scrolls to the bottom of the content.</summary>
    public void ScrollToEnd() => ScrollOffset = MaxScroll;

    /// <inheritdoc/>
    protected override bool ClipsChildren => true;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _contentHeight = 0f;
        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Measure(context, new UiVec2(available.X, float.PositiveInfinity));
            _contentHeight = MathF.Max(_contentHeight, child.DesiredSize.Y);
        }

        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // Re-clamp: a relayout may have shrunk the content below the current offset.
        _offset.Target = Math.Clamp(_offset.Target, 0f, MaxScroll);
        float offset = Math.Clamp(_offset.Value, 0f, MaxScroll);

        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Arrange(new UiRect(contentRect.X, contentRect.Y - offset, contentRect.Width, child.DesiredSize.Y));
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds) => _offset.Update(elapsedSeconds);

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (MaxScroll <= 0f)
        {
            return;
        }

        // A slim thumb on the right, its length and position tracking what fraction is shown.
        float trackW = painter.Context.Px(4f);
        float x = Bounds.Right - trackW;
        float visibleFraction = Bounds.Height / _contentHeight;
        float thumbHeight = MathF.Max(painter.Context.Px(24f), Bounds.Height * visibleFraction);
        float offset = Math.Clamp(_offset.Value, 0f, MaxScroll);
        float thumbY = Bounds.Y + ((Bounds.Height - thumbHeight) * (offset / MaxScroll));

        painter.FillRect(new UiRect(x, Bounds.Y, trackW, Bounds.Height), _theme.Border.WithAlpha(40), BlendMode.AlphaBlend);
        painter.FillRect(new UiRect(x, thumbY, trackW, thumbHeight), _theme.Accent.WithAlpha(160), BlendMode.AlphaBlend);
    }
}
