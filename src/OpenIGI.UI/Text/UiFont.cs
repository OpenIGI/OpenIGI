using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Text;

/// <summary>
/// A font ready to lay out and draw: a rasterised atlas, its glyph table indexed for lookup, and the
/// GPU texture the glyphs are sampled from.
/// </summary>
/// <remarks>
/// <para>
/// The single-channel atlas is uploaded as opaque white with the coverage (or distance) in the alpha
/// channel, so the ordinary <c>texture × vertex-colour</c> modulation tints the text and carries its
/// shape in alpha without any special colour handling.
/// </para>
/// <para>
/// Measurement never touches the GPU. A font built with <see cref="ForMeasurement"/> has no texture and
/// is what the layout tests use; one built with <see cref="Upload"/> can also draw. Both measure
/// identically, because measurement reads only the glyph table.
/// </para>
/// </remarks>
public sealed class UiFont : IDisposable
{
    private readonly Dictionary<int, GlyphInfo> _glyphs;
    private readonly IGraphicsDevice? _device;
    private readonly int _fallbackCodepoint;
    private bool _disposed;

    private UiFont(FontAtlasData atlas, IGraphicsDevice? device, TextureHandle texture)
    {
        Atlas = atlas;
        _device = device;
        Texture = texture;

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

    /// <summary>Gets the atlas texture, or <see cref="TextureHandle.None"/> for a measurement-only font.</summary>
    public TextureHandle Texture { get; }

    /// <summary>Gets what one atlas texel means.</summary>
    public FontAtlasKind Kind => Atlas.Kind;

    /// <summary>Gets the pixel height the atlas was rasterised at.</summary>
    public float BasePixelHeight => Atlas.BasePixelHeight;

    /// <summary>Creates a font that can measure but not draw, uploading nothing to the GPU.</summary>
    /// <param name="atlas">The rasterised atlas.</param>
    /// <returns>A measurement-only font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="atlas"/> is <see langword="null"/>.</exception>
    public static UiFont ForMeasurement(FontAtlasData atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        return new UiFont(atlas, null, TextureHandle.None);
    }

    /// <summary>Uploads an atlas to a device and returns a font that can measure and draw.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <param name="atlas">The rasterised atlas.</param>
    /// <returns>A drawable font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> or <paramref name="atlas"/> is <see langword="null"/>.</exception>
    public static UiFont Upload(IGraphicsDevice device, FontAtlasData atlas)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(atlas);

        Bgra32[] pixels = new Bgra32[atlas.Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            // White with the glyph shape in alpha: modulating by the vertex colour then tints freely.
            pixels[i] = new Bgra32(255, 255, 255, atlas.Pixels[i]);
        }

        TextureHandle texture = device.CreateTexture(new TextureDescription(atlas.Width, atlas.Height), pixels);
        return new UiFont(atlas, device, texture);
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
            _device?.DestroyTexture(Texture);
        }
    }
}
