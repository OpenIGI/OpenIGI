using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Text;

/// <summary>
/// A font ready to lay out and draw: a rasterised atlas, its glyph table indexed for lookup, and the
/// image the glyphs are sampled from.
/// </summary>
/// <remarks>
/// <para>
/// The single-channel atlas is uploaded as opaque white with the coverage (or distance) in the alpha
/// channel, so the ordinary <c>texture × vertex-colour</c> modulation tints the text and carries its
/// shape in alpha without any special colour handling.
/// </para>
/// <para>
/// Measurement never touches a renderer. A font built with <see cref="ForMeasurement"/> has no texture and
/// is what the layout tests use; one built with <see cref="Create"/> can also draw from its atlas. Both
/// measure identically, because measurement reads only the glyph table.
/// </para>
/// <para>
/// A renderer that draws vector glyphs (the launcher's Skia canvas) needs no atlas image at all: it
/// reads <see cref="FontData"/>, the TrueType file the atlas was built from, and draws each glyph at the
/// pen position the atlas metrics computed, so its layout is the game's to the pixel.
/// </para>
/// </remarks>
public sealed class UiFont : IDisposable
{
    private readonly Dictionary<int, GlyphInfo> _glyphs;
    private readonly IUiImageFactory? _owner;
    private readonly int _fallbackCodepoint;
    private bool _disposed;

    private UiFont(FontAtlasData atlas, IUiImageFactory? owner, UiImage texture, byte[]? fontData)
    {
        Atlas = atlas;
        _owner = owner;
        Texture = texture;
        FontData = fontData;

        _glyphs = new Dictionary<int, GlyphInfo>(atlas.Glyphs.Count);
        foreach (GlyphInfo glyph in atlas.Glyphs)
        {
            _glyphs[glyph.Codepoint] = glyph;
        }

        // '?' is the friendliest stand-in for a code point the atlas has no glyph for; space, then any
        // glyph at all, keep a font with an unusual character set from throwing on lookup.
        _fallbackCodepoint = _glyphs.ContainsKey('?') ? '?'
            : _glyphs.ContainsKey(' ') ? ' '
            : atlas.Glyphs.Count > 0 ? atlas.Glyphs[0].Codepoint : ' ';
    }

    /// <summary>Gets the rasterised atlas backing this font.</summary>
    public FontAtlasData Atlas { get; }

    /// <summary>Gets the atlas image, or <see cref="UiImage.None"/> for a measurement-only font.</summary>
    public UiImage Texture { get; }

    /// <summary>Gets the TrueType file the atlas was built from, or <see langword="null"/> when it was not kept.</summary>
    /// <remarks>What a vector renderer draws from; an atlas renderer never reads it.</remarks>
    public byte[]? FontData { get; }

    /// <summary>Gets what one atlas texel means.</summary>
    public FontAtlasKind Kind => Atlas.Kind;

    /// <summary>Gets the pixel height the atlas was rasterised at.</summary>
    public float BasePixelHeight => Atlas.BasePixelHeight;

    /// <summary>Creates a font that can measure, uploading nothing.</summary>
    /// <param name="atlas">The rasterised atlas.</param>
    /// <param name="fontData">The TrueType file the atlas was built from, for a renderer that draws vector glyphs.</param>
    /// <returns>A font with no atlas image: it measures, and draws only through a vector renderer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="atlas"/> is <see langword="null"/>.</exception>
    public static UiFont ForMeasurement(FontAtlasData atlas, byte[]? fontData = null)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        return new UiFont(atlas, null, UiImage.None, fontData);
    }

    /// <summary>Uploads an atlas through an image factory and returns a font that can measure and draw.</summary>
    /// <param name="images">The factory that makes the atlas image and later releases it.</param>
    /// <param name="atlas">The rasterised atlas.</param>
    /// <param name="fontData">The TrueType file the atlas was built from, kept for vector renderers.</param>
    /// <returns>A drawable font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="images"/> or <paramref name="atlas"/> is <see langword="null"/>.</exception>
    public static UiFont Create(IUiImageFactory images, FontAtlasData atlas, byte[]? fontData = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(atlas);

        Bgra32[] pixels = new Bgra32[atlas.Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            // White with the glyph shape in alpha: modulating by the vertex colour then tints freely.
            pixels[i] = new Bgra32(255, 255, 255, atlas.Pixels[i]);
        }

        UiImage texture = images.CreateImage(atlas.Width, atlas.Height, pixels);
        return new UiFont(atlas, images, texture, fontData);
    }

    /// <summary>Gets the scale from atlas pixels to a requested pixel size.</summary>
    /// <param name="sizePx">The requested text size in pixels.</param>
    /// <returns>The factor every atlas-pixel metric is multiplied by.</returns>
    public float ScaleFor(float sizePx) => sizePx / Atlas.BasePixelHeight;

    /// <summary>Gets the height of one line at a requested size, in pixels.</summary>
    /// <param name="sizePx">The requested text size in pixels.</param>
    /// <returns>The line height in pixels.</returns>
    public float LineHeight(float sizePx) => Atlas.LineHeight * ScaleFor(sizePx);

    /// <summary>Gets the baseline-to-top distance at a requested size, in pixels.</summary>
    /// <param name="sizePx">The requested text size in pixels.</param>
    /// <returns>The ascent in pixels.</returns>
    public float Ascent(float sizePx) => Atlas.Ascent * ScaleFor(sizePx);

    /// <summary>Looks up a glyph, falling back to a stand-in when the atlas has no such code point.</summary>
    /// <param name="codepoint">The Unicode code point.</param>
    /// <returns>The glyph to draw.</returns>
    public GlyphInfo GetGlyph(int codepoint)
    {
        if (_glyphs.TryGetValue(codepoint, out GlyphInfo glyph))
        {
            return glyph;
        }

        return _glyphs[_fallbackCodepoint];
    }

    /// <summary>Measures a string, honouring line feeds.</summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizePx">The requested text size in pixels.</param>
    /// <returns>The widest line and the total height, both in pixels.</returns>
    public UiVec2 Measure(ReadOnlySpan<char> text, float sizePx)
    {
        float scale = ScaleFor(sizePx);
        float widest = 0f;
        float lineWidth = 0f;
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                widest = MathF.Max(widest, lineWidth);
                lineWidth = 0f;
                lines++;
                continue;
            }

            lineWidth += GetGlyph(c).Advance * scale;
        }

        widest = MathF.Max(widest, lineWidth);
        return new UiVec2(widest, lines * Atlas.LineHeight * scale);
    }

    /// <summary>Breaks text onto lines no wider than a limit, at spaces, honouring existing line feeds.</summary>
    /// <param name="text">The text to wrap.</param>
    /// <param name="sizePx">The requested text size in pixels.</param>
    /// <param name="maxWidthPx">The widest a line may be, in pixels.</param>
    /// <returns>The same text with line feeds inserted at the break points.</returns>
    /// <remarks>
    /// Greedy word wrapping: a word that will not fit starts a new line, and one that is longer than the
    /// limit on its own is left to overhang rather than being broken mid-word. Enough for prose in a
    /// briefing panel or a credits block, which is all the shell asks of it.
    /// </remarks>
    public string WrapText(string text, float sizePx, float maxWidthPx)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxWidthPx <= 0f || text.Length == 0)
        {
            return text;
        }

        var wrapped = new System.Text.StringBuilder(text.Length + 16);
        float spaceWidth = MeasureWidth(" ", sizePx);

        foreach (string paragraph in text.Split('\n'))
        {
            if (wrapped.Length > 0)
            {
                wrapped.Append('\n');
            }

            float lineWidth = 0f;
            bool firstWordOnLine = true;
            foreach (string word in paragraph.Split(' '))
            {
                if (word.Length == 0)
                {
                    continue;
                }

                float wordWidth = MeasureWidth(word, sizePx);
                if (!firstWordOnLine && lineWidth + spaceWidth + wordWidth > maxWidthPx)
                {
                    wrapped.Append('\n');
                    lineWidth = 0f;
                    firstWordOnLine = true;
                }

                if (!firstWordOnLine)
                {
                    wrapped.Append(' ');
                    lineWidth += spaceWidth;
                }

                wrapped.Append(word);
                lineWidth += wordWidth;
                firstWordOnLine = false;
            }
        }

        return wrapped.ToString();
    }

    /// <summary>Measures the advance width of a single line, ignoring line feeds.</summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizePx">The requested text size in pixels.</param>
    /// <returns>The advance width in pixels.</returns>
    public float MeasureWidth(ReadOnlySpan<char> text, float sizePx)
    {
        float scale = ScaleFor(sizePx);
        float width = 0f;
        foreach (char c in text)
        {
            width += GetGlyph(c).Advance * scale;
        }

        return width;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Texture.IsValid)
        {
            _owner?.DestroyImage(Texture);
        }
    }
}
