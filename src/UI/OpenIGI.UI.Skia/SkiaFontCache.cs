using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using OpenIGI.UI.Text;
using SkiaSharp;

namespace OpenIGI.UI.Skia;

/// <summary>Turns the interface's fonts into Skia typefaces sized to match the game's atlas metrics exactly.</summary>
/// <remarks>
/// <para>
/// The layout is the atlas's: <see cref="UiFont"/> measures with the advances stb_truetype computed for a
/// pixel height, where a pixel height is the font's ascender-to-descender span. Skia sizes a font by its
/// em instead, so a size has to be converted by <c>unitsPerEm / (ascender − descender)</c>, read from the
/// file's own <c>head</c> and <c>hhea</c> tables — the very numbers stb used — rather than from whatever
/// metrics the platform's font manager would report.
/// </para>
/// <para>
/// Glyph ids are looked up once per character and kept, because the canvas draws runs of ids.
/// </para>
/// </remarks>
public sealed class SkiaFontCache : IDisposable
{
    private readonly ConditionalWeakTable<UiFont, Face> _faces = [];
    private readonly List<Face> _all = [];

    /// <summary>Gets the Skia face for a font, or <see langword="null"/> when it carries no TrueType data.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The face.</returns>
    public Face? Get(UiFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (_faces.TryGetValue(font, out Face? face))
        {
            return face;
        }

        if (font.FontData is not { } data)
        {
            return null;
        }

        SKTypeface? typeface = SKTypeface.FromData(SKData.CreateCopy(data));
        if (typeface is null)
        {
            return null;
        }

        face = new Face(typeface, EmPerPixelHeight(data));
        _faces.Add(font, face);
        _all.Add(face);
        return face;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Face face in _all)
        {
            face.Dispose();
        }

        _all.Clear();
        _faces.Clear();
    }

    /// <summary>Reads how many em units one stb pixel height spans, from the font's own tables.</summary>
    /// <param name="ttf">The TrueType file.</param>
    /// <returns><c>unitsPerEm / (ascender − descender)</c>, or 1 when the tables cannot be read.</returns>
    public static float EmPerPixelHeight(ReadOnlySpan<byte> ttf)
    {
        if (ttf.Length < 12)
        {
            return 1f;
        }

        int tables = BinaryPrimitives.ReadUInt16BigEndian(ttf[4..]);
        int head = -1;
        int hhea = -1;
        for (int i = 0; i < tables; i++)
        {
            int record = 12 + (i * 16);
            if (record + 16 > ttf.Length)
            {
                break;
            }

            uint tag = BinaryPrimitives.ReadUInt32BigEndian(ttf[record..]);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(ttf[(record + 8)..]);
            if (tag == 0x68656164)
            {
                head = offset;
            }
            else if (tag == 0x68686561)
            {
                hhea = offset;
            }
        }

        if (head < 0 || hhea < 0 || head + 20 > ttf.Length || hhea + 8 > ttf.Length)
        {
            return 1f;
        }

        int unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(ttf[(head + 18)..]);
        int ascender = BinaryPrimitives.ReadInt16BigEndian(ttf[(hhea + 4)..]);
        int descender = BinaryPrimitives.ReadInt16BigEndian(ttf[(hhea + 6)..]);
        int span = ascender - descender;
        return unitsPerEm <= 0 || span <= 0 ? 1f : unitsPerEm / (float)span;
    }

    /// <summary>One typeface, with its size conversion and its character-to-glyph table.</summary>
    public sealed class Face : IDisposable
    {
        private readonly Dictionary<char, ushort> _glyphs = [];
        private readonly SKFont _font;

        internal Face(SKTypeface typeface, float emPerPixelHeight)
        {
            Typeface = typeface;
            EmPerPixelHeight = emPerPixelHeight;
            _font = new SKFont(typeface, 16f) { Subpixel = true, Hinting = SKFontHinting.Slight, Edging = SKFontEdging.Antialias };
        }

        /// <summary>Gets the typeface.</summary>
        public SKTypeface Typeface { get; }

        /// <summary>Gets the size conversion: a Skia em size is a pixel height times this.</summary>
        public float EmPerPixelHeight { get; }

        /// <summary>Gets the glyph id for a character.</summary>
        /// <param name="character">The character.</param>
        /// <returns>The glyph id; zero for a character the face does not have.</returns>
        public ushort Glyph(char character)
        {
            if (!_glyphs.TryGetValue(character, out ushort id))
            {
                id = _font.GetGlyph(character);
                _glyphs[character] = id;
            }

            return id;
        }

        /// <summary>Makes a Skia font for drawing at a pixel height.</summary>
        /// <param name="pixelHeight">The pixel height, as the interface measures it.</param>
        /// <returns>The font; the caller disposes it.</returns>
        public SKFont CreateFont(float pixelHeight) =>
            new(Typeface, pixelHeight * EmPerPixelHeight) { Subpixel = true, Hinting = SKFontHinting.Slight, Edging = SKFontEdging.Antialias };

        /// <inheritdoc/>
        public void Dispose()
        {
            _font.Dispose();
            Typeface.Dispose();
        }
    }
}
