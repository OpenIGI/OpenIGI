using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.Core.IO;

/// <summary>
/// A forward cursor over a byte span that reads the primitive types used by the original game's file
/// formats, in either byte order.
/// </summary>
/// <remarks>
/// <para>
/// Both byte orders are needed, sometimes within a single file: MTP packs are IFF-85 containers whose
/// chunk sizes are <em>big-endian</em> while the integers inside chunk payloads are little-endian
/// (see <c>docs/formats/mtp-dat.md</c>). Method names therefore always state the byte order rather
/// than relying on an ambient setting.
/// </para>
/// <para>
/// This is a <see langword="ref"/> struct: it never copies the underlying data and cannot outlive it.
/// </para>
/// </remarks>
public ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="SpanReader"/> struct positioned at the start of <paramref name="data"/>.</summary>
    /// <param name="data">The bytes to read.</param>
    public SpanReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Gets the bytes this reader was created over.</summary>
    public readonly ReadOnlySpan<byte> Data => _data;

    /// <summary>Gets the total number of bytes available.</summary>
    public readonly int Length => _data.Length;

    /// <summary>Gets or sets the current read offset.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or past the end of the data.</exception>
    public int Position
    {
        readonly get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _data.Length);
            _position = value;
        }
    }

    /// <summary>Gets the number of bytes between the current position and the end of the data.</summary>
    public readonly int Remaining => _data.Length - _position;

    /// <summary>Gets a value indicating whether the cursor has reached the end of the data.</summary>
    public readonly bool EndOfData => _position >= _data.Length;

    /// <summary>Advances the cursor without reading.</summary>
    /// <param name="count">The number of bytes to skip.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="EndOfStreamException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public void Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _position = Advance(count);
    }

    /// <summary>Advances the cursor to the next multiple of <paramref name="alignment"/>.</summary>
    /// <param name="alignment">The alignment, which must be a positive power of two.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alignment"/> is not a positive power of two.</exception>
    /// <exception cref="EndOfStreamException">The aligned position lies past the end of the data.</exception>
    /// <remarks>Alignment is relative to the start of this reader's data, not to any enclosing file.</remarks>
    public void Align(int alignment)
    {
        int aligned = Numerics.IgiMath.AlignUp(_position, alignment);
        if (aligned > _data.Length)
        {
            throw new EndOfStreamException(
                $"Aligning to {alignment} from offset {_position} runs past the end of {_data.Length} bytes.");
        }

        _position = aligned;
    }

    /// <summary>Reads one unsigned byte.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">No bytes remain.</exception>
    public byte ReadByte()
    {
        int start = _position;
        _position = Advance(sizeof(byte));
        return _data[start];
    }

    /// <summary>Reads one signed byte.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">No bytes remain.</exception>
    public sbyte ReadSByte() => (sbyte)ReadByte();

    /// <summary>Reads a little-endian unsigned 16-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than two bytes remain.</exception>
    public ushort ReadUInt16LittleEndian() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

    /// <summary>Reads a big-endian unsigned 16-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than two bytes remain.</exception>
    public ushort ReadUInt16BigEndian() => BinaryPrimitives.ReadUInt16BigEndian(Take(sizeof(ushort)));

    /// <summary>Reads a little-endian signed 16-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than two bytes remain.</exception>
    public short ReadInt16LittleEndian() => BinaryPrimitives.ReadInt16LittleEndian(Take(sizeof(short)));

    /// <summary>Reads a big-endian signed 16-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than two bytes remain.</exception>
    public short ReadInt16BigEndian() => BinaryPrimitives.ReadInt16BigEndian(Take(sizeof(short)));

    /// <summary>Reads a little-endian unsigned 32-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public uint ReadUInt32LittleEndian() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));

    /// <summary>Reads a big-endian unsigned 32-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    /// <remarks>This is the form used by IFF-85 chunk sizes in MTP packs.</remarks>
    public uint ReadUInt32BigEndian() => BinaryPrimitives.ReadUInt32BigEndian(Take(sizeof(uint)));

    /// <summary>Reads a little-endian signed 32-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public int ReadInt32LittleEndian() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));

    /// <summary>Reads a big-endian signed 32-bit integer.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public int ReadInt32BigEndian() => BinaryPrimitives.ReadInt32BigEndian(Take(sizeof(int)));

    /// <summary>Reads a little-endian 32-bit floating-point value.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public float ReadSingleLittleEndian() => BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));

    /// <summary>Reads a little-endian 64-bit floating-point value.</summary>
    /// <returns>The value read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than eight bytes remain.</exception>
    public double ReadDoubleLittleEndian() => BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double)));

    /// <summary>Reads a four-character tag in file order.</summary>
    /// <returns>The tag read.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public FourCc ReadFourCc() => FourCc.FromBytes(Take(FourCc.Size));

    /// <summary>Reads <paramref name="count"/> raw bytes.</summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>A slice of the underlying data; no copy is made.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="EndOfStreamException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Take(count);
    }

    /// <summary>Reads a NUL-terminated single-byte string and consumes the terminator.</summary>
    /// <returns>The decoded string, without the terminator.</returns>
    /// <exception cref="EndOfStreamException">No terminator is present before the end of the data.</exception>
    /// <remarks>Bytes are decoded as Latin-1, which is lossless for the single-byte text the original stores.</remarks>
    public string ReadNullTerminatedString()
    {
        int terminator = _data[_position..].IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new EndOfStreamException($"No NUL terminator found after offset {_position}.");
        }

        string result = Encoding.Latin1.GetString(_data.Slice(_position, terminator));
        _position += terminator + 1;
        return result;
    }

    /// <summary>Reads a fixed-length single-byte string, stopping at the first NUL.</summary>
    /// <param name="length">The number of bytes the field occupies; all of them are consumed.</param>
    /// <returns>The decoded string, trimmed at the first NUL.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="EndOfStreamException">Fewer than <paramref name="length"/> bytes remain.</exception>
    public string ReadFixedString(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ReadOnlySpan<byte> field = Take(length);
        int terminator = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? field : field[..terminator]);
    }

    /// <summary>Reads a little-endian unsigned 32-bit integer without moving the cursor.</summary>
    /// <returns>The value at the current position.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public readonly uint PeekUInt32LittleEndian()
    {
        EnsureAvailable(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(_data[_position..]);
    }

    /// <summary>Reads a four-character tag without moving the cursor.</summary>
    /// <returns>The tag at the current position.</returns>
    /// <exception cref="EndOfStreamException">Fewer than four bytes remain.</exception>
    public readonly FourCc PeekFourCc()
    {
        EnsureAvailable(FourCc.Size);
        return FourCc.FromBytes(_data[_position..]);
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        int start = _position;
        _position = Advance(count);
        return _data.Slice(start, count);
    }

    private readonly int Advance(int count)
    {
        EnsureAvailable(count);
        return _position + count;
    }

    private readonly void EnsureAvailable(int count)
    {
        if (count > Remaining)
        {
            throw new EndOfStreamException(
                $"Reading {count} byte(s) at offset {_position} runs past the end of {_data.Length} bytes.");
        }
    }
}
