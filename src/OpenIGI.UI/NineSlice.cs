using OpenIGI.Rendering;

namespace OpenIGI.UI;

/// <summary>
/// One stretchable sprite in a surface atlas: a square corner, described once and drawn into all four
/// corners by mirroring its source rectangle.
/// </summary>
/// <remarks>
/// <para>
/// Only the top-left corner is stored. The other three are the same texels sampled with a flipped
/// source rectangle, which is what makes a rounded panel cost one small quadrant of texture instead of
/// a whole nine-cell sheet, and guarantees the four corners are identical rather than nearly so.
/// </para>
/// <para>
/// The edges are that same corner stretched along its straight side, and the middle is a flat fill, so
/// the whole figure is nine quads and one texture.
/// </para>
/// </remarks>
/// <param name="Texture">The atlas the corner lives in.</param>
/// <param name="Source">The top-left corner's rectangle within the atlas, in texture coordinates.</param>
/// <param name="CornerPixels">The corner's size in texels, which is also the radius it draws at 1:1.</param>
public readonly record struct NineSlice(TextureHandle Texture, RectF Source, float CornerPixels)
{
    /// <summary>Gets a value indicating whether the slice refers to a real texture.</summary>
    public bool IsValid => Texture.IsValid && CornerPixels > 0f;

    /// <summary>Returns the source rectangle for one corner, mirrored out of the stored one.</summary>
    /// <param name="right">Whether to mirror horizontally.</param>
    /// <param name="bottom">Whether to mirror vertically.</param>
    /// <returns>The source rectangle, with negative extents where it is mirrored.</returns>
    /// <remarks>
    /// Mirroring is expressed by moving the origin to the far edge and negating the extent, so the
    /// sprite batch interpolates the texture coordinates backwards without needing a flip flag.
    /// </remarks>
    public RectF Corner(bool right, bool bottom) => new(
        right ? Source.X + Source.Width : Source.X,
        bottom ? Source.Y + Source.Height : Source.Y,
        right ? -Source.Width : Source.Width,
        bottom ? -Source.Height : Source.Height);

    /// <summary>Returns the source strip used to stretch a horizontal edge.</summary>
    /// <param name="bottom">Whether this is the bottom edge.</param>
    /// <returns>The rectangle covering the corner's inner column.</returns>
    /// <remarks>
    /// A one-texel-wide column taken from the corner's inside edge: along a straight edge the shape is
    /// constant, so one column stretched is exactly right and costs nothing to store.
    /// </remarks>
    public RectF HorizontalEdge(bool bottom)
    {
        float texel = Source.Width <= 0f ? 0f : Source.Width / MathF.Max(1f, CornerPixels);
        return new RectF(
            Source.X + Source.Width - texel,
            bottom ? Source.Y + Source.Height : Source.Y,
            texel,
            bottom ? -Source.Height : Source.Height);
    }

    /// <summary>Returns the source strip used to stretch a vertical edge.</summary>
    /// <param name="right">Whether this is the right edge.</param>
    /// <returns>The rectangle covering the corner's inner row.</returns>
    public RectF VerticalEdge(bool right)
    {
        float texel = Source.Height <= 0f ? 0f : Source.Height / MathF.Max(1f, CornerPixels);
        return new RectF(
            right ? Source.X + Source.Width : Source.X,
            Source.Y + Source.Height - texel,
            right ? -Source.Width : Source.Width,
            texel);
    }
}
