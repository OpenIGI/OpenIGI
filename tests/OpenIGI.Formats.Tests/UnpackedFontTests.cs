using System.Buffers.Binary;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Exercises unpacked LOOP 10 fonts using synthetic pixels, without proprietary assets.</summary>
public sealed class UnpackedFontTests
{
    private const int TableOffset = 292;
    private const int RecordSize = 20;
    private const int PixelBase = TableOffset + (4 * RecordSize);

    [Fact]
    public void UnpackedGlyphsRetainTheirMappingsMetricsAndPixelsInOneAtlas()
    {
        SpriteAsset font = SpriteLoader.Load(Font(), "unpacked.fnt");

        Assert.True(font.IsFont);
        Assert.Equal(SpriteKind.Font, font.Kind);
        Assert.Equal(3, font.Frames.Count);
        TextureImage atlas = Assert.Single(font.Textures);
        Assert.Equal(32, font.DefaultWidth);
        Assert.Equal(15, font.DefaultHeight);
        Assert.Equal(4, font.DefaultAdvance);
        Assert.Equal(0u, font.Flags);

        SpriteFrame first = font.GetGlyph('A');
        Assert.Equal(font.Frames[0], first);
        Assert.Equal(2, first.Width);
        Assert.Equal(2, first.Height);
        Assert.Equal(1, first.OffsetX);
        Assert.Equal(3, first.OffsetY);
        Assert.Equal(5, first.Advance);
        Assert.Equal(new Bgra32(1, 2, 3, 4), Pixel(atlas, first, 0, 0));
        Assert.Equal(new Bgra32(5, 6, 7, 8), Pixel(atlas, first, 1, 0));
        Assert.Equal(new Bgra32(9, 10, 11, 12), Pixel(atlas, first, 0, 1));
        Assert.Equal(new Bgra32(13, 14, 15, 16), Pixel(atlas, first, 1, 1));

        SpriteFrame second = font.GetGlyph('Z');
        Assert.Equal(font.Frames[1], second);
        Assert.Equal(new Bgra32(21, 22, 23, 24), Pixel(atlas, second, 0, 0));
        Assert.Equal(7, second.Advance);
        Assert.Equal(second, font.GetGlyph('\u00ff'));
        Assert.Equal(first, font.GetGlyph('\u0141'));

        // The skipped source record zero must never become 'A', and padding must not paint a cell.
        int x = (int)(first.U0 * atlas.Width);
        int y = (int)(first.V0 * atlas.Height);
        Assert.Equal(Bgra32.Transparent, atlas.Pixels[(y * atlas.Width) + x - 1]);
        Assert.Equal(Bgra32.Transparent, atlas.Pixels[(y * atlas.Width) + x + first.Width]);
    }

    [Fact]
    public void AnUnmappedCharacterUsesTheDefaultAndAnEmptyGlyphKeepsItsAdvance()
    {
        SpriteAsset font = SpriteLoader.Load(Font());

        Assert.Equal(font.DefaultFrame, font.GetGlyph(' '));
        Assert.Equal(4, font.GetGlyph(' ').Advance);
        Assert.Equal(0.0f, font.GetGlyph(' ').U1);
        Assert.Equal(font.Frames[2], font.GetGlyph('#'));
        Assert.True(font.GetGlyph('#').IsEmpty);
        Assert.Equal(9, font.GetGlyph('#').Advance);
    }

    [Theory]
    [InlineData(16, 0u)]
    [InlineData(16, 256u)]
    [InlineData(16, uint.MaxValue)]
    [InlineData(20, 0u)]
    [InlineData(20, PixelBase - 1u)]
    [InlineData(20, uint.MaxValue)]
    [InlineData(TableOffset + RecordSize, uint.MaxValue)]
    [InlineData(TableOffset + RecordSize + 4, 1u)]
    [InlineData(TableOffset + RecordSize + 4, 67u)]
    public void InvalidCountsOffsetsAndFormatsAreReportedAsFormatErrors(int offset, uint value)
    {
        byte[] data = Font();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

        Assert.Throws<TextureFormatException>(() => SpriteLoader.Load(data, "invalid.fnt"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(TableOffset - 1)]
    [InlineData(PixelBase - 1)]
    [InlineData(PixelBase + 27)]
    public void TruncatedContainersAreReportedAsFormatErrors(int length)
    {
        byte[] data = Font()[..length];

        Assert.Throws<TextureFormatException>(() => SpriteLoader.Load(data, "short.fnt"));
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(10, ushort.MaxValue)]
    [InlineData(12, ushort.MaxValue)]
    public void InvalidGlyphGeometryIsRejectedBeforeAllocatingAnAtlas(int field, ushort value)
    {
        byte[] data = Font();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(TableOffset + RecordSize + field), value);

        Assert.Throws<TextureFormatException>(() => SpriteLoader.Load(data));
    }

    [Fact]
    public void AnOutOfRangeCharacterMappingFallsBackAsItDoesForPackedFonts()
    {
        byte[] data = Font();
        data[36 + 'B'] = 255;
        SpriteAsset font = SpriteLoader.Load(data);

        Assert.Equal(font.DefaultFrame, font.GetGlyph('B'));
    }

    [Fact]
    public void DisparateGlyphSizesCannotForceAnUnboundedAtlasAllocation()
    {
        // Two thin glyphs need only 24 KB of input, but their combined maximum width and height
        // would create three 3002x3002 cells without a bound on the normalized atlas.
        byte[] data = new byte[PixelBase + 24000];
        Font().AsSpan(0, PixelBase).CopyTo(data);
        WriteGlyph(data.AsSpan(TableOffset + RecordSize), 0, 3, 12000, 3000, 1, 0, 0, 1);
        WriteGlyph(data.AsSpan(TableOffset + (2 * RecordSize)), 12000, 3, 4, 1, 3000, 0, 0, 1);
        WriteGlyph(data.AsSpan(TableOffset + (3 * RecordSize)), 24000, 0, 0, 0, 0, 0, 0, 1);

        TextureFormatException error = Assert.Throws<TextureFormatException>(() => SpriteLoader.Load(data));

        Assert.Contains("atlas limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GlyphOffsetsAreSigned()
    {
        // A glyph may sit left of or above its pen position, as LOOP 9's signed offsets allow.
        byte[] data = Font();
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(TableOffset + RecordSize + 14), -2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(TableOffset + RecordSize + 16), -3);
        SpriteFrame glyph = SpriteLoader.Load(data).GetGlyph('A');

        Assert.Equal(-2, glyph.OffsetX);
        Assert.Equal(-3, glyph.OffsetY);
    }

    [Fact]
    public void VersionFiveRemainsUnsupported()
    {
        byte[] data = Font();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 5);

        Assert.Throws<TextureFormatException>(() => SpriteLoader.Load(data));
    }

    private static Bgra32 Pixel(TextureImage atlas, SpriteFrame frame, int column, int row)
    {
        int x = (int)(frame.U0 * atlas.Width) + column;
        int y = (int)(frame.V0 * atlas.Height) + row;
        return atlas.Pixels[(y * atlas.Width) + x];
    }

    private static byte[] Font()
    {
        byte[] data = new byte[PixelBase + 28];
        "LOOP"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), PixelBase);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(30), 15);
        data[36 + 'A'] = 1;
        data[36 + 'Z'] = 2;
        data[36 + '#'] = 3;
        data[36 + 255] = 2;

        WriteGlyph(data.AsSpan(TableOffset + RecordSize), 0, 3, 12, 2, 2, 1, 3, 5);
        WriteGlyph(data.AsSpan(TableOffset + (2 * RecordSize)), 24, 4, 4, 1, 1, 0, 0, 7);
        WriteGlyph(data.AsSpan(TableOffset + (3 * RecordSize)), 28, 0, 0, 0, 0, 0, 0, 9);
        byte[] pixels =
        [
            1, 2, 3, 4, 5, 6, 7, 8, 255, 255, 255, 255,
            9, 10, 11, 12, 13, 14, 15, 16, 255, 255, 255, 255,
            21, 22, 23, 24,
        ];
        pixels.CopyTo(data, PixelBase);
        return data;
    }

    private static void WriteGlyph(
        Span<byte> record, uint offset, uint format, ushort pitch, ushort width, ushort height,
        ushort offsetX, ushort offsetY, ushort advance)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(record, offset);
        BinaryPrimitives.WriteUInt32LittleEndian(record[4..], format);
        BinaryPrimitives.WriteUInt16LittleEndian(record[8..], pitch);
        BinaryPrimitives.WriteUInt16LittleEndian(record[10..], width);
        BinaryPrimitives.WriteUInt16LittleEndian(record[12..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(record[14..], offsetX);
        BinaryPrimitives.WriteUInt16LittleEndian(record[16..], offsetY);
        BinaryPrimitives.WriteUInt16LittleEndian(record[18..], advance);
    }
}
