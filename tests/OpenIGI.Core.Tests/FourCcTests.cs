using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>Verifies the two byte orders a <see cref="FourCc"/> has to bridge.</summary>
public sealed class FourCcTests
{
    [Theory]
    [InlineData("ILFF", 0x46464C49u)]
    [InlineData("NAME", 0x454D414Eu)]
    [InlineData("BODY", 0x59444F42u)]
    [InlineData("PATH", 0x48544150u)]
    [InlineData("CSTR", 0x52545343u)]
    [InlineData("DBUG", 0x47554244u)]
    public void FromString_PacksBytesAsTheOriginalDwordConstant(string text, uint expected) =>
        Assert.Equal(expected, FourCc.FromString(text).Value);

    [Fact]
    public void FromBytes_ReadsFileOrder()
    {
        ReadOnlySpan<byte> bytes = "ILFF"u8;
        Assert.Equal(FourCc.FromString("ILFF"), FourCc.FromBytes(bytes));
    }

    [Theory]
    [InlineData("HSEM", "MESH")]
    [InlineData("XTVM", "MVTX")]
    [InlineData("OCEM", "MECO")]
    [InlineData("TROP", "PORT")]
    public void Reversed_ConvertsMefFileOrderToTheLogicalName(string onDisk, string logical) =>
        Assert.Equal(FourCc.FromString(logical), FourCc.FromString(onDisk).Reversed());

    [Fact]
    public void Value_MatchesTheMefMultiCharacterLiteral()
    {
        // The original's source wrote 'MESH', an MSVC multi-character literal = 0x4D455348. Stored
        // little-endian that is the byte sequence "HSEM", so the raw dword of the on-disk tag equals
        // the code constant and only the character order is reversed.
        Assert.Equal(0x4D455348u, FourCc.FromString("HSEM").Value);
        Assert.Equal(FourCc.FromString("MESH"), FourCc.FromString("HSEM").Reversed());
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        Assert.Equal("IRES", FourCc.FromString("IRES").ToString());
        Assert.Equal("GTT ", FourCc.FromString("GTT ").ToString());
    }

    [Fact]
    public void ToString_RendersNonPrintableBytesAsDots() =>
        Assert.Equal("....", new FourCc(0x01020304u).ToString());

    [Fact]
    public void WriteTo_ProducesTheOriginalBytes()
    {
        Span<byte> buffer = stackalloc byte[4];
        FourCc.FromString("IRES").WriteTo(buffer);
        Assert.True(buffer.SequenceEqual("IRES"u8));
    }

    [Fact]
    public void FromString_RejectsWrongLengths()
    {
        Assert.Throws<ArgumentException>(() => FourCc.FromString("ILF"));
        Assert.Throws<ArgumentException>(() => FourCc.FromString("ILFFF"));
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.True(FourCc.FromString("BODY") == new FourCc(0x59444F42u));
        Assert.True(FourCc.FromString("BODY") != FourCc.FromString("NAME"));
        Assert.Equal(FourCc.FromString("BODY").GetHashCode(), new FourCc(0x59444F42u).GetHashCode());
    }
}
