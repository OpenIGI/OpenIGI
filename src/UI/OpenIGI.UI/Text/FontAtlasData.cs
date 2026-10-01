namespace OpenIGI.UI.Text;

/// <summary>What one texel of a font atlas means.</summary>
public enum FontAtlasKind
{
    /// <summary>
    /// Direct anti-aliased coverage: the texel is the glyph's alpha. Rendered through the ordinary
    /// textured sprite path, so it needs no special shader; sharp when drawn near its rasterised size.
    /// </summary>
    Coverage,

    /// <summary>
    /// A signed distance to the nearest glyph edge, remapped to <c>[0,1]</c>. Needs the distance-field
    /// shader to threshold, and in exchange stays crisp at any magnification and drives outline, glow
    /// and soft shadow from the one channel.
    /// </summary>
    SignedDistanceField,
}

/// <summary>
/// The CPU-side result of rasterising a font into an atlas: the single-channel pixels, the glyph table
/// and the vertical metrics. This is exactly what the disk cache stores and what
/// <see cref="OpenIGI.UI.Text.UiFont"/> uploads to the GPU.
/// </summary>
/// <remarks>
/// The pixels are one byte per texel. For <see cref="FontAtlasKind.Coverage"/> that byte is alpha; for
/// <see cref="FontAtlasKind.SignedDistanceField"/> it is the remapped distance, with
/// <see cref="SdfOnEdge"/> marking the glyph edge and <see cref="SdfPixelDistScale"/> giving how many
/// distance units one atlas texel spans. Every metric is in atlas pixels — the height the atlas was
/// rasterised at, <see cref="BasePixelHeight"/> — and scaled by the renderer at draw time.
/// </remarks>
public sealed class FontAtlasData
{
    /// <summary>Initializes a new instance of the <see cref="FontAtlasData"/> class.</summary>
    /// <param name="kind">What the pixels mean.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    /// <param name="pixels">The single-channel atlas, row-major, <paramref name="width"/> per row.</param>
    /// <param name="basePixelHeight">The pixel height the glyphs were rasterised at.</param>
    /// <param name="ascent">The baseline-to-top distance in atlas pixels.</param>
    /// <param name="descent">The baseline-to-bottom distance in atlas pixels (positive downwards).</param>
    /// <param name="lineGap">The recommended extra space between lines, in atlas pixels.</param>
    /// <param name="glyphs">The glyph table.</param>
    /// <param name="sdfPadding">For an SDF atlas, the padding baked around each glyph in texels; otherwise zero.</param>
    /// <param name="sdfOnEdge">For an SDF atlas, the texel value that marks the glyph edge; otherwise zero.</param>
    /// <param name="sdfPixelDistScale">For an SDF atlas, distance units per texel; otherwise zero.</param>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is not <paramref name="width"/> × <paramref name="height"/> long.</exception>
    public FontAtlasData(
        FontAtlasKind kind,
        int width,
        int height,
        byte[] pixels,
        float basePixelHeight,
        float ascent,
        float descent,
        float lineGap,
        IReadOnlyList<GlyphInfo> glyphs,
        int sdfPadding = 0,
        byte sdfOnEdge = 0,
        float sdfPixelDistScale = 0f)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException(
                $"Atlas pixels are {pixels.Length} bytes, expected {width}×{height} = {width * height}.",
                nameof(pixels));
        }

        Kind = kind;
        Width = width;
        Height = height;
        Pixels = pixels;
        BasePixelHeight = basePixelHeight;
        Ascent = ascent;
        Descent = descent;
        LineGap = lineGap;
        Glyphs = glyphs;
        SdfPadding = sdfPadding;
        SdfOnEdge = sdfOnEdge;
        SdfPixelDistScale = sdfPixelDistScale;
    }

    /// <summary>Gets what one texel means.</summary>
    public FontAtlasKind Kind { get; }

    /// <summary>Gets the atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the atlas height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the single-channel atlas, row-major.</summary>
    public byte[] Pixels { get; }

    /// <summary>Gets the pixel height the glyphs were rasterised at.</summary>
    public float BasePixelHeight { get; }

    /// <summary>Gets the baseline-to-top distance in atlas pixels.</summary>
    public float Ascent { get; }

    /// <summary>Gets the baseline-to-bottom distance in atlas pixels, positive downwards.</summary>
    public float Descent { get; }

    /// <summary>Gets the recommended extra space between lines, in atlas pixels.</summary>
    public float LineGap { get; }

    /// <summary>Gets the height of one line — ascent plus descent plus line gap — in atlas pixels.</summary>
    public float LineHeight => Ascent + Descent + LineGap;

    /// <summary>Gets the glyph table.</summary>
    public IReadOnlyList<GlyphInfo> Glyphs { get; }

    /// <summary>Gets the SDF padding baked around each glyph in texels, or zero for a coverage atlas.</summary>
    public int SdfPadding { get; }

    /// <summary>Gets the texel value that marks the glyph edge, or zero for a coverage atlas.</summary>
    public byte SdfOnEdge { get; }

    /// <summary>Gets the distance units per atlas texel, or zero for a coverage atlas.</summary>
    public float SdfPixelDistScale { get; }
}
