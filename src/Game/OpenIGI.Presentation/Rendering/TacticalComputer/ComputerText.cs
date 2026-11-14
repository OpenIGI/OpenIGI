using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The remastered computer's text: set in the title's own bitmap font at a whole number of pixels a pixel, so it stays as
/// crisp as the original's, with the shared monospaced face standing in where the font is missing or lacks a letter.
/// </summary>
/// <remarks>
/// One line height serves both, the bitmap font's where there is one, so a line that had to fall back sits on the same
/// baseline grid as its neighbours.
/// </remarks>
internal sealed class ComputerText
{
    // How many strings' capitals and coverage are remembered before the memory is started again.
    private const int MemoryLimit = 512;

    private readonly Dictionary<string, string> _capitals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _covered = new(StringComparer.Ordinal);
    private readonly UiFont _fallback;
    private BitmapTextRenderer? _font;
    private SpriteBatch? _batch;
    private UiPainter? _painter;
    private int _scale = 1;
    private float _opacity = 1f;

    /// <summary>Initializes a new instance of the <see cref="ComputerText"/> class.</summary>
    /// <param name="fallback">The face drawn where the title's font cannot.</param>
    public ComputerText(UiFont fallback) => _fallback = fallback;

    /// <summary>Gets the height of a line, in pixels.</summary>
    public float LineHeight => _font is { } font ? font.LineHeight * _scale : MathF.Ceiling(_fallback.LineHeight(FallbackSize));

    // The stand-in face's size: a retail line's worth.
    private float FallbackSize => 9f * _scale;

    /// <summary>The whole number of pixels a font pixel is drawn as on a screen.</summary>
    /// <param name="requested">The interface scale the player asked for, or automatic.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <returns>The scale: the player's own, or one for every 500 lines of the screen.</returns>
    public static int ScaleFor(int requested, int height) => ScaleFor(requested, height, 1f);

    /// <summary>Gets the whole-number scale the display's text is drawn at, on a screen of a given pixel density.</summary>
    /// <param name="requested">The scale asked for, or <see cref="UiScale.Automatic"/>.</param>
    /// <param name="height">The screen's height in its own pixels.</param>
    /// <param name="pixelDensity">How many of those there are to a pixel as the person sees it: 3 on most phones.</param>
    /// <returns>The scale.</returns>
    /// <remarks>
    /// A phone's 1170 rows are a hand's width of glass: counted as rows they ask for the scale of a small monitor, and
    /// the display's labels came out five seen pixels tall. On a dense screen the text is at least a size that reads.
    /// </remarks>
    public static int ScaleFor(int requested, int height, float pixelDensity)
    {
        if (requested != UiScale.Automatic)
        {
            return Math.Clamp(requested, 1, UiScale.Maximum);
        }

        int byHeight = height / 500;
        int byDensity = pixelDensity >= 1.5f ? (int)MathF.Round(1.6f * pixelDensity) : 1;
        return Math.Clamp(Math.Max(byHeight, byDensity), 1, UiScale.Maximum);
    }

    /// <summary>Sets the font and the size for what is measured next, before anything is drawn.</summary>
    /// <param name="font">The title's font, or <see langword="null"/>.</param>
    /// <param name="scale">How many pixels a font pixel is drawn as.</param>
    public void Use(BitmapTextRenderer? font, int scale)
    {
        if (!ReferenceEquals(font, _font))
        {
            _covered.Clear();
        }

        _font = font;
        _scale = Math.Max(1, scale);
    }

    /// <summary>Starts a frame's drawing.</summary>
    /// <param name="batch">The batch the font's glyphs go into.</param>
    /// <param name="painter">The painter the stand-in face is drawn with.</param>
    /// <param name="opacity">How much of the text shows, where no later pass fades it.</param>
    public void Begin(SpriteBatch batch, UiPainter painter, float opacity)
    {
        _batch = batch;
        _painter = painter;
        _opacity = Math.Clamp(opacity, 0f, 1f);
    }

    /// <summary>A string in capitals, remembered.</summary>
    /// <param name="text">The string.</param>
    /// <returns>It in capitals.</returns>
    public string Capitals(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (!_capitals.TryGetValue(text, out string? capitals))
        {
            if (_capitals.Count >= MemoryLimit)
            {
                _capitals.Clear();
            }

            capitals = text.ToUpperInvariant();
            _capitals[text] = capitals;
        }

        return capitals;
    }

    /// <summary>Running text as the font can set it: as written where it has the letters, else in capitals.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text to draw.</returns>
    public string Prose(string text) => Covers(text) ? text : Capitals(text);

    /// <summary>Measures a line.</summary>
    /// <param name="text">The line.</param>
    /// <returns>Its width in pixels.</returns>
    public float Measure(string text) =>
        string.IsNullOrEmpty(text) ? 0f
        : _font is { } font && Covers(text) ? font.Measure(text, _scale, _scale).Width
        : _fallback.MeasureWidth(text, FallbackSize);

    /// <summary>Draws a line with its top left at a point.</summary>
    /// <param name="text">The line.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    /// <returns>Its width in pixels.</returns>
    public float Draw(string text, float x, float y, Bgra32 colour)
    {
        if (string.IsNullOrEmpty(text) || _batch is null || _painter is null)
        {
            return 0f;
        }

        Bgra32 ink = colour.WithAlpha((byte)(colour.A * _opacity));
        float left = MathF.Round(x), top = MathF.Round(y);
        if (_font is { } font && Covers(text))
        {
            font.Draw(_batch, text, left, top, ink, _scale, _scale);
            return font.Measure(text, _scale, _scale).Width;
        }

        float size = FallbackSize;
        _painter.DrawText(_fallback, text, new UiVec2(left, top + MathF.Round((LineHeight - _fallback.LineHeight(size)) * 0.5f)), size, ink);
        return _fallback.MeasureWidth(text, size);
    }

    /// <summary>Draws a line ending at a point.</summary>
    /// <param name="text">The line.</param>
    /// <param name="right">Its right edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    /// <returns>Its left edge, in pixels.</returns>
    public float DrawRight(string text, float right, float y, Bgra32 colour)
    {
        float left = right - Measure(text);
        Draw(text, left, y, colour);
        return left;
    }

    /// <summary>Draws a line over the map, on a dark plate so it reads on any ground.</summary>
    /// <param name="text">The line.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    public void DrawOnMap(string text, float x, float y, Bgra32 colour)
    {
        if (string.IsNullOrEmpty(text) || _painter is null)
        {
            return;
        }

        float left = MathF.Round(x), top = MathF.Round(y);
        float padX = 3f * _scale, padY = _scale;
        _painter.FillRect(
            new UiRect(left - padX, top - padY, Measure(text) + (padX * 2f), LineHeight + (padY * 2f)),
            Bgra32.FromRgba(0, 0, 0, (byte)(colour.A * 0.62f)));
        Draw(text, left, top, colour);
    }

    /// <summary>Breaks running text into lines no wider than a width.</summary>
    /// <param name="text">The text; a line feed starts a new line.</param>
    /// <param name="width">The widest a line may be, in pixels.</param>
    /// <param name="lines">Receives the lines, after what it already holds.</param>
    public void Wrap(string text, float width, List<string> lines)
    {
        foreach (string paragraph in text.Split('\n'))
        {
            string line = string.Empty;
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string longer = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(longer) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = longer;
                }
            }

            lines.Add(line);
        }
    }

    // Whether the title's font has every letter of a string.
    private bool Covers(string text)
    {
        if (_font is not { } font)
        {
            return false;
        }

        if (_covered.TryGetValue(text, out bool covered))
        {
            return covered;
        }

        if (_covered.Count >= MemoryLimit)
        {
            _covered.Clear();
        }

        ReadOnlySpan<byte> map = font.Font.Asset.CharacterMap;
        covered = true;
        foreach (char c in text)
        {
            if (c is ' ' or '\n')
            {
                continue;
            }

            if (c >= map.Length || map[c] == 0)
            {
                covered = false;
                break;
            }
        }

        _covered[text] = covered;
        return covered;
    }
}
