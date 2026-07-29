using System.Buffers.Binary;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Builds GraphStream bytes for tests. The engine has no writer — a <c>graph&lt;n&gt;.dat</c> is produced by
/// the level tools — so this exists only so a stream can be constructed exactly and read back.
/// </summary>
internal sealed class GraphStreamWriter
{
    private readonly List<byte> _bytes = [];

    /// <summary>Writes a whole field: its name hash, its declared type, then its payload.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="type">The type to declare.</param>
    /// <param name="payload">Writes the payload.</param>
    public void Field(string name, GraphValueType type, Action<GraphStreamWriter> payload)
    {
        RawFieldHash(GraphFieldHash.Of(name));
        Byte(GraphValueType.Bool8, (byte)type);
        payload(this);
    }

    /// <summary>Writes a field name hash as the tagged unsigned integer the stream stores it as.</summary>
    /// <param name="hash">The hash.</param>
    public void RawFieldHash(uint hash) => UInt32Tagged(GraphValueType.UInt32, hash);

    /// <summary>Writes a bare four-byte value with no tag — only the file magic is stored this way.</summary>
    /// <param name="value">The value.</param>
    public void RawUInt32(uint value) => Append(4, span => BinaryPrimitives.WriteUInt32LittleEndian(span, value));

    /// <summary>Writes a tagged single byte.</summary>
    /// <param name="tag">The tag to write.</param>
    /// <param name="value">The payload.</param>
    public void Byte(GraphValueType tag, byte value)
    {
        _bytes.Add((byte)tag);
        _bytes.Add(value);
    }

    /// <summary>Writes a tagged signed 16-bit integer.</summary>
    /// <param name="value">The payload.</param>
    public void Int16(short value)
    {
        _bytes.Add((byte)GraphValueType.Int16);
        Append(2, span => BinaryPrimitives.WriteInt16LittleEndian(span, value));
    }

    /// <summary>Writes a tagged signed 32-bit integer.</summary>
    /// <param name="value">The payload.</param>
    public void Int32(int value)
    {
        _bytes.Add((byte)GraphValueType.Int32);
        Append(4, span => BinaryPrimitives.WriteInt32LittleEndian(span, value));
    }

    /// <summary>Writes a tagged unsigned 32-bit integer.</summary>
    /// <param name="value">The payload.</param>
    public void UInt32(uint value) => UInt32Tagged(GraphValueType.UInt32, value);

    /// <summary>Writes a tagged single-precision float.</summary>
    /// <param name="value">The payload.</param>
    public void Single(float value)
    {
        _bytes.Add((byte)GraphValueType.Real32);
        Append(4, span => BinaryPrimitives.WriteSingleLittleEndian(span, value));
    }

    /// <summary>Writes a tagged double-precision float.</summary>
    /// <param name="value">The payload.</param>
    public void Double(double value)
    {
        _bytes.Add((byte)GraphValueType.Real64);
        Append(8, span => BinaryPrimitives.WriteDoubleLittleEndian(span, value));
    }

    /// <summary>Writes a tagged <c>Real64x3</c>.</summary>
    /// <param name="value">The payload.</param>
    public void Vector3d(Vector3d value)
    {
        _bytes.Add((byte)GraphValueType.Real64x3);
        Append(24, span =>
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span, value.X);
            BinaryPrimitives.WriteDoubleLittleEndian(span[8..], value.Y);
            BinaryPrimitives.WriteDoubleLittleEndian(span[16..], value.Z);
        });
    }

    /// <summary>Writes a tagged string, adding the terminating NUL the length byte counts.</summary>
    /// <param name="value">The payload.</param>
    public void String(string value)
    {
        _bytes.Add((byte)GraphValueType.String8);
        byte[] encoded = System.Text.Encoding.Latin1.GetBytes(value);
        _bytes.Add(checked((byte)(encoded.Length + 1)));
        _bytes.AddRange(encoded);
        _bytes.Add(0);
    }

    /// <summary>Writes a blob — a tagged element type, a tagged length, then the bytes.</summary>
    /// <param name="elementType">The element type to declare.</param>
    /// <param name="payload">The bytes.</param>
    public void Blob(GraphValueType elementType, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Byte(GraphValueType.Bool8, (byte)elementType);
        Int32(payload.Length);
        _bytes.AddRange(payload);
    }

    /// <summary>Gets the bytes written so far.</summary>
    /// <returns>The stream.</returns>
    public byte[] ToArray() => [.. _bytes];

    private void UInt32Tagged(GraphValueType tag, uint value)
    {
        _bytes.Add((byte)tag);
        Append(4, span => BinaryPrimitives.WriteUInt32LittleEndian(span, value));
    }

    private void Append(int size, SpanAction write)
    {
        Span<byte> span = stackalloc byte[size];
        write(span);
        foreach (byte b in span)
        {
            _bytes.Add(b);
        }
    }

    private delegate void SpanAction(Span<byte> span);
}
