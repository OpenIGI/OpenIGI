using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Text;

/// <summary>
/// One glyph's placement in the font atlas and the metrics needed to lay it out.
/// </summary>
/// <remarks>
/// Every measurement is in <em>atlas pixels</em> — the pixel height the atlas was rasterised at. The
/// text renderer scales them by the ratio between the requested size and that base height, so a font is
/// rasterised once and drawn at any size. Positions follow the typographic convention: the pen sits on
/// the baseline, <see cref="OffsetX"/>/<see cref="OffsetY"/> place the glyph's top-left relative to it
/// (Y grows downwards, so a glyph reaching above the baseline has a negative <see cref="OffsetY"/>), and
/// <see cref="Advance"/> moves the pen on to the next glyph.
/// </remarks>
/// <param name="Codepoint">The Unicode code point this glyph renders.</param>
/// <param name="AtlasX">The glyph's left edge in the atlas, in texels.</param>
/// <param name="AtlasY">The glyph's top edge in the atlas, in texels.</param>
/// <param name="Width">The glyph bitmap's width in texels; zero for a whitespace glyph.</param>
/// <param name="Height">The glyph bitmap's height in texels; zero for a whitespace glyph.</param>
/// <param name="OffsetX">The pen-to-glyph-left offset in atlas pixels.</param>
/// <param name="OffsetY">The baseline-to-glyph-top offset in atlas pixels.</param>
/// <param name="Advance">How far the pen advances after this glyph, in atlas pixels.</param>
public readonly record struct GlyphInfo(
    int Codepoint,
    int AtlasX,
    int AtlasY,
    int Width,
    int Height,
    float OffsetX,
    float OffsetY,
    float Advance)
{
    /// <summary>Gets a value indicating whether this glyph has pixels to draw.</summary>
    public bool HasBitmap => Width > 0 && Height > 0;

    /// <summary>Computes the glyph's texture-coordinate rectangle within an atlas of the given size.</summary>
    /// <param name="atlasWidth">The atlas width in texels.</param>
    /// <param name="atlasHeight">The atlas height in texels.</param>
    /// <returns>The source rectangle in normalised texture coordinates.</returns>
    public UiRect SourceRect(int atlasWidth, int atlasHeight) => new(
        (float)AtlasX / atlasWidth,
        (float)AtlasY / atlasHeight,
        (float)Width / atlasWidth,
        (float)Height / atlasHeight);
}
