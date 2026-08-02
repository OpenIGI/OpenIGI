using StbTrueTypeSharp;

namespace OpenIGI.UI.Text;

/// <summary>
/// Rasterises a TrueType font into a <see cref="FontAtlasData"/> with <c>stb_truetype</c>.
/// </summary>
/// <remarks>
/// <para>
/// Run once at startup (and then cached to disk, see <see cref="FontAtlasCache"/>). Each requested code
/// point is rasterised at a single base pixel height, its bitmap copied into a managed buffer and the
/// native allocation freed immediately, then the whole set is packed by <see cref="ShelfPacker"/>. The
/// result is resolution-independent: the renderer scales the atlas-pixel metrics to whatever size the
/// text is drawn at.
/// </para>
/// <para>
/// This is the only unsafe code in the framework. <c>stb_truetype</c> works through raw pointers and,
/// crucially, its <c>stbtt_fontinfo</c> keeps a pointer into the font bytes — so every rasterisation
/// call lives inside the one <c>fixed</c> block that pins them.
/// </para>
/// </remarks>
public static class FontAtlasBuilder
{
    /// <summary>Gets the printable ASCII code points, U+0020 through U+007E.</summary>
    public static IReadOnlyList<int> AsciiPrintable { get; } = BuildAsciiRange();

    /// <summary>Rasterises a font into an atlas.</summary>
    /// <param name="fontData">The TrueType file's bytes.</param>
    /// <param name="pixelHeight">The base pixel height to rasterise at. Larger is sharper and larger on disk.</param>
    /// <param name="kind">Whether to produce a coverage or a signed-distance-field atlas.</param>
    /// <param name="codepoints">The code points to include, or <see langword="null"/> for printable ASCII.</param>
    /// <param name="atlasWidth">The atlas width in texels; the height is derived from the packing.</param>
    /// <param name="sdfPadding">The distance the SDF extends beyond a glyph, in texels. Ignored for coverage.</param>
    /// <param name="sdfOnEdge">The texel value placed exactly on a glyph edge. Ignored for coverage.</param>
    /// <returns>The rasterised atlas.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fontData"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size argument is out of range.</exception>
    /// <exception cref="ArgumentException">The font could not be parsed, or a glyph is too wide for the atlas.</exception>
    public static unsafe FontAtlasData Build(
        byte[] fontData,
        float pixelHeight,
        FontAtlasKind kind = FontAtlasKind.Coverage,
        IReadOnlyList<int>? codepoints = null,
        int atlasWidth = 512,
        int sdfPadding = 4,
        byte sdfOnEdge = 128)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(atlasWidth);

        codepoints ??= AsciiPrintable;

        // For an SDF, one texel is worth (onEdge / padding) distance units: a glyph edge sits at
        // onEdge, and the field reaches zero one padding-width away. The shader needs this to know how
        // far a texel of distance is when it thresholds.
        int padding = kind == FontAtlasKind.SignedDistanceField ? sdfPadding : 0;
        float pixelDistScale = padding > 0 ? sdfOnEdge / (float)padding : 0f;

        var rasterised = new List<RasterGlyph>(codepoints.Count);

        var info = new StbTrueType.stbtt_fontinfo();
        fixed (byte* dataPtr = fontData)
        {
            if (StbTrueType.stbtt_InitFont(info, dataPtr, 0) == 0)
            {
                throw new ArgumentException("The font data could not be parsed as TrueType.", nameof(fontData));
            }

            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelHeight);

            int rawAscent;
            int rawDescent;
            int rawLineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &rawAscent, &rawDescent, &rawLineGap);
            float ascent = rawAscent * scale;
            float descent = -rawDescent * scale; // stb reports descent as negative; store it positive-down.
            float lineGap = rawLineGap * scale;

            foreach (int codepoint in codepoints)
            {
                int glyph = StbTrueType.stbtt_FindGlyphIndex(info, codepoint);

                int rawAdvance;
                int leftSideBearing;
                StbTrueType.stbtt_GetGlyphHMetrics(info, glyph, &rawAdvance, &leftSideBearing);
                float advance = rawAdvance * scale;

                int width = 0;
                int height = 0;
                int offsetX = 0;
                int offsetY = 0;
                byte* bitmap = kind == FontAtlasKind.SignedDistanceField
                    ? StbTrueType.stbtt_GetGlyphSDF(info, scale, glyph, padding, sdfOnEdge, pixelDistScale, &width, &height, &offsetX, &offsetY)
                    : StbTrueType.stbtt_GetGlyphBitmap(info, scale, scale, glyph, &width, &height, &offsetX, &offsetY);

                byte[] copy = [];
                if (bitmap is not null && width > 0 && height > 0)
                {
                    copy = new Span<byte>(bitmap, width * height).ToArray();
                    if (kind == FontAtlasKind.SignedDistanceField)
                    {
                        StbTrueType.stbtt_FreeSDF(bitmap, null);
                    }
                    else
                    {
                        StbTrueType.stbtt_FreeBitmap(bitmap, null);
                    }
                }
                else
                {
                    width = 0;
                    height = 0;
                }

                rasterised.Add(new RasterGlyph(codepoint, width, height, offsetX, offsetY, advance, copy));
            }

            var sizes = new (int Width, int Height)[rasterised.Count];
            for (int i = 0; i < rasterised.Count; i++)
            {
                sizes[i] = (rasterised[i].Width, rasterised[i].Height);
            }

            ShelfPacker.Result packed = ShelfPacker.Pack(sizes, atlasWidth, padding: 1);
            int atlasHeight = packed.Height;

            byte[] pixels = new byte[atlasWidth * atlasHeight];
            var glyphs = new GlyphInfo[rasterised.Count];

            for (int i = 0; i < rasterised.Count; i++)
            {
                RasterGlyph glyph = rasterised[i];
                ShelfPacker.Placement at = packed.Placements[i];

                if (glyph.Width > 0 && glyph.Height > 0)
                {
                    for (int row = 0; row < glyph.Height; row++)
                    {
                        int source = row * glyph.Width;
                        int destination = ((at.Y + row) * atlasWidth) + at.X;
                        Array.Copy(glyph.Bitmap, source, pixels, destination, glyph.Width);
                    }
                }

                glyphs[i] = new GlyphInfo(
                    glyph.Codepoint,
                    glyph.Width > 0 ? at.X : 0,
                    glyph.Height > 0 ? at.Y : 0,
                    glyph.Width,
                    glyph.Height,
                    glyph.OffsetX,
                    glyph.OffsetY,
                    glyph.Advance);
            }

            return new FontAtlasData(
                kind,
                atlasWidth,
                atlasHeight,
                pixels,
                pixelHeight,
                ascent,
                descent,
                lineGap,
                glyphs,
                padding,
                sdfOnEdge,
                pixelDistScale);
        }
    }

    private static int[] BuildAsciiRange()
    {
        int[] range = new int[0x7F - 0x20];
        for (int i = 0; i < range.Length; i++)
        {
            range[i] = 0x20 + i;
        }

        return range;
    }

    private readonly record struct RasterGlyph(
        int Codepoint,
        int Width,
        int Height,
        int OffsetX,
        int OffsetY,
        float Advance,
        byte[] Bitmap);
}
