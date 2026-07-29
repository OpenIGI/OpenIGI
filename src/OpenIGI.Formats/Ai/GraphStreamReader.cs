using System.Buffers.Binary;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>
/// The tagged-value reader a <c>graph&lt;n&gt;.dat</c> is built from — the engine's own "GraphStream".
/// </summary>
/// <remarks>
/// <para>
/// <b>Every</b> value in the stream is tagged: one byte naming a <see cref="GraphValueType"/>, then the
/// payload. Nothing else delimits anything. A field is three consecutive tagged values — its name hash,
/// its declared type, and its data — which is why an unknown field can be skipped by consuming exactly
/// two of them (<c>0x4FA3B0</c> called with a count of 2).
/// </para>
/// <para>
/// <b>The tag on the payload is never checked.</b> Each reader in <c>0x4F9CD0</c>'s dispatch is chosen by
/// the field's <em>declared</em> type and then reads a fixed number of bytes, discarding the first. So a
/// stream whose payload tag disagrees with its declared type is read as the declared type, silently. The
/// peek at <c>0x4FAA10</c> looks like the missing check but is not one: it reads the tag and immediately
/// seeks back one byte, and <c>0x4F9CD0</c> throws its return value away. Its one real use is the
/// positional variant, which peeks to decide whether an optional field is present at all.
/// </para>
/// <para>
/// This reader keeps the same shape — <see cref="PeekTag"/> does not consume — and additionally exposes
/// the payload tag it skipped so a caller can assert the two agree. Retail data always agrees; the
/// corpus test pins that.
/// </para>
/// </remarks>
public sealed class GraphStreamReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="GraphStreamReader"/> class.</summary>
    /// <param name="data">The stream bytes, positioned at the first tagged value.</param>
    public GraphStreamReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>Gets the current byte offset into the stream.</summary>
    public int Position => _position;

    /// <summary>Gets the stream's length in bytes.</summary>
    public int Length => _data.Length;

    /// <summary>Gets a value indicating whether the cursor has reached the end.</summary>
    /// <remarks>
    /// The record loop tests this before every field, so a stream whose last field ends exactly on the
    /// last byte is sound. Retail data is exact in all 300 files.
    /// </remarks>
    public bool IsAtEnd => _position >= _data.Length;

    /// <summary>Gets the tag of the value the cursor is on, without consuming it.</summary>
    /// <returns>The tag byte.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public GraphValueType PeekTag()
    {
        if (_position >= _data.Length)
        {
            throw new GraphFormatException($"Peeked past the end of a {_data.Length}-byte graph stream.");
        }

        return (GraphValueType)_data.Span[_position];
    }

    /// <summary>Reads a tagged single byte.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public byte ReadByte() => Payload(1)[0];

    /// <summary>Reads a tagged signed 16-bit integer.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Payload(2));

    /// <summary>Reads a tagged signed 32-bit integer.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Payload(4));

    /// <summary>Reads a tagged unsigned 32-bit integer — the form a field's name hash is stored in.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Payload(4));

    /// <summary>Reads a tagged single-precision float.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Payload(4));

    /// <summary>Reads a tagged double-precision float.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Payload(8));

    /// <summary>Reads a tagged <c>Real64x3</c> world position.</summary>
    /// <returns>The payload.</returns>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public Vector3d ReadVector3d()
    {
        ReadOnlySpan<byte> p = Payload(24);
        return new Vector3d(
            BinaryPrimitives.ReadDoubleLittleEndian(p),
            BinaryPrimitives.ReadDoubleLittleEndian(p[8..]),
            BinaryPrimitives.ReadDoubleLittleEndian(p[16..]));
    }

    /// <summary>Reads a tagged length-prefixed string.</summary>
    /// <returns>The payload with its terminating NUL and anything past it removed.</returns>
    /// <remarks>
    /// The length byte <b>includes</b> the NUL, so an empty string is stored as a length of one. Decoding
    /// is Latin-1 so bytes round-trip and ordinal comparisons match the original's <c>strcmp</c>, the same
    /// rule the script VM uses.
    /// </remarks>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public string ReadString()
    {
        Advance(1);
        int length = Take(1)[0];
        ReadOnlySpan<byte> bytes = Take(length);
        int nul = bytes.IndexOf((byte)0);
        if (nul >= 0)
        {
            bytes = bytes[..nul];
        }

        return System.Text.Encoding.Latin1.GetString(bytes);
    }

    /// <summary>Reads a tagged blob.</summary>
    /// <param name="elementType">Receives the blob's tagged element type.</param>
    /// <param name="declaredLength">Receives the length the blob declares.</param>
    /// <param name="expectedLength">
    /// How many bytes the caller intends to consume. The original ignores the declared length entirely and
    /// reads exactly this many (<c>0x4FA9D0</c>), which is what this reproduces.
    /// </param>
    /// <returns>The blob bytes, <paramref name="expectedLength"/> of them.</returns>
    /// <remarks>
    /// A blob is the one value with <b>no tag of its own</b>: its seven-byte header is itself two tagged
    /// values, an element type and a length. <see cref="SkipValue"/> does not agree with that — see its
    /// remarks — and only this form occurs in retail data.
    /// </remarks>
    /// <exception cref="GraphFormatException">The stream is exhausted or the length is negative.</exception>
    public ReadOnlyMemory<byte> ReadBlob(out GraphValueType elementType, out int declaredLength, int expectedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);

        elementType = (GraphValueType)ReadByte();
        declaredLength = ReadInt32();
        int start = _position;
        Take(expectedLength);
        return _data.Slice(start, expectedLength);
    }

    /// <summary>Skips one tagged value of any type, using only its tag to find its width.</summary>
    /// <returns>The tag that was skipped.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>0x4FA3B0</c>'s body for a single value. An <b>unrecognised tag advances by nothing but
    /// the tag byte</b> — the original's switch has no default arm — so a corrupt stream walks forward one
    /// byte at a time rather than stopping. Reproduced, because it is what decides whether a damaged file
    /// loads at all.
    /// </para>
    /// <para>
    /// <b>The blob arm disagrees with <see cref="ReadBlob"/>, in the original as much as here.</b> Skipping
    /// expects <c>{tag, u32 length, payload}</c>; reading expects <c>{tagged element type, tagged length,
    /// payload}</c>, which is two bytes longer and starts with the element type's tag rather than a
    /// <see cref="GraphValueType.Blob"/> one. So skipping an unknown field whose data is a blob
    /// desynchronises the stream.
    /// It cannot happen on retail data — every field name in all 300 files is one of the eleven registered
    /// ones, which the corpus test asserts — but it is an inconsistency in the original, not a
    /// simplification here.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphFormatException">The stream is exhausted.</exception>
    public GraphValueType SkipValue()
    {
        var tag = PeekTag();
        Advance(1);
        switch (tag)
        {
            case GraphValueType.Bool8:
            case GraphValueType.Int8:
                Take(1);
                break;
            case GraphValueType.UInt16:
            case GraphValueType.Int16:
                Take(2);
                break;
            case GraphValueType.UInt32:
            case GraphValueType.Int32:
            case GraphValueType.Real32:
                Take(4);
                break;
            case GraphValueType.Real64:
                Take(8);
                break;
            case GraphValueType.Real64x3:
                Take(24);
                break;
            case GraphValueType.String8:
                Take(Take(1)[0]);
                break;
            case GraphValueType.Blob:
                Take(BinaryPrimitives.ReadInt32LittleEndian(Take(4)));
                break;
            default:
                break;
        }

        return tag;
    }

    /// <summary>Reads the four-byte value the whole file opens with, without a tag.</summary>
    /// <returns>The magic.</returns>
    /// <exception cref="GraphFormatException">The stream is shorter than four bytes.</exception>
    public uint ReadRawUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    /// <summary>Rewinds the cursor to the start of the stream.</summary>
    /// <remarks>
    /// The loader does exactly this when the magic does not match, then reads the file positionally
    /// instead — the magic is part of the first field of the positional variant, not a header it skips.
    /// </remarks>
    public void Rewind() => _position = 0;

    private ReadOnlySpan<byte> Payload(int size)
    {
        Advance(1);
        return Take(size);
    }

    private void Advance(int count)
    {
        Take(count);
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > _data.Length - _position)
        {
            throw new GraphFormatException(
                $"Graph stream wanted {count} bytes at offset {_position} but only {_data.Length - _position} remain.");
        }

        ReadOnlySpan<byte> span = _data.Span.Slice(_position, count);
        _position += count;
        return span;
    }
}
