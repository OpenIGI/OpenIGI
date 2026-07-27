using OpenIGI.Core.IO;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>Verifies the byte-order and cursor behaviour the format readers rely on.</summary>
public sealed class SpanReaderTests
{
    [Fact]
    public void ReadsBothByteOrdersFromTheSameBytes()
    {
        ReadOnlySpan<byte> bytes = [0x12, 0x34, 0x56, 0x78];

        var little = new SpanReader(bytes);
        Assert.Equal(0x78563412u, little.ReadUInt32LittleEndian());

        var big = new SpanReader(bytes);
        Assert.Equal(0x12345678u, big.ReadUInt32BigEndian());
    }

    [Fact]
    public void MixesByteOrdersWithinOneStream()
    {
        // Models an MTP chunk: big-endian IFF size followed by a little-endian payload count.
        ReadOnlySpan<byte> bytes = [0x00, 0x00, 0x00, 0x10, 0x02, 0x00, 0x00, 0x00];
        var reader = new SpanReader(bytes);

        Assert.Equal(0x10u, reader.ReadUInt32BigEndian());
        Assert.Equal(2u, reader.ReadUInt32LittleEndian());
        Assert.True(reader.EndOfData);
    }

    [Fact]
    public void TracksPositionAndRemaining()
    {
        var reader = new SpanReader([1, 2, 3, 4, 5]);
        Assert.Equal(5, reader.Length);
        Assert.Equal(5, reader.Remaining);

        reader.Skip(2);
        Assert.Equal(2, reader.Position);
        Assert.Equal(3, reader.Remaining);

        reader.Position = 5;
        Assert.True(reader.EndOfData);
    }

    [Fact]
    public void ThrowsWhenReadingPastTheEnd()
    {
        var reader = new SpanReader([1, 2, 3]);
        Assert.Throws<EndOfStreamException>(() =>
        {
            var local = new SpanReader([1, 2, 3]);
            local.ReadUInt32LittleEndian();
        });

        reader.Skip(3);
        Assert.Throws<EndOfStreamException>(() =>
        {
            var local = new SpanReader([1, 2, 3]);
            local.Skip(4);
        });
    }

    [Fact]
    public void AlignAdvancesToTheNextMultiple()
    {
        var reader = new SpanReader(new byte[16]);
        reader.Skip(5);
        reader.Align(4);
        Assert.Equal(8, reader.Position);

        reader.Align(4);
        Assert.Equal(8, reader.Position);
    }

    [Fact]
    public void ReadsNullTerminatedStringsAndConsumesTheTerminator()
    {
        var reader = new SpanReader("LOCAL:common/textures/shadow1.tex\0BODY"u8);
        Assert.Equal("LOCAL:common/textures/shadow1.tex", reader.ReadNullTerminatedString());
        Assert.Equal(FourCc.FromString("BODY"), reader.ReadFourCc());
    }

    [Fact]
    public void ReadNullTerminatedString_ThrowsWhenUnterminated()
    {
        Assert.Throws<EndOfStreamException>(() =>
        {
            var local = new SpanReader("no terminator"u8);
            local.ReadNullTerminatedString();
        });
    }

    [Fact]
    public void ReadsFixedLengthStringsAndConsumesThePadding()
    {
        var reader = new SpanReader("name\0\0\0\0rest"u8);
        Assert.Equal("name", reader.ReadFixedString(8));
        Assert.Equal("rest", reader.ReadFixedString(4));
    }

    [Fact]
    public void PeekDoesNotAdvance()
    {
        var reader = new SpanReader("ILFF"u8);
        Assert.Equal(FourCc.FromString("ILFF"), reader.PeekFourCc());
        Assert.Equal(0, reader.Position);
        Assert.Equal(FourCc.FromString("ILFF"), reader.ReadFourCc());
        Assert.Equal(4, reader.Position);
    }

    [Fact]
    public void ReadsSignedAndFloatingPointValues()
    {
        byte[] bytes = new byte[10];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), -1.5f);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (short)-2);
        BitConverter.TryWriteBytes(bytes.AsSpan(6), -3);

        var reader = new SpanReader(bytes);
        Assert.Equal(-1.5f, reader.ReadSingleLittleEndian());
        Assert.Equal((short)-2, reader.ReadInt16LittleEndian());
        Assert.Equal(-3, reader.ReadInt32LittleEndian());
    }
}
