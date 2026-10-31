using System.Text;

namespace OpenIGI.Net;

/// <summary>Reads back what a <see cref="BitWriter"/> packed, in the same order and with the same arguments.</summary>
/// <remarks>
/// The bytes come off the network, so nothing throws on a short buffer: reading past the end yields zeros and sets
/// <see cref="IsOverflowed"/>, which the caller checks once at the end and drops the message for.
/// </remarks>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    /// <summary>Initializes a new instance of the <see cref="BitReader"/> struct.</summary>
    /// <param name="data">The bytes to read.</param>
    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bitPosition = 0;
        IsOverflowed = false;
    }

    /// <summary>Gets a value indicating whether a read went past the end of the bytes.</summary>
    public bool IsOverflowed { get; private set; }

    /// <summary>Gets how many bits are left.</summary>
    public readonly int BitsRemaining => Math.Max(0, (_data.Length * 8) - _bitPosition);

    /// <summary>Reads bits written by <see cref="BitWriter.WriteBits"/>.</summary>
    /// <param name="bits">How many bits, 1 to 32.</param>
    /// <returns>The value, or zero past the end.</returns>
    public uint ReadBits(int bits)
    {
        if (bits < 1 || bits > 32 || bits > BitsRemaining)
        {
            IsOverflowed = true;
            _bitPosition = _data.Length * 8;
            return 0;
        }

        uint value = 0;
        int written = 0;
        while (written < bits)
        {
            int byteIndex = _bitPosition >> 3;
            int bitOffset = _bitPosition & 7;
            int take = Math.Min(8 - bitOffset, bits - written);
            uint chunk = (uint)(_data[byteIndex] >> bitOffset) & ((1u << take) - 1u);
            value |= chunk << written;
            written += take;
            _bitPosition += take;
        }

        return value;
    }

    /// <summary>Reads a bit written by <see cref="BitWriter.WriteBool"/>.</summary>
    /// <returns>The bit.</returns>
    public bool ReadBool() => ReadBits(1) != 0;

    /// <summary>Reads an integer written by <see cref="BitWriter.WriteRanged"/>.</summary>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    /// <returns>The value, clamped into the range.</returns>
    public int ReadRanged(int min, int max)
    {
        int bits = BitWriter.BitsForRange(min, max);
        if (bits == 0)
        {
            return min;
        }

        long value = min + (long)ReadBits(bits);
        return (int)Math.Min(value, max);
    }

    /// <summary>Reads an integer written by <see cref="BitWriter.WriteVarUInt"/>.</summary>
    /// <returns>The value; an over-long encoding sets <see cref="IsOverflowed"/>.</returns>
    public uint ReadVarUInt()
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            uint b = ReadBits(8);
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        IsOverflowed = true;
        return 0;
    }

    /// <summary>Reads an integer written by <see cref="BitWriter.WriteVarInt"/>.</summary>
    /// <returns>The value.</returns>
    public int ReadVarInt()
    {
        uint zig = ReadVarUInt();
        return (int)(zig >> 1) ^ -(int)(zig & 1);
    }

    /// <summary>Reads a float written by <see cref="BitWriter.WriteSingle"/>.</summary>
    /// <returns>The value.</returns>
    public float ReadSingle() => BitConverter.UInt32BitsToSingle(ReadBits(32));

    /// <summary>Reads a double written by <see cref="BitWriter.WriteDouble"/>.</summary>
    /// <returns>The value.</returns>
    public double ReadDouble()
    {
        ulong low = ReadBits(32);
        ulong high = ReadBits(32);
        return BitConverter.UInt64BitsToDouble(low | (high << 32));
    }

    /// <summary>Reads a float written by <see cref="BitWriter.WriteQuantized"/>.</summary>
    /// <param name="min">The range's low end.</param>
    /// <param name="max">The range's high end.</param>
    /// <param name="bits">How many bits, 1 to 32.</param>
    /// <returns>The value as the quantization left it.</returns>
    public float ReadQuantized(float min, float max, int bits) => Quantize.FromBits(ReadBits(bits), min, max, bits);

    /// <summary>Reads bytes written by <see cref="BitWriter.WriteBytes"/>.</summary>
    /// <param name="maxBytes">The most bytes accepted; a longer length sets <see cref="IsOverflowed"/>.</param>
    /// <returns>The bytes, empty on overflow.</returns>
    public byte[] ReadBytes(int maxBytes = 65536)
    {
        uint length = ReadVarUInt();
        if (length > maxBytes || length * 8 > BitsRemaining)
        {
            IsOverflowed = true;
            return [];
        }

        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)ReadBits(8);
        }

        return bytes;
    }

    /// <summary>Reads a string written by <see cref="BitWriter.WriteString"/>.</summary>
    /// <param name="maxBytes">The most bytes accepted.</param>
    /// <returns>The string, empty on overflow.</returns>
    public string ReadString(int maxBytes = 1024) => Encoding.UTF8.GetString(ReadBytes(maxBytes));
}
