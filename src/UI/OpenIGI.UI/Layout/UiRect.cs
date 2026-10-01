namespace OpenIGI.UI.Layout;

/// <summary>
/// An axis-aligned rectangle in the interface's pixel space, held as a position and a size.
/// </summary>
/// <remarks>
/// The framework's only rectangle: layout boxes, clip rectangles and texture-coordinate windows alike,
/// so nothing here names a graphics backend's type. Position-and-size, origin top-left, Y growing
/// downwards, matching every renderer's 2D convention. A texture window may carry a negative size to
/// mirror the image.
/// </remarks>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width. Never negative for a laid-out box.</param>
/// <param name="Height">The height. Never negative for a laid-out box.</param>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    /// <summary>Gets the empty rectangle at the origin.</summary>
    public static UiRect Empty => default;

    /// <summary>Gets the unit rectangle (0,0)–(1,1): a whole image in texture coordinates.</summary>
    public static UiRect Unit => new(0f, 0f, 1f, 1f);

    /// <summary>Gets the right edge.</summary>
    public float Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>Gets the top-left corner.</summary>
    public UiVec2 TopLeft => new(X, Y);

    /// <summary>Gets the size.</summary>
    public UiVec2 Size => new(Width, Height);

    /// <summary>Gets the centre point.</summary>
    public UiVec2 Center => new(X + (Width * 0.5f), Y + (Height * 0.5f));

    /// <summary>Gets a value indicating whether the rectangle encloses no area.</summary>
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    /// <summary>Creates a rectangle from two corners.</summary>
    /// <param name="left">The left edge.</param>
    /// <param name="top">The top edge.</param>
    /// <param name="right">The right edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    /// <returns>The rectangle spanning the two corners.</returns>
    public static UiRect FromEdges(float left, float top, float right, float bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>Determines whether a point lies inside the rectangle, left and top inclusive.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Determines whether a point lies inside the rectangle.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(UiVec2 point) => Contains(point.X, point.Y);

    /// <summary>Returns this rectangle moved by an offset.</summary>
    /// <param name="dx">The horizontal offset.</param>
    /// <param name="dy">The vertical offset.</param>
    /// <returns>The moved rectangle.</returns>
    public UiRect Offset(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Returns this rectangle shrunk on every side by a padding.</summary>
    /// <param name="padding">The amount to remove from each edge.</param>
    /// <returns>The deflated rectangle, clamped so it never has negative extent.</returns>
    public UiRect Deflate(Thickness padding)
    {
        float x = X + padding.Left;
        float y = Y + padding.Top;
        float w = MathF.Max(0f, Width - padding.Horizontal);
        float h = MathF.Max(0f, Height - padding.Vertical);
        return new UiRect(x, y, w, h);
    }

    /// <summary>Returns this rectangle grown on every side by a margin.</summary>
    /// <param name="margin">The amount to add to each edge.</param>
    /// <returns>The inflated rectangle.</returns>
    public UiRect Inflate(Thickness margin) =>
        new(X - margin.Left, Y - margin.Top, Width + margin.Horizontal, Height + margin.Vertical);

    /// <summary>Returns the overlap of this rectangle with another.</summary>
    /// <param name="other">The rectangle to intersect with.</param>
    /// <returns>The overlap, or <see cref="Empty"/> when they do not overlap.</returns>
    public UiRect Intersect(UiRect other)
    {
        float left = MathF.Max(X, other.X);
        float top = MathF.Max(Y, other.Y);
        float right = MathF.Min(Right, other.Right);
        float bottom = MathF.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : FromEdges(left, top, right, bottom);
    }
}
