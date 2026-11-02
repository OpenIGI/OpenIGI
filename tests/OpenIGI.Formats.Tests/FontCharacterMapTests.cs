using System.Buffers.Binary;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Covers the two shapes a font's character map comes in: <c>TRAN</c>, which the 2003 releases ship,
/// and <c>TRN2</c>, which every font in the GOG release of I.G.I. 2 carries instead.
/// </summary>
public sealed class FontCharacterMapTests
{
    // Three glyphs after the blank: '!' is glyph 1, 'A' glyph 2 and an a-acute (0xE1) glyph 3.
    private static readonly ushort[] GlyphCodes = [(ushort)'!', (ushort)'A', 0xE1];

    [Fact]
    public void ATrn2FontMapsEachCodeToTheGlyphAfterItsEntry()
    {
        SpriteAsset font = SpriteLoader.Load(Font("TRN2", Trn2(GlyphCodes)), "trn2.fnt");

        Assert.Equal(1, font.CharacterMap['!']);
        Assert.Equal(2, font.CharacterMap['A']);
        Assert.Equal(3, font.CharacterMap[0xE1]);
        Assert.Equal(0, font.CharacterMap['B']);
    }

    [Fact]
    public void ATrn2FontReadsAsTheTranFontItWasMadeFrom()
    {
        byte[] tran = new byte[256 * sizeof(ushort)];
        for (int k = 0; k < GlyphCodes.Length; k++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(tran.AsSpan(GlyphCodes[k] * sizeof(ushort)), (ushort)(k + 1));
        }

        SpriteAsset fromTran = SpriteLoader.Load(Font("TRAN", tran), "tran.fnt");
        SpriteAsset fromTrn2 = SpriteLoader.Load(Font("TRN2", Trn2(GlyphCodes)), "trn2.fnt");

        Assert.Equal(fromTran.CharacterMap.ToArray(), fromTrn2.CharacterMap.ToArray());
    }

    [Fact]
    public void ACodePastAByteIsLeftOutRatherThanWrapped()
    {
        // 0x141's low byte is 'A'. Wrapped, glyph 3 would overwrite 'A's own glyph 2.
        SpriteAsset font = SpriteLoader.Load(Font("TRN2", Trn2([(ushort)'!', (ushort)'A', 0x0141])), "wide.fnt");

        Assert.Equal(1, font.CharacterMap['!']);
        Assert.Equal(2, font.CharacterMap['A']);
    }

    [Fact]
    public void AFontWithNeitherMapIsRefused()
    {
        TextureFormatException ex = Assert.Throws<TextureFormatException>(
            () => SpriteLoader.Load(Font("XXXX", new byte[8]), "bare.fnt"));

        Assert.Contains("TRN2", ex.Message, StringComparison.Ordinal);
    }

    private static byte[] Trn2(ushort[] codes)
    {
        byte[] chunk = new byte[codes.Length * sizeof(ushort)];
        for (int k = 0; k < codes.Length; k++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(k * sizeof(ushort)), codes[k]);
        }

        return chunk;
    }

    // The chunk order the shipped fonts use: FNTH, ANMF, the map, then one texture.
    private static byte[] Font(string mapTag, byte[] map)
    {
        const int Size = 4;
        int glyphCount = GlyphCodes.Length;

        byte[] fnth = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(fnth, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(fnth.AsSpan(4), (uint)glyphCount);
        BinaryPrimitives.WriteUInt32LittleEndian(fnth.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(fnth.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(fnth.AsSpan(20), Size);

        byte[] anmf = new byte[glyphCount * 40];

        // TEXH as the shipped fonts write it: format 3, the size at +12/+14/+16/+18, 32 bits a pixel.
        byte[] texh = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(texh, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(texh.AsSpan(12), Size);
        BinaryPrimitives.WriteUInt16LittleEndian(texh.AsSpan(14), Size);
        BinaryPrimitives.WriteUInt16LittleEndian(texh.AsSpan(16), Size);
        BinaryPrimitives.WriteUInt16LittleEndian(texh.AsSpan(18), Size);
        BinaryPrimitives.WriteUInt16LittleEndian(texh.AsSpan(22), 32);

        return new IlffWriter("FONT")
            .AddChunk("FNTH", fnth)
            .AddChunk("ANMF", anmf)
            .AddChunk(mapTag, map)
            .AddChunk("TEXH", texh)
            .AddBody(new byte[Size * Size * 4])
            .ToArray();
    }
}
