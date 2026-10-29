using OpenIGI.UI.Drawing;
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
/// <para>
/// Scrolling itself is driven from outside: the owning screen routes the wheel to whichever viewport the
/// pointer is inside (<see cref="ScrollByWheel"/>) and, when focus moves to a row that is off-screen,
/// asks the viewport to bring it back (<see cref="EnsureVisible"/>). Keeping the two out of the widget is
/// what lets one mouse-wheel rule and one focus rule serve every viewport on a screen.
/// </para>
/// </remarks>
public class ScrollView : UiElement, IWheelScrollable
{
    private readonly Spring _offset = new(0f, smoothTime: 0.12f);
    private readonly UiTheme _theme;
    private float _contentHeight;
    private float _arrangedOffset;

    // How far below the thumb's top the pointer holds it during a scrollbar drag, in pixels.
    private float _grab;

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

    /// <summary>Gets or sets how far one wheel notch scrolls, in design units.</summary>
    public float WheelStep { get; set; } = 110f;

    /// <summary>Gets or sets the gap kept between a revealed row and the viewport edge, in design units.</summary>
    public float RevealMargin { get; set; } = 14f;

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

    /// <summary>Scrolls in response to the mouse wheel.</summary>
    /// <param name="notches">The wheel movement, positive away from the user.</param>
    /// <returns><see langword="true"/> when this viewport had somewhere to scroll and took the wheel.</returns>
    /// <remarks>The wheel is reported positive when pushed away, which scrolls the content <em>up</em>.</remarks>
    public bool ScrollByWheel(float notches)
    {
        if (MaxScroll <= 0f || notches == 0f)
        {
            return false;
        }

        ScrollOffset -= notches * Px(WheelStep);
        return true;
    }

    /// <summary>Gets or sets the width of the strip along the right edge that grabs the scrollbar, in design units.</summary>
    /// <remarks>Wider than the drawn track, which is too thin to hit; content keeps a margin clear of it.</remarks>
    public float ScrollbarHitWidth { get; set; } = 18f;

    /// <summary>Gets a value indicating whether a point lies on the scrollbar, when there is one.</summary>
    /// <param name="point">The point in pixels.</param>
    /// <returns><see langword="true"/> when a press there should take the scrollbar.</returns>
    public bool IsOverScrollbar(UiVec2 point) =>
        MaxScroll > 0f && Bounds.Contains(point) && point.X >= Bounds.Right - Px(ScrollbarHitWidth);

    /// <summary>Starts dragging the scrollbar from a press on it.</summary>
    /// <param name="point">Where the press landed, in pixels.</param>
    /// <remarks>
    /// A press on the thumb holds it where it was taken; a press on the track beside it centres the thumb on the
    /// pointer first, as a desktop scrollbar's does, and the drag carries on from there.
    /// </remarks>
    public void BeginScrollbarDrag(UiVec2 point)
    {
        (float thumbY, float thumbHeight) = Thumb(ScrollOffset);
        _grab = point.Y >= thumbY && point.Y < thumbY + thumbHeight ? point.Y - thumbY : thumbHeight * 0.5f;
        DragScrollbar(point);
    }

    /// <summary>Moves the content with the scrollbar being dragged.</summary>
    /// <param name="point">The pointer, in pixels.</param>
    /// <remarks>The offset is snapped, not sprung, so the thumb stays under the pointer.</remarks>
    public void DragScrollbar(UiVec2 point)
    {
        float travel = Bounds.Height - Thumb(ScrollOffset).Height;
        if (travel <= 0f)
        {
            return;
        }

        ScrollOffset = (point.Y - _grab - Bounds.Y) / travel * MaxScroll;
        _offset.SnapTo(ScrollOffset);
    }

    /// <summary>Brings a rectangle inside the viewport into view, scrolling the least distance that does it.</summary>
    /// <param name="target">The rectangle in pixels, as laid out — a focused row's bounds.</param>
    public void EnsureVisible(UiRect target)
    {
        if (MaxScroll <= 0f || target.IsEmpty)
        {
            return;
        }

        // The child was arranged with the offset the spring was at, so undoing that offset turns its
        // screen rectangle back into a position within the content.
        float top = target.Y - Bounds.Y + _arrangedOffset;
        float bottom = top + target.Height;
        float margin = Px(RevealMargin);

        if (top - margin < ScrollOffset)
        {
            ScrollOffset = top - margin;
        }
        else if (bottom + margin > ScrollOffset + Bounds.Height)
        {
            ScrollOffset = bottom + margin - Bounds.Height;
        }
    }

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
        _arrangedOffset = offset;

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
        (float thumbY, float thumbHeight) = Thumb(_offset.Value);

        painter.FillRect(new UiRect(x, Bounds.Y, trackW, Bounds.Height), _theme.Border.WithAlpha(40), UiBlend.AlphaBlend);
        painter.FillRect(new UiRect(x, thumbY, trackW, thumbHeight), _theme.Accent.WithAlpha(160), UiBlend.AlphaBlend);
    }

    // The thumb's top and length in pixels at a scroll offset: its length the fraction of the content shown.
    private (float Y, float Height) Thumb(float offset)
    {
        float height = MathF.Max(Px(24f), Bounds.Height * (Bounds.Height / _contentHeight));
        float y = Bounds.Y + ((Bounds.Height - height) * (Math.Clamp(offset, 0f, MaxScroll) / MaxScroll));
        return (y, height);
    }

    // Design units to pixels, falling back to the raw number before the first measure has run.
    private float Px(float designUnits) => Context?.Px(designUnits) ?? designUnits;
}
