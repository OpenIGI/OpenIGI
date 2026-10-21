using System.Buffers.Binary;
using OpenIGI.Core;

namespace OpenIGI.Formats.Iff;

/// <summary>
/// Reads an EA IFF-85 container into a chunk tree.
/// </summary>
/// <remarks>
/// <para>
/// The rules the engine's reader (<c>0x517A00</c> and friends) relies on: a chunk is a four-byte id, a
/// <b>big-endian</b> size that excludes the eight-byte header, and that many payload bytes, **padded to
/// an even boundary**. `FORM`, `LIST` and `CAT ` are groups whose payload opens with a four-byte form
/// type and continues with nested chunks.
/// </para>
/// <para>
/// Payload <em>contents</em> are little-endian — only the chunk sizes are big-endian.
/// </para>
/// </remarks>
public static class IffFile
{
    /// <summary>The size of a chunk header: the id and the big-endian size.</summary>
    public const int ChunkHeaderSize = 8;

    /// <summary>Reads a container.</summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The top-level chunks, in file order.</returns>
    /// <exception cref="IffFormatException">A chunk runs past the end of its parent.</exception>
    public static IReadOnlyList<IffChunk> Read(ReadOnlyMemory<byte> data)
    {
        List<IffChunk> roots = ReadRange(data, 0, data.Length, depth: 0, out _);
        if (roots.Count == 0)
        {
            throw new IffFormatException("The data holds no IFF chunk.");
        }

        return roots;
    }

    /// <summary>Reads a container and returns its first group of an expected form type.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="formType">The form type the file is expected to open with.</param>
    /// <returns>The group.</returns>
    /// <exception cref="IffFormatException">The file is malformed or is not of that type.</exception>
    /// <remarks>
    /// The original reports these two failures separately — <c>"File %s is not IFF file."</c> and
    /// <c>"IFF File %s is not of correct type."</c> — and so does this, through the message.
    /// </remarks>
    public static IffChunk ReadForm(ReadOnlyMemory<byte> data, FourCc formType)
    {
        foreach (IffChunk root in Read(data))
        {
            if (root.IsGroup && root.FormType == formType)
            {
                return root;
            }
        }

        throw new IffFormatException($"The IFF container is not of type '{formType}'.");
    }

    // The nesting cap is a guard against a malformed file that recurses forever; retail animation sets
    // nest four deep.
    private const int MaximumDepth = 32;

    private static List<IffChunk> ReadRange(
        ReadOnlyMemory<byte> data,
        int offset,
        int end,
        int depth,
        out int consumedTo)
    {
        if (depth > MaximumDepth)
        {
            throw new IffFormatException($"IFF nesting deeper than {MaximumDepth} levels.");
        }

        List<IffChunk> into = [];
        while (offset + ChunkHeaderSize <= end)
        {
            ReadOnlySpan<byte> span = data.Span;
            FourCc id = FourCc.FromBytes(span.Slice(offset, 4));
            uint rawSize = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + 4, 4));
            int body = offset + ChunkHeaderSize;

            if (rawSize > (uint)(end - body))
            {
                // The retail animation sets back-patch inner FORM sizes but not the outermost one:
                // 002.iff's `FORM BOBJ` claims 588 bytes while the `FORM BOAL` inside it claims
                // 155228 and runs far past that. The engine never notices because its navigator
                // (0x517940/0x517980) scans for the next chunk with an id rather than enforcing
                // containment. So when a child disagrees with its parent, the child wins — that is
                // the reading that recovers the real tree — as long as it still fits the file.
                if (rawSize > (uint)(data.Length - body))
                {
                    throw new IffFormatException(
                        $"Chunk '{id}' at {offset} claims {rawSize} bytes, past the end of the data.");
                }

                end = body + (int)rawSize;
            }

            int size = (int)rawSize;
            bool isGroup = id == IffChunk.Form || id == IffChunk.List || id == IffChunk.Catalog;

            IffChunk chunk;
            if (isGroup)
            {
                if (size < 4)
                {
                    throw new IffFormatException($"Group chunk '{id}' at {offset} has no form type.");
                }

                FourCc formType = FourCc.FromBytes(span.Slice(body, 4));
                chunk = new IffChunk(id, formType, data.Slice(body + 4, size - 4));
                List<IffChunk> children = ReadRange(data, body + 4, body + size, depth + 1, out int childEnd);
                foreach (IffChunk child in children)
                {
                    chunk.Add(child);
                }

                // A nested group may have corrected this one's extent (see above). Resume where the
                // descent actually stopped, or the scan walks back into the group it just read and
                // yields every chunk inside it a second time.
                size = Math.Max(size, childEnd - body);
            }
            else
            {
                chunk = new IffChunk(id, default, data.Slice(body, size));
            }

            into.Add(chunk);

            // Odd-sized chunks are followed by a pad byte that is not part of the payload.
            offset = body + size + (size & 1);
            if (offset > end)
            {
                end = offset;
            }
        }

        consumedTo = offset;
        return into;
    }
}
