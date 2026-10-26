using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Lays out and draws strings with a bitmap font.
/// </summary>
/// <remarks>
/// <para>
/// Layout follows the original's font object (<c>Font_Create</c>, <c>0x4B6DB0</c>): a cursor, a
/// per-axis scale and a colour. Each character resolves through the font's <c>TRAN</c> map to a glyph,
/// the glyph is drawn at the cursor plus its own offset, and the cursor advances by the glyph's
/// advance.
/// </para>
/// <para>
/// The original's sprite and text colours are on a scale of 128: it initialises a font's colour to
/// <c>128.0f</c> and multiplies every colour by 1/128 on its way to the vertex (<c>0x534AB0</c>), so 128 is
/// the art as it is. The 2D batch multiplies by the colour as a fraction of 255, so
/// <see cref="NeutralColor"/> is full white and <see cref="FromRetail"/> converts the original's colours.
/// </para>
/// </remarks>
public sealed class BitmapTextRenderer
{
    private readonly SpriteResource _font;

    /// <summary>Initializes a new instance of the <see cref="BitmapTextRenderer"/> class.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="font"/> is not a font.</exception>
    public BitmapTextRenderer(SpriteResource font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!font.IsFont)
        {
            throw new ArgumentException($"Sprite '{font.Asset.Name}' has no character map and cannot render text.", nameof(font));
        }

        _font = font;
    }

    /// <summary>Gets the colour that leaves the art as it is: the original's 128 in each channel.</summary>
    public static Bgra32 NeutralColor { get; } = new(255, 255, 255, 255);

    /// <summary>Converts one of the original's sprite, text or rectangle colours, 128 a channel, to the batch's.</summary>
    /// <param name="r">Red, 0 to 128.</param>
    /// <param name="g">Green.</param>
    /// <param name="b">Blue.</param>
    /// <param name="alpha">The alpha, 0 to 1.</param>
    /// <returns>The colour, each channel times 255/128 made whole and held to a byte, as the vertex packing does.</returns>
    public static Bgra32 FromRetail(float r, float g, float b, float alpha = 1f) =>
        Bgra32.FromRgba(Channel(r * 255f / 128f), Channel(g * 255f / 128f), Channel(b * 255f / 128f), Channel(alpha * 255f));

    private static byte Channel(float value) => (byte)Math.Clamp((int)value, 0, 255);

    /// <summary>Gets the font this renderer draws with.</summary>
    public SpriteResource Font => _font;

    /// <summary>Gets the height of one line in pixels at scale 1.</summary>
    public int LineHeight => _font.Asset.DefaultHeight;

    /// <summary>Measures a string without drawing it.</summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="scaleX">The horizontal scale.</param>
    /// <param name="scaleY">The vertical scale.</param>
    /// <returns>The advance width and the line height, both in pixels.</returns>
    public (float Width, float Height) Measure(ReadOnlySpan<char> text, float scaleX = 1.0f, float scaleY = 1.0f)
    {
        float width = 0.0f;
        float lineWidth = 0.0f;
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                width = Math.Max(width, lineWidth);
                lineWidth = 0.0f;
                lines++;
                continue;
            }

            lineWidth += AdvanceOf(c) * scaleX;
        }

        return (Math.Max(width, lineWidth), lines * LineHeight * scaleY);
    }

    /// <summary>Draws a string.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="text">The text to draw. A line feed starts a new line at the original left edge.</param>
    /// <param name="x">The left edge in pixels.</param>
    /// <param name="y">The top edge in pixels.</param>
    /// <param name="color">The colour modulated with the glyph pixels.</param>
    /// <param name="scaleX">The horizontal scale.</param>
    /// <param name="scaleY">The vertical scale.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    /// <returns>The position the cursor ended at.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/>.</exception>
    public (float X, float Y) Draw(
        SpriteBatch batch,
        ReadOnlySpan<char> text,
        float x,
        float y,
        Bgra32 color,
        float scaleX = 1.0f,
        float scaleY = 1.0f,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        ArgumentNullException.ThrowIfNull(batch);

        float cursorX = x;
        float cursorY = y;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                cursorX = x;
                cursorY += LineHeight * scaleY;
                continue;
            }

            SpriteFrame glyph = _font.Asset.GetGlyph(c);
            _font.DrawFrame(batch, glyph, cursorX, cursorY, scaleX, scaleY, color, blend);
            cursorX += AdvanceOf(c) * scaleX;
        }

        return (cursorX, cursorY);
    }

    /// <summary>Draws a string centred horizontally on a position.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="centerX">The horizontal centre in pixels.</param>
    /// <param name="y">The top edge in pixels.</param>
    /// <param name="color">The colour modulated with the glyph pixels.</param>
    /// <param name="scaleX">The horizontal scale.</param>
    /// <param name="scaleY">The vertical scale.</param>
    /// <param name="blend">How the glyphs combine with what is behind them.</param>
    public void DrawCentered(
        SpriteBatch batch,
        ReadOnlySpan<char> text,
        float centerX,
        float y,
        Bgra32 color,
        float scaleX = 1.0f,
        float scaleY = 1.0f,
        BlendMode blend = BlendMode.AlphaBlend)
    {
        (float width, _) = Measure(text, scaleX, scaleY);
        Draw(batch, text, centerX - (width / 2.0f), y, color, scaleX, scaleY, blend);
    }

    /// <summary>
    /// Gets how far the cursor moves after a character.
    /// </summary>
    /// <remarks>
    /// Glyphs whose advance is zero fall back to their own width. Retail fonts do set an advance, but
    /// the fallback keeps a font that does not from stacking every character on the same pixel.
    /// </remarks>
    private float AdvanceOf(char c)
    {
        SpriteFrame glyph = _font.Asset.GetGlyph(c);
        if (glyph.Advance > 0)
        {
            return glyph.Advance;
        }

        return glyph.Width > 0 ? glyph.Width : _font.Asset.DefaultAdvance;
    }
}
