using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>The pieces the wire format is built from: sequence arithmetic, checksums, keyed hash, bit packing.</summary>
public sealed class WirePrimitiveTests
{
    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(2, 65534, true)]
    [InlineData(65534, 2, false)]
    [InlineData(32768, 0, true)]
    [InlineData(32769, 0, false)]
    public void IsNewer_WrapsAroundTheRing(int a, int b, bool expected) =>
        Assert.Equal(expected, SequenceMath.IsNewer((ushort)a, (ushort)b));

    [Fact]
    public void Distance_IsSignedAcrossTheWrap()
    {
        Assert.Equal(4, SequenceMath.Distance(2, 65534));
        Assert.Equal(-4, SequenceMath.Distance(65534, 2));
    }

    [Fact]
    public void SequenceBuffer_ForgetsAnEntryWhoseSlotWasReused()
    {
        SequenceBuffer<string> buffer = new(16);
        buffer.Insert(3, "three");
        buffer.Insert(19, "nineteen");

        Assert.False(buffer.Contains(3));
        Assert.True(buffer.TryGet(19, out string? value));
        Assert.Equal("nineteen", value);
    }

    [Fact]
    public void Crc32_MatchesTheStandardCheckValue() =>
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));

    [Fact]
    public void SipHash_MatchesTheReferenceVector()
    {
        // The SipHash paper's appendix: key 00..0f, message 00..0e.
        byte[] message = new byte[15];
        for (int i = 0; i < message.Length; i++)
        {
            message[i] = (byte)i;
        }

        ulong tag = SipHash.Compute(0x0706050403020100UL, 0x0F0E0D0C0B0A0908UL, message);
        Assert.Equal(0xA129CA6149BE45E5UL, tag);
    }

    [Fact]
    public void BitWriter_RoundTripsEveryKindOfValue()
    {
        BitWriter writer = new(4);
        writer.WriteBool(true);
        writer.WriteBits(0x5, 3);
        writer.WriteRanged(-7, -10, 10);
        writer.WriteVarUInt(300);
        writer.WriteVarInt(-123456);
        writer.WriteSingle(3.25f);
        writer.WriteDouble(-1e300);
        writer.WriteQuantized(0.5f, 0f, 1f, 10);
        writer.WriteString("Jones ✓");
        writer.WriteBits(0xDEADBEEF, 32);

        BitReader reader = new(writer.AsSpan());
        Assert.True(reader.ReadBool());
        Assert.Equal(5u, reader.ReadBits(3));
        Assert.Equal(-7, reader.ReadRanged(-10, 10));
        Assert.Equal(300u, reader.ReadVarUInt());
        Assert.Equal(-123456, reader.ReadVarInt());
        Assert.Equal(3.25f, reader.ReadSingle());
        Assert.Equal(-1e300, reader.ReadDouble());
        Assert.Equal(0.5f, reader.ReadQuantized(0f, 1f, 10), 3);
        Assert.Equal("Jones ✓", reader.ReadString());
        Assert.Equal(0xDEADBEEFu, reader.ReadBits(32));
        Assert.False(reader.IsOverflowed);
    }

    [Fact]
    public void BitReader_FlagsAShortBufferInsteadOfThrowing()
    {
        BitReader reader = new([0xFF]);
        Assert.Equal(0u, reader.ReadBits(16));
        Assert.True(reader.IsOverflowed);
    }

    [Fact]
    public void BitReader_RefusesALengthLongerThanTheBuffer()
    {
        BitWriter writer = new();
        writer.WriteVarUInt(5000);
        BitReader reader = new(writer.AsSpan());

        Assert.Empty(reader.ReadBytes());
        Assert.True(reader.IsOverflowed);
    }

    [Theory]
    [InlineData(-1000f, -1000f, 1000f)]
    [InlineData(1000f, -1000f, 1000f)]
    [InlineData(123.456f, -1000f, 1000f)]
    public void Quantize_KeepsTheEndsExactAndTheRestWithinHalfAStep(float value, float min, float max)
    {
        const int Bits = 16;
        float step = (max - min) / ((1 << Bits) - 1);
        Assert.InRange(Quantize.RoundTrip(value, min, max, Bits), value - (step / 2) - 1e-4f, value + (step / 2) + 1e-4f);
        Assert.Equal(min, Quantize.RoundTrip(min, min, max, Bits));
        Assert.Equal(max, Quantize.RoundTrip(max, min, max, Bits));
    }

    [Fact]
    public void AngleBits_WrapWholeTurns()
    {
        Assert.Equal(Quantize.AngleToBits(0.5f), Quantize.AngleToBits(0.5f + (float)(2 * Math.PI)));
        Assert.Equal(-Math.PI, Quantize.AngleFromBits(Quantize.AngleToBits((float)Math.PI)), 3);
    }

    [Fact]
    public void NetAddress_StoresIPv4Mapped()
    {
        NetAddress address = NetAddress.IPv4(192, 168, 1, 20, 26001);
        Assert.True(address.IsIPv4);
        Assert.Equal("192.168.1.20:26001", address.ToString());
    }
}
