using System.Buffers.Binary;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Formats.Sprites;

/// <summary>Normalizes unpacked LOOP 10 fonts to the same atlas and glyph shape as packed fonts.</summary>
/// <remarks>
/// This is a compatibility extension, not a claim about the reference retail executable. Layout was
/// measured from unpacked intro, computer and menu fonts in a Project I.G.I. installation: a 36-byte
/// header, 256 byte-sized character mappings, then the default record and one 20-byte record per glyph.
/// Accepting them prevents optional subtitle/HUD assets from terminating mission entry. The stored
/// pixels and metrics are retained; packing them together avoids a GPU texture and binding per glyph.
/// </remarks>
internal static class UnpackedFontLoader
{
    private const int HeaderSize = 36;
    private const int CharacterCount = 256;
    private const int TableOffset = HeaderSize + CharacterCount;
    private const int RecordSize = 20;
    private const int MaxAtlasSize = 4096;

    internal static SpriteAsset Load(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length < TableOffset)
        {
            throw new TextureFormatException($"Unpacked font '{name}' has a truncated header or character map.");
        }

        uint glyphCount = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        uint pixelBase = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
        long tableEnd = TableOffset + (((long)glyphCount + 1) * RecordSize);
        if (glyphCount is 0 or > byte.MaxValue || tableEnd > pixelBase || pixelBase > data.Length)
        {
            throw new TextureFormatException($"Unpacked font '{name}' has an invalid glyph table or pixel offset.");
        }

        Glyph[] glyphs = new Glyph[glyphCount];
        int maxWidth = 1;
        int maxHeight = 1;
        for (int i = 0; i < glyphs.Length; i++)
        {
            // Record zero is the blank/default, whereas SpriteAsset's map resolves n to frame n - 1.
            ReadOnlySpan<byte> record = data.Slice(TableOffset + ((i + 1) * RecordSize), RecordSize);
            Glyph glyph = ReadGlyph(record);
            ValidateGlyph(glyph, pixelBase, data.Length, name);
            glyphs[i] = glyph;
            if (glyph.Width > 0 && glyph.Height > 0)
            {
                maxWidth = Math.Max(maxWidth, glyph.Width);
                maxHeight = Math.Max(maxHeight, glyph.Height);
            }
        }

        int cellWidth = maxWidth + 2;
        int cellHeight = maxHeight + 2;
        int atlasWidth = 1;
        while ((atlasWidth / cellWidth) * (atlasWidth / cellHeight) < glyphs.Length)
        {
            if (atlasWidth >= MaxAtlasSize)
            {
                throw new TextureFormatException($"Unpacked font '{name}' exceeds the {MaxAtlasSize}-pixel atlas limit.");
            }

            atlasWidth *= 2;
        }

        int columns = atlasWidth / cellWidth;
        int rows = (glyphs.Length + columns - 1) / columns;
        int atlasHeight = 1;
        while (atlasHeight < rows * cellHeight)
        {
            atlasHeight *= 2;
        }

        Bgra32[] pixels = new Bgra32[atlasWidth * atlasHeight];
        SpriteFrame[] frames = new SpriteFrame[glyphs.Length];
        for (int i = 0; i < glyphs.Length; i++)
        {
            Glyph glyph = glyphs[i];
            int x = ((i % columns) * cellWidth) + 1;
            int y = ((i / columns) * cellHeight) + 1;
            for (int row = 0; row < glyph.Height && glyph.Width > 0; row++)
            {
                int sourceOffset = (int)(pixelBase + glyph.PixelOffset) + (row * glyph.Pitch);
                ReadOnlySpan<byte> source = data.Slice(sourceOffset, glyph.Width * Bgra32.Size);
                int targetOffset = ((y + row) * atlasWidth) + x;
                for (int column = 0; column < glyph.Width; column++)
                {
                    pixels[targetOffset + column] = Bgra32.FromPacked(
                        BinaryPrimitives.ReadUInt32LittleEndian(source[(column * Bgra32.Size)..]));
                }
            }

            frames[i] = new SpriteFrame(
                0, glyph.Width, glyph.Height, glyph.OffsetX, glyph.OffsetY, glyph.Advance, 0,
                (float)x / atlasWidth, (float)y / atlasHeight,
                (float)(x + glyph.Width) / atlasWidth, (float)(y + glyph.Height) / atlasHeight);
        }

        return new SpriteAsset(
            name, SpriteKind.Font, [TextureImage.FromPixels(atlasWidth, atlasHeight, pixels)], frames,
            BinaryPrimitives.ReadUInt16LittleEndian(data[28..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[30..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[26..]),
            flags: 0, palette: null, data.Slice(HeaderSize, CharacterCount).ToArray(), tileLayout: null);
    }

    private static Glyph ReadGlyph(ReadOnlySpan<byte> record) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(record),
        BinaryPrimitives.ReadUInt32LittleEndian(record[4..]),
        BinaryPrimitives.ReadUInt16LittleEndian(record[8..]),
        BinaryPrimitives.ReadUInt16LittleEndian(record[10..]),
        BinaryPrimitives.ReadUInt16LittleEndian(record[12..]),
        BinaryPrimitives.ReadUInt16LittleEndian(record[14..]),
        BinaryPrimitives.ReadUInt16LittleEndian(record[16..]),
        BinaryPrimitives.ReadUInt16LittleEndian(record[18..]));

    private static void ValidateGlyph(Glyph glyph, uint pixelBase, int length, string name)
    {
        long start = (long)pixelBase + glyph.PixelOffset;
        if (start > length)
        {
            throw new TextureFormatException($"Unpacked font '{name}' has a glyph pixel offset outside its payload.");
        }

        if (glyph.Width == 0 || glyph.Height == 0)
        {
            return;
        }

        if (glyph.Format is not ((uint)TextureFormatCode.Bgra8) and not ((uint)TextureFormatCode.Bgra8Alternate))
        {
            throw new TextureFormatException($"Unpacked font '{name}' has unsupported glyph format {glyph.Format}.");
        }

        if (glyph.Width > MaxAtlasSize - 2 || glyph.Height > MaxAtlasSize - 2
            || glyph.Pitch < glyph.Width * Bgra32.Size
            || start + ((long)glyph.Pitch * glyph.Height) > length)
        {
            throw new TextureFormatException($"Unpacked font '{name}' has invalid glyph dimensions, stride or pixel data.");
        }
    }

    private readonly record struct Glyph(
        uint PixelOffset, uint Format, int Pitch, int Width, int Height, int OffsetX, int OffsetY, int Advance);
}
