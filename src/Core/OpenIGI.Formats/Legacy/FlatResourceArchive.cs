using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Legacy;

/// <summary>
/// The flat <c>"RESOURCE"</c> archive: the second container shape the original's resource registrar
/// recognises.
/// </summary>
/// <remarks>
/// <para>
/// <c>Res_Register</c> (<c>0x4B5F80</c>) dispatches on the first bytes of a buffer: <c>ILFF</c> selects
/// the <see cref="Ilff.IresArchive"/> path, the ASCII text <c>RESOURCE</c> selects this one, and
/// anything else is registered as a single opaque blob.
/// </para>
/// <para>
/// <b>Inferred, not confirmed.</b> No retail file uses this layout — it looks like a development-only
/// format — so the record layout comes from reading the original's parser rather than from real bytes.
/// Treat a parse failure here as a reason to re-read <c>0x4B5F80</c>, not as corrupt data.
/// </para>
/// </remarks>
public sealed class FlatResourceArchive
{
    /// <summary>The size in bytes of the archive header.</summary>
    public const int HeaderSize = 32;

    /// <summary>The alignment every entry and payload is padded to.</summary>
    public const int Alignment = 32;

    private static readonly byte[] MagicBytes = "RESOURCE"u8.ToArray();

    private FlatResourceArchive(ReadOnlyCollection<FlatResourceEntry> entries) => Entries = entries;

    /// <summary>Gets the entries, in file order.</summary>
    public IReadOnlyList<FlatResourceEntry> Entries { get; }

    /// <summary>Determines whether a buffer starts with the flat archive magic.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the first eight bytes are <c>RESOURCE</c>.</returns>
    public static bool IsFlatArchive(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && data[..MagicBytes.Length].SequenceEqual(MagicBytes);

    /// <summary>Parses a flat archive.</summary>
    /// <param name="data">The archive bytes.</param>
    /// <returns>The parsed archive.</returns>
    /// <exception cref="FlatResourceFormatException">The bytes are not a valid flat archive.</exception>
    public static FlatResourceArchive Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (!IsFlatArchive(span))
        {
            throw new FlatResourceFormatException("The buffer does not start with a 'RESOURCE' header.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        if (count > int.MaxValue)
        {
            throw new FlatResourceFormatException($"The archive claims {count} entries.");
        }

        List<FlatResourceEntry> entries = [];
        int offset = HeaderSize;

        for (int i = 0; i < (int)count; i++)
        {
            if (offset + 12 > span.Length)
            {
                throw new FlatResourceFormatException($"Entry {i} starts at {offset}, past the end of {span.Length} bytes.");
            }

            int dataSize = ReadBounded(span[(offset + 4)..], $"entry {i} payload size");
            int nameLength = ReadBounded(span[(offset + 8)..], $"entry {i} name length");

            if (nameLength > span.Length - (offset + 12))
            {
                throw new FlatResourceFormatException($"Entry {i} has a {nameLength}-byte name that leaves the archive.");
            }

            string name = ResourcePath.Normalize(DecodeName(span.Slice(offset + 12, nameLength)));

            int payloadOffset = offset + IgiMath.AlignUp(nameLength + 12, Alignment);
            if (payloadOffset > span.Length || dataSize > span.Length - payloadOffset)
            {
                throw new FlatResourceFormatException(
                    $"Entry '{name}' claims {dataSize} bytes at offset {payloadOffset}, past the end of {span.Length} bytes.");
            }

            entries.Add(new FlatResourceEntry(name, data.Slice(payloadOffset, dataSize)));
            offset = payloadOffset + IgiMath.AlignUp(dataSize, Alignment);
        }

        return new FlatResourceArchive(new ReadOnlyCollection<FlatResourceEntry>(entries));
    }

    /// <inheritdoc/>
    public override string ToString() => $"RESOURCE archive, {Entries.Count} entries";

    private static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }

    private static int ReadBounded(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (value > int.MaxValue)
        {
            throw new FlatResourceFormatException($"The {what} {value} is too large to address.");
        }

        return (int)value;
    }
}
