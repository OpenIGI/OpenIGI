namespace OpenIGI.Rendering;

/// <summary>
/// An axis-aligned rectangle given as a position and a size.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width. May be negative to mirror horizontally.</param>
/// <param name="Height">The height. May be negative to mirror vertically.</param>
/// <remarks>
/// Position-and-size rather than a corner pair, because that is the form the original's primitives
/// carry and converting once at the boundary is
/// less error-prone than converting at each use.
/// </remarks>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    /// <summary>Gets the empty rectangle at the origin.</summary>
    public static RectF Empty => default;

    /// <summary>Gets the whole unit square, the texture coordinates of a full texture.</summary>
    public static RectF Unit => new(0.0f, 0.0f, 1.0f, 1.0f);

    /// <summary>Gets the right edge.</summary>
    public float Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>Gets a value indicating whether the rectangle encloses no area.</summary>
    public bool IsEmpty => Width == 0.0f || Height == 0.0f;

    /// <summary>Creates a rectangle from two corners.</summary>
    /// <param name="left">The left edge.</param>
    /// <param name="top">The top edge.</param>
    /// <param name="right">The right edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    /// <returns>The rectangle spanning the two corners.</returns>
    public static RectF FromEdges(float left, float top, float right, float bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>Determines whether a point lies inside the rectangle, left and top inclusive.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Returns this rectangle moved by an offset.</summary>
    /// <param name="dx">The horizontal offset.</param>
    /// <param name="dy">The vertical offset.</param>
    /// <returns>The moved rectangle.</returns>
    public RectF Offset(float dx, float dy) => new(X + dx, Y + dy, Width, Height);
}
