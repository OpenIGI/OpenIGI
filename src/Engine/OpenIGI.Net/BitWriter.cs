using System.Numerics;
using System.Text;

namespace OpenIGI.Net;

/// <summary>Packs values into a growing byte buffer at bit granularity, least significant bit first.</summary>
/// <remarks>
/// Reused between messages: <see cref="Reset"/> keeps the buffer. Every value has a twin on <see cref="BitReader"/> that
/// reads it back with the same arguments.
/// </remarks>
public sealed class BitWriter
{
    private byte[] _buffer;
    private ulong _scratch;
    private int _scratchBits;
    private int _bytes;

    /// <summary>Initializes a new instance of the <see cref="BitWriter"/> class.</summary>
    /// <param name="capacity">The bytes to reserve at first.</param>
    public BitWriter(int capacity = 256)
    {
        _buffer = new byte[Math.Max(capacity, 8)];
    }

    /// <summary>Gets how many bits have been written.</summary>
    public int BitCount => (_bytes * 8) + _scratchBits;

    /// <summary>Gets how many bytes the written bits fill, the last one padded with zeros.</summary>
    public int ByteCount => _bytes + ((_scratchBits + 7) >> 3);

    /// <summary>Forgets everything written, keeping the buffer.</summary>
    public void Reset()
    {
        _scratch = 0;
        _scratchBits = 0;
        _bytes = 0;
    }

    /// <summary>Writes the low bits of a value.</summary>
    /// <param name="value">The value; bits above <paramref name="bits"/> are ignored.</param>
    /// <param name="bits">How many bits, 1 to 32.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bits"/> is outside 1 to 32.</exception>
    public void WriteBits(uint value, int bits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bits, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, 32);

        ulong masked = bits == 32 ? value : value & ((1u << bits) - 1u);
        _scratch |= masked << _scratchBits;
        _scratchBits += bits;
        while (_scratchBits >= 8)
        {
            EnsureCapacity(_bytes + 1);
            _buffer[_bytes++] = (byte)_scratch;
            _scratch >>= 8;
            _scratchBits -= 8;
        }
    }

    /// <summary>Writes one bit.</summary>
    /// <param name="value">The bit.</param>
    public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

    /// <summary>Writes an integer known to lie in a range, in as few bits as the range needs.</summary>
    /// <param name="value">The value, clamped into the range.</param>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    public void WriteRanged(int value, int min, int max)
    {
        int bits = BitsForRange(min, max);
        if (bits == 0)
        {
            return;
        }

        WriteBits((uint)(Math.Clamp(value, min, max) - min), bits);
    }

    /// <summary>Writes an unsigned integer in seven-bit groups, small values in one byte.</summary>
    /// <param name="value">The value.</param>
    public void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            WriteBits((value & 0x7F) | 0x80, 8);
            value >>= 7;
        }

        WriteBits(value, 8);
    }

    /// <summary>Writes a signed integer zig-zagged so small magnitudes of either sign stay short.</summary>
    /// <param name="value">The value.</param>
    public void WriteVarInt(int value) => WriteVarUInt((uint)((value << 1) ^ (value >> 31)));

    /// <summary>Writes a float's bits exactly.</summary>
    /// <param name="value">The value.</param>
    public void WriteSingle(float value) => WriteBits(BitConverter.SingleToUInt32Bits(value), 32);

    /// <summary>Writes a double's bits exactly.</summary>
    /// <param name="value">The value.</param>
    public void WriteDouble(double value)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        WriteBits((uint)bits, 32);
        WriteBits((uint)(bits >> 32), 32);
    }

    /// <summary>Writes a float quantized over a range.</summary>
    /// <param name="value">The value, clamped into the range.</param>
    /// <param name="min">The range's low end.</param>
    /// <param name="max">The range's high end.</param>
    /// <param name="bits">How many bits, 1 to 32.</param>
    public void WriteQuantized(float value, float min, float max, int bits) =>
        WriteBits(Quantize.ToBits(value, min, max, bits), bits);

    /// <summary>Writes bytes after a length.</summary>
    /// <param name="data">The bytes.</param>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        WriteVarUInt((uint)data.Length);
        foreach (byte b in data)
        {
            WriteBits(b, 8);
        }
    }

    /// <summary>Writes a string as UTF-8 after its byte length, cut to a maximum.</summary>
    /// <param name="value">The string.</param>
    /// <param name="maxBytes">The most bytes to send.</param>
    public void WriteString(string value, int maxBytes = 1024)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteBytes(bytes.AsSpan(0, Math.Min(bytes.Length, maxBytes)));
    }

    /// <summary>Gets the written bytes, the last one padded with zeros.</summary>
    /// <returns>A span over the writer's buffer, valid until the next write.</returns>
    public ReadOnlySpan<byte> AsSpan()
    {
        if (_scratchBits > 0)
        {
            EnsureCapacity(_bytes + 1);
            _buffer[_bytes] = (byte)_scratch;
        }

        return _buffer.AsSpan(0, ByteCount);
    }

    /// <summary>Copies the written bytes out.</summary>
    /// <returns>A new array.</returns>
    public byte[] ToArray() => AsSpan().ToArray();

    /// <summary>Gets how many bits a value in a range takes.</summary>
    /// <param name="min">The smallest value.</param>
    /// <param name="max">The largest value.</param>
    /// <returns>The bit count, 0 when the range holds one value.</returns>
    public static int BitsForRange(int min, int max)
    {
        uint span = (uint)(max - min);
        return span == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(span);
    }

    private void EnsureCapacity(int bytes)
    {
        if (bytes > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(bytes, _buffer.Length * 2));
        }
    }
}
