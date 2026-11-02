using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// One axis of scrolling: the offset, the wheel, and a thin scrollbar that widens under the pointer and can be
/// dragged. Every scrolling editor widget — trees, lists, the property grid, the code editor — scrolls through
/// one of these, so they all scroll and look alike.
/// </summary>
public sealed class ScrollModel
{
    private float _grabOffset = float.NaN;
    private float _grabPointer;

    /// <summary>Gets or sets the offset in pixels, kept within range.</summary>
    public float Offset
    {
        get;
        set => field = Math.Clamp(value, 0f, MaxOffset);
    }

    /// <summary>Gets or sets the length of the content in pixels.</summary>
    public float Content { get; set; }

    /// <summary>Gets or sets the length of the visible area in pixels.</summary>
    public float Viewport { get; set; }

    /// <summary>Gets the largest offset.</summary>
    public float MaxOffset => MathF.Max(0f, Content - Viewport);

    /// <summary>Gets whether the content overflows and a scrollbar is shown.</summary>
    public bool CanScroll => Content > Viewport + 0.5f;

    /// <summary>Gets whether the scrollbar is being dragged.</summary>
    public bool IsDragging => !float.IsNaN(_grabOffset);

    /// <summary>Updates the extents, keeping the offset within the new range.</summary>
    /// <param name="content">The content length.</param>
    /// <param name="viewport">The visible length.</param>
    public void Measure(float content, float viewport)
    {
        Content = content;
        Viewport = viewport;
        Offset = Math.Min(Offset, MaxOffset);
    }

    /// <summary>Scrolls by wheel notches.</summary>
    /// <param name="notches">Notches; positive scrolls towards the start.</param>
    /// <param name="step">Pixels per notch.</param>
    /// <returns>Whether the offset moved; if not, an enclosing view may take the wheel.</returns>
    public bool Wheel(float notches, float step)
    {
        float before = Offset;
        Offset -= notches * step;
        return Offset != before;
    }

    /// <summary>Scrolls just enough to bring a span into view.</summary>
    /// <param name="start">The span's start, in content pixels.</param>
    /// <param name="length">The span's length.</param>
    public void Reveal(float start, float length)
    {
        if (start < Offset)
        {
            Offset = start;
        }
        else if (start + length > Offset + Viewport)
        {
            Offset = start + length - Viewport;
        }
    }

    /// <summary>Gets the vertical scrollbar's track along a view's right edge.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="view">The view.</param>
    /// <returns>The track.</returns>
    public static UiRect VerticalTrack(EditorTheme theme, UiRect view)
    {
        ArgumentNullException.ThrowIfNull(theme);
        float width = theme.Px(10f);
        return new UiRect(view.Right - width, view.Y, width, view.Height);
    }

    /// <summary>Gets the thumb within a vertical track.</summary>
    /// <param name="track">The track.</param>
    /// <returns>The thumb.</returns>
    public UiRect Thumb(UiRect track)
    {
        float length = MathF.Max(track.Height * (Viewport / MathF.Max(Content, 1f)), MathF.Min(24f, track.Height));
        float travel = track.Height - length;
        float y = track.Y + (MaxOffset > 0f ? travel * (Offset / MaxOffset) : 0f);
        return new UiRect(track.X, y, track.Width, length);
    }

    /// <summary>Starts dragging the scrollbar, or jumps a page when the press is on the track off the thumb.</summary>
    /// <param name="track">The track.</param>
    /// <param name="pointerY">The pointer's y.</param>
    public void BeginDrag(UiRect track, float pointerY)
    {
        UiRect thumb = Thumb(track);
        if (pointerY < thumb.Y || pointerY > thumb.Bottom)
        {
            Offset += (pointerY < thumb.Y ? -1f : 1f) * Viewport;
        }

        _grabOffset = Offset;
        _grabPointer = pointerY;
    }

    /// <summary>Follows the pointer while dragging.</summary>
    /// <param name="track">The track.</param>
    /// <param name="pointerY">The pointer's y.</param>
    public void Drag(UiRect track, float pointerY)
    {
        if (!IsDragging)
        {
            return;
        }

        float travel = track.Height - Thumb(track).Height;
        if (travel > 0f)
        {
            Offset = _grabOffset + ((pointerY - _grabPointer) * (MaxOffset / travel));
        }
    }

    /// <summary>Ends a drag.</summary>
    public void EndDrag() => _grabOffset = float.NaN;

    /// <summary>Draws the scrollbar: a slim thumb, wider and brighter when hovered or dragged.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="track">The track.</param>
    /// <param name="hot">Whether the pointer is over the track or the thumb is held.</param>
    public void Draw(UiPainter painter, EditorTheme theme, UiRect track, bool hot)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        if (!CanScroll)
        {
            return;
        }

        UiRect thumb = Thumb(track);
        float width = hot || IsDragging ? theme.Px(8f) : theme.Px(4f);
        UiRect shape = new(track.Right - width - theme.Px(1f), thumb.Y + theme.Px(1f), width, thumb.Height - theme.Px(2f));
        EditorDraw.Fill(painter, theme, shape, hot || IsDragging ? theme.BorderStrong : theme.Border, width * 0.5f);
    }

    /// <summary>Starts a drag when a press lands on a view's vertical scrollbar.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="view">The scrolling view.</param>
    /// <param name="point">The press.</param>
    /// <returns>Whether the press was on the scrollbar and is now a drag the view should capture.</returns>
    public bool TryGrab(EditorTheme theme, UiRect view, UiVec2 point)
    {
        UiRect track = VerticalTrack(theme, view);
        if (!CanScroll || !track.Contains(point))
        {
            return false;
        }

        BeginDrag(track, point.Y);
        return true;
    }

    /// <summary>Follows the pointer during a drag started by <see cref="TryGrab"/>.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="view">The scrolling view.</param>
    /// <param name="point">The pointer.</param>
    /// <returns>Whether a drag is in progress, so the view should not treat the move as its own.</returns>
    public bool FollowGrab(EditorTheme theme, UiRect view, UiVec2 point)
    {
        if (!IsDragging)
        {
            return false;
        }

        Drag(VerticalTrack(theme, view), point.Y);
        return true;
    }

    /// <summary>Ends a drag started by <see cref="TryGrab"/>.</summary>
    /// <returns>Whether one was in progress.</returns>
    public bool ReleaseGrab()
    {
        bool was = IsDragging;
        EndDrag();
        return was;
    }

    /// <summary>Draws a view's vertical scrollbar, hot when the pointer is over its track.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="view">The scrolling view.</param>
    /// <param name="pointer">The pointer, or <see langword="null"/> when it is not over the view.</param>
    public void DrawVertical(UiPainter painter, EditorTheme theme, UiRect view, UiVec2? pointer)
    {
        UiRect track = VerticalTrack(theme, view);
        Draw(painter, theme, track, pointer is { } p && track.Contains(p));
    }
}
