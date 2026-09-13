using OpenIGI.Rendering;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Text;

/// <summary>Where one glyph goes, and what it samples.</summary>
/// <param name="Destination">The glyph's box, in the coordinates the run was started in.</param>
/// <param name="Source">The glyph's rectangle in the font's atlas.</param>
public readonly record struct GlyphPlacement(UiRect Destination, RectF Source);

/// <summary>
/// Walks a run of text, giving each drawable glyph its box.
/// </summary>
/// <remarks>
/// <para>
/// The one description of how a line of text is laid out: the pen starts at the run's top-left, each
/// glyph is placed from the baseline by its own bearings, and the pen advances. It exists so that the
/// surfaces which draw text — the screen, and a canvas standing in the level — lay it out the same
/// way rather than each keeping their own copy of the same arithmetic.
/// </para>
/// <para>
/// It is a <see langword="ref"/> struct over the caller's span: no allocation, and a line feed returns
/// the pen and drops a line, which is the only formatting a run understands.
/// </para>
/// </remarks>
public ref struct GlyphRun
{
    private readonly UiFont _font;
    private readonly ReadOnlySpan<char> _text;
    private readonly float _size;
    private readonly float _scale;
    private readonly float _originX;
    private float _penX;
    private float _baseline;
    private int _index;

    /// <summary>Initializes a run over a span of text.</summary>
    /// <param name="font">The face the run is set in.</param>
    /// <param name="text">The text; a line feed starts a new line at the original left edge.</param>
    /// <param name="sizePx">The text size, in whatever units the caller draws in.</param>
    /// <param name="topLeft">The top-left of the first line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public GlyphRun(UiFont font, ReadOnlySpan<char> text, float sizePx, UiVec2 topLeft = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        _font = font;
        _text = text;
        _size = sizePx;
        _scale = font.ScaleFor(sizePx);
        _originX = topLeft.X;
        _penX = topLeft.X;
        _baseline = topLeft.Y + font.Ascent(sizePx);
        _index = 0;
        Current = default;
    }

    /// <summary>Gets the glyph the run has reached.</summary>
    public GlyphPlacement Current { get; private set; }

    /// <summary>Gets where the pen is now: after the last glyph, on that glyph's line.</summary>
    public readonly UiVec2 Pen => new(_penX, _baseline - _font.Ascent(_size));

    /// <summary>Advances to the next glyph that has something to draw.</summary>
    /// <returns><see langword="false"/> at the end of the run.</returns>
    public bool MoveNext()
    {
        while (_index < _text.Length)
        {
            char character = _text[_index++];
            if (character == '\n')
            {
                _penX = _originX;
                _baseline += _font.LineHeight(_size);
                continue;
            }

            GlyphInfo glyph = _font.GetGlyph(character);
            float advance = glyph.Advance * _scale;
            if (!glyph.HasBitmap)
            {
                _penX += advance;
                continue;
            }

            Current = new GlyphPlacement(
                new UiRect(
                    _penX + (glyph.OffsetX * _scale),
                    _baseline + (glyph.OffsetY * _scale),
                    glyph.Width * _scale,
                    glyph.Height * _scale),
                glyph.SourceRect(_font.Atlas.Width, _font.Atlas.Height));
            _penX += advance;
            return true;
        }

        return false;
    }
}
