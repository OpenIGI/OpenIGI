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
/// The neutral colour is <b>128</b> per channel, not 255. The original initialises its font colour to
/// <c>128.0f</c> in all three channels, so treating 255 as "unmodified" renders every string at double
/// brightness. <see cref="NeutralColor"/> spells that out.
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

    /// <summary>Gets the colour the original treats as "unmodified": 128 in each channel.</summary>
    public static Bgra32 NeutralColor { get; } = new(128, 128, 128, 255);

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
