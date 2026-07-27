using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.TestSupport;

/// <summary>
/// Builds <c>"RESOURCE"</c> flat archives from the layout documented in <c>docs/formats/ilff.md</c>.
/// </summary>
/// <remarks>
/// No retail file uses this container, so a test built on this writer proves the reader matches the
/// documented layout — not that the layout matches shipped data. The layout itself was inferred from
/// the original's registrar (<c>0x4B5F80</c>).
/// </remarks>
public sealed class FlatResourceWriter
{
    private const int HeaderSize = 32;
    private const int Alignment = 32;
    private const int EntryHeaderSize = 12;

    private readonly List<(string Name, byte[] Data)> _entries = [];

    /// <summary>Appends an entry.</summary>
    /// <param name="name">The resource name.</param>
    /// <param name="data">The payload bytes.</param>
    /// <returns>This writer, for chaining.</returns>
    public FlatResourceWriter Add(string name, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(name);
        _entries.Add((name, data.ToArray()));
        return this;
    }

    /// <summary>Renders the archive.</summary>
    /// <returns>The archive bytes.</returns>
    public byte[] ToArray()
    {
        List<byte> buffer = [.. "RESOURCE"u8];
        Pad(buffer, HeaderSize);
        WriteUInt32(buffer, 8, (uint)_entries.Count);

        foreach ((string name, byte[] data) in _entries)
        {
            byte[] nameBytes = Encoding.Latin1.GetBytes(name + "\0");
            int entryStart = buffer.Count;

            Pad(buffer, entryStart + EntryHeaderSize);
            buffer.AddRange(nameBytes);
            WriteUInt32(buffer, entryStart + 4, (uint)data.Length);
            WriteUInt32(buffer, entryStart + 8, (uint)nameBytes.Length);

            Pad(buffer, entryStart + AlignUp(nameBytes.Length + EntryHeaderSize, Alignment));
            buffer.AddRange(data);
            Pad(buffer, AlignUp(buffer.Count, Alignment));
        }

        return [.. buffer];
    }

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static void Pad(List<byte> buffer, int size)
    {
        while (buffer.Count < size)
        {
            buffer.Add(0);
        }
    }

    private static void WriteUInt32(List<byte> buffer, int offset, uint value)
    {
        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, value);
        for (int i = 0; i < encoded.Length; i++)
        {
            buffer[offset + i] = encoded[i];
        }
    }
}
