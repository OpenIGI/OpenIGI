using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI;

/// <summary>One quad of a rounded figure: where it goes, and what it samples.</summary>
/// <param name="Destination">The piece's rectangle in the caller's own coordinates.</param>
/// <param name="Source">The piece's rectangle in the atlas, mirrored where the piece is.</param>
public readonly record struct NineSlicePiece(UiRect Destination, UiRect Source);

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
public readonly record struct NineSlice(UiImage Texture, UiRect Source, float CornerPixels)
{
    /// <summary>Gets a value indicating whether the slice refers to a real texture.</summary>
    public bool IsValid => Texture.IsValid && CornerPixels > 0f;

    /// <summary>The most pieces a rounded figure is ever drawn from.</summary>
    public const int MaxPieces = 9;

    /// <summary>Lays out the pieces a rounded rectangle is drawn from.</summary>
    /// <param name="rect">The figure's rectangle, in whatever coordinates the caller draws in.</param>
    /// <param name="radius">The corner radius, clamped so it can never exceed half the rectangle.</param>
    /// <param name="pieces">Receives the pieces; needs room for <see cref="MaxPieces"/>.</param>
    /// <returns>How many pieces were written.</returns>
    /// <exception cref="ArgumentException"><paramref name="pieces"/> is too short.</exception>
    /// <remarks>
    /// <para>
    /// The geometry only, so that the one description of what a rounded figure is made of serves every
    /// surface that draws one: <see cref="UiPainter.FillRoundedRect"/> puts these on the screen, and a
    /// canvas standing in the level puts the same pieces on a plane.
    /// </para>
    /// <para>
    /// The middle samples the corner's innermost texel rather than being a flat untextured fill, so a
    /// caller can draw every piece the same way. That texel is the inside of the shape, which is solid.
    /// </para>
    /// </remarks>
    public int GetPieces(UiRect rect, float radius, Span<NineSlicePiece> pieces)
    {
        if (pieces.Length < MaxPieces)
        {
            throw new ArgumentException($"A rounded figure needs room for {MaxPieces} pieces.", nameof(pieces));
        }

        float r = MathF.Min(radius, MathF.Min(rect.Width, rect.Height) * 0.5f);
        if (r <= 0f || rect.IsEmpty)
        {
            return 0;
        }

        float left = rect.X;
        float top = rect.Y;
        float right = rect.Right;
        float bottom = rect.Bottom;
        float innerWidth = MathF.Max(0f, rect.Width - (r * 2f));
        float innerHeight = MathF.Max(0f, rect.Height - (r * 2f));

        int count = 0;
        pieces[count++] = new NineSlicePiece(new UiRect(left, top, r, r), Corner(false, false));
        pieces[count++] = new NineSlicePiece(new UiRect(right - r, top, r, r), Corner(true, false));
        pieces[count++] = new NineSlicePiece(new UiRect(left, bottom - r, r, r), Corner(false, true));
        pieces[count++] = new NineSlicePiece(new UiRect(right - r, bottom - r, r, r), Corner(true, true));

        if (innerWidth > 0f)
        {
            pieces[count++] = new NineSlicePiece(new UiRect(left + r, top, innerWidth, r), HorizontalEdge(false));
            pieces[count++] = new NineSlicePiece(new UiRect(left + r, bottom - r, innerWidth, r), HorizontalEdge(true));
        }

        if (innerHeight > 0f)
        {
            pieces[count++] = new NineSlicePiece(new UiRect(left, top + r, r, innerHeight), VerticalEdge(false));
            pieces[count++] = new NineSlicePiece(new UiRect(right - r, top + r, r, innerHeight), VerticalEdge(true));
        }

        if (innerWidth > 0f && innerHeight > 0f)
        {
            pieces[count++] = new NineSlicePiece(new UiRect(left + r, top + r, innerWidth, innerHeight), Inside());
        }

        return count;
    }

    /// <summary>The single solid texel at the corner's inside, which the middle is filled from.</summary>
    private UiRect Inside()
    {
        float texelX = Source.Width <= 0f ? 0f : Source.Width / MathF.Max(1f, CornerPixels);
        float texelY = Source.Height <= 0f ? 0f : Source.Height / MathF.Max(1f, CornerPixels);
        return new UiRect(
            Source.X + Source.Width - (texelX * 0.5f),
            Source.Y + Source.Height - (texelY * 0.5f),
            0f,
            0f);
    }

    /// <summary>Returns the source rectangle for one corner, mirrored out of the stored one.</summary>
    /// <param name="right">Whether to mirror horizontally.</param>
    /// <param name="bottom">Whether to mirror vertically.</param>
    /// <returns>The source rectangle, with negative extents where it is mirrored.</returns>
    /// <remarks>
    /// Mirroring is expressed by moving the origin to the far edge and negating the extent, so the
    /// sprite batch interpolates the texture coordinates backwards without needing a flip flag.
    /// </remarks>
    public UiRect Corner(bool right, bool bottom) => new(
        right ? Source.X + Source.Width : Source.X,
        bottom ? Source.Y + Source.Height : Source.Y,
        right ? -Source.Width : Source.Width,
        bottom ? -Source.Height : Source.Height);

    /// <summary>Returns the source strip used to stretch a horizontal edge.</summary>
    /// <param name="bottom">Whether this is the bottom edge.</param>
    /// <returns>The rectangle covering the corner's inner column.</returns>
    /// <remarks>
    /// <para>
    /// A one-texel-wide column taken from the corner's inside edge: along a straight edge the shape is
    /// constant, so one column stretched is exactly right and costs nothing to store.
    /// </para>
    /// <para>
    /// Taken through the middle of that texel rather than across it. Spanning the whole texel makes the
    /// far end of the strip sample halfway into its neighbour, which on a corner sprite is a texel of
    /// partial coverage: the edge then meets its corner a couple of percent short of opaque, and on a
    /// dark fill over a light background that shows as a seam.
    /// </para>
    /// </remarks>
    public UiRect HorizontalEdge(bool bottom)
    {
        float texel = Source.Width <= 0f ? 0f : Source.Width / MathF.Max(1f, CornerPixels);
        return new UiRect(
            Source.X + Source.Width - (texel * 0.5f),
            bottom ? Source.Y + Source.Height : Source.Y,
            0f,
            bottom ? -Source.Height : Source.Height);
    }

    /// <summary>Returns the source strip used to stretch a vertical edge.</summary>
    /// <param name="right">Whether this is the right edge.</param>
    /// <returns>The rectangle covering the corner's inner row.</returns>
    /// <remarks>Sampled through the middle of the row, for the reason <see cref="HorizontalEdge"/> gives.</remarks>
    public UiRect VerticalEdge(bool right)
    {
        float texel = Source.Height <= 0f ? 0f : Source.Height / MathF.Max(1f, CornerPixels);
        return new UiRect(
            right ? Source.X + Source.Width : Source.X,
            Source.Y + Source.Height - (texel * 0.5f),
            right ? -Source.Width : Source.Width,
            0f);
    }
}
