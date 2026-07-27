using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Formats.Ilff;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Parses the fallback bitmap font the original <c>IGI.exe</c> embeds in its image.
/// </summary>
/// <remarks>
/// The font lives at virtual address <c>0x5488DC</c> and is a complete <c>ILFF</c>+<c>FONT</c>
/// container, which makes it the smallest real-world container available. It is read out of the user's
/// own executable rather than committed here, so this test skips without <c>IGI_GAME_PATH</c>.
/// </remarks>
public sealed class EmbeddedFontTests
{
    private const ulong FontVirtualAddress = 0x5488DC;
    private const int FontSize = 0x507C;

    [RequiresGameDataFact]
    public void ParsesTheContainerEmbeddedInTheExecutable()
    {
        IlffContainer font = ReadEmbeddedFont();

        Assert.Equal(IlffTags.Font, font.FormType);
        Assert.Equal(4, font.Align);
        Assert.Equal(FontSize, font.Size);
        Assert.Equal(0, font.Next);
    }

    [RequiresGameDataFact]
    public void ExposesTheFontChunkList()
    {
        IlffContainer font = ReadEmbeddedFont();

        Assert.Equal(
            ["FNTH", "ANMF", "TRAN", "TEXH", "BODY"],
            font.Chunks.Select(c => c.Id.ToString()).ToArray());

        Assert.Equal([0x18, 0xDE8, 0x200, 0x18, 0x4000], font.Chunks.Select(c => c.Size).ToArray());
    }

    [RequiresGameDataFact]
    public void ReadsTheFontHeaderPayload()
    {
        IlffContainer font = ReadEmbeddedFont();

        Assert.True(font.TryFindChunk(FourCc.FromString("FNTH"), out IlffChunk header));
        Assert.Equal(20, header.OffsetInParent);

        ReadOnlySpan<byte> payload = header.Payload.Span;
        uint firstCharacter = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        uint characterCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);

        Assert.Equal(0u, firstCharacter);
        Assert.Equal(0x59u, characterCount);
    }

    [RequiresGameDataFact]
    public void GlyphRecordsCoverEveryCharacter()
    {
        IlffContainer font = ReadEmbeddedFont();

        Assert.True(font.TryFindChunk(FourCc.FromString("FNTH"), out IlffChunk header));
        Assert.True(font.TryFindChunk(FourCc.FromString("ANMF"), out IlffChunk glyphs));

        int characterCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.Payload.Span[4..]);

        // 89 characters x 40 bytes each accounts for the chunk exactly.
        Assert.Equal(0, glyphs.Size % characterCount);
        Assert.Equal(40, glyphs.Size / characterCount);
    }

    private static IlffContainer ReadEmbeddedFont()
    {
        var image = PortableExecutable.Load(GameData.Resolve("IGI.exe"));
        return IlffContainer.Read(image.ReadVirtual(FontVirtualAddress, FontSize));
    }
}
