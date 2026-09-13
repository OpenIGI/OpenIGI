using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ilff;

/// <summary>
/// Builds an <c>ILFF</c>+<c>IRES</c> archive: the <c>.res</c> files the game loads its resources from.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="IresArchive"/>, and written to the same description: a stream of
/// <c>NAME</c> chunks, each followed by the payload chunks it names.
/// </para>
/// <para>
/// ALIGNMENT IS PER CHUNK, not per archive, and that is the one rule it is easy to get wrong because 91
/// of the game's 92 archives hide it — they align everything to 4, so nothing distinguishes the chunk's
/// alignment from the container's. The 92nd, <c>common/textures/common.res</c>, aligns its container and
/// every <c>BODY</c> to 32 while leaving each <c>NAME</c> at 4. A writer that assumes one number for the
/// whole file reproduces that archive 7,776 bytes too long.
/// </para>
/// <para>
/// So: a payload sits at <c>alignUp(chunkOffset + 16, chunkAlign)</c>, and a chunk advances by
/// <c>alignUp(payloadEnd, chunkAlign) - chunkOffset</c>, with zero on the last one to end the walk.
/// MEASURED: with that rule every one of the retail archives rebuilds from its own entries byte for
/// byte, alignment 32 included.
/// </para>
/// <para>
/// The one thing the retail files disagree with each other about is the FINAL chunk: 61 pad it out to
/// its alignment and 31 stop at the last payload byte. Both read back the same, so this pads, and the
/// difference is at most a few bytes on the end of a file.
/// </para>
/// <para>
/// ENTRY ORDER IS PRESERVED, and it is not cosmetic. The original hashes names into one table where the
/// FIRST registration wins, so an archive holding a name twice resolves to whichever copy came first.
/// Rebuilding in a different order therefore changes which picture a name means, which is invisible at
/// every later stage because both copies decode perfectly well.
/// </para>
/// <para>
/// Names are written back exactly as they were read, <c>LOCAL:</c> namespace prefix included. The prefix
/// is part of the name the game looks a resource up by, not decoration on the path.
/// </para>
/// </remarks>
public static class IresWriter
{
    /// <summary>The alignment 91 of the game's 92 archives use, and the default for a new one.</summary>
    public const int DefaultAlignment = 4;

    /// <summary>The alignment every <c>NAME</c> chunk uses, whatever the archive aligns its payloads to.</summary>
    /// <remarks>
    /// Four in every retail archive, including the one that aligns its bodies to 32. A name is a short
    /// string and there is nothing to gain by padding it out to a cache line.
    /// </remarks>
    public const int NameAlignment = 4;

    /// <summary>One resource to write into an archive.</summary>
    /// <param name="Name">The resource name, namespace prefix included, exactly as it should be looked up.</param>
    /// <param name="Kind">Which payload chunk carries it.</param>
    /// <param name="Data">The payload bytes.</param>
    public readonly record struct Entry(string Name, IresEntryKind Kind, ReadOnlyMemory<byte> Data);

    /// <summary>Encodes an archive holding the given entries, in order.</summary>
    /// <param name="entries">The entries to write.</param>
    /// <param name="alignment">What to align payloads to; a power of two, and the container's alignment too.</param>
    /// <returns>The complete archive bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="IlffFormatException">
    /// The alignment is not a power of two, an entry has an empty name, or the archive would exceed what
    /// its own size field can record.
    /// </exception>
    public static byte[] Write(IReadOnlyList<Entry> entries, int alignment = DefaultAlignment)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!IgiMath.IsPowerOfTwo(alignment) || alignment < NameAlignment)
        {
            throw new IlffFormatException(
                $"Archive alignment {alignment} must be a power of two and at least {NameAlignment}, "
                    + "which is what a NAME chunk uses.");
        }

        // Laid out first, then written, because a chunk's advance depends on where its payload landed
        // and the container's size is only known once the last one has.
        List<(FourCc Id, int Align, ReadOnlyMemory<byte> Payload)> chunks = new(entries.Count * 2);

        foreach (Entry entry in entries)
        {
            chunks.Add((IlffTags.Name, NameAlignment, NameBytes(entry)));
            chunks.Add((PayloadTag(entry.Kind), alignment, entry.Data));
        }

        if (chunks.Count == 0)
        {
            throw new IlffFormatException("An archive needs at least one entry; an empty container has no chunk to end the walk.");
        }

        int[] offsets = new int[chunks.Count];
        int[] payloads = new int[chunks.Count];
        long cursor = IgiMath.AlignUp(IlffContainer.HeaderSize, alignment);

        for (int i = 0; i < chunks.Count; i++)
        {
            (_, int align, ReadOnlyMemory<byte> payload) = chunks[i];

            long start = cursor;
            long at = IgiMath.AlignUp(checked((int)(start + IlffChunk.HeaderSize)), align);

            offsets[i] = checked((int)start);
            payloads[i] = checked((int)at);
            cursor = IgiMath.AlignUp(checked((int)(at + payload.Length)), align);

            if (cursor > int.MaxValue)
            {
                throw new IlffFormatException(
                    $"The archive passes the {int.MaxValue}-byte limit its own size field can record.");
            }
        }

        byte[] file = new byte[cursor];
        Span<byte> span = file;

        IlffTags.Ilff.WriteTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)alignment);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0);
        IlffTags.Ires.WriteTo(span[16..]);

        for (int i = 0; i < chunks.Count; i++)
        {
            (FourCc id, int align, ReadOnlyMemory<byte> payload) = chunks[i];
            bool last = i == chunks.Count - 1;
            int offset = offsets[i];

            id.WriteTo(span[offset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 4)..], (uint)payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 8)..], (uint)align);

            // Zero on the last chunk is what ends the walk. The true distance there would send the
            // reader into whatever follows the archive, which for the final chunk of a file is nothing.
            int next = last ? 0 : offsets[i + 1] - offset;
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 12)..], (uint)next);

            payload.Span.CopyTo(span[payloads[i]..]);
        }

        return file;
    }

    private static FourCc PayloadTag(IresEntryKind kind) => kind switch
    {
        IresEntryKind.Body => IlffTags.Body,
        IresEntryKind.Path => IlffTags.Path,
        IresEntryKind.ContextString => IlffTags.Cstr,
        _ => throw new IlffFormatException($"Entry kind {kind} has no chunk tag to write it under."),
    };

    private static byte[] NameBytes(Entry entry)
    {
        if (string.IsNullOrEmpty(entry.Name))
        {
            throw new IlffFormatException("An archive entry needs a name; the reader rejects a payload that has none.");
        }

        // Latin-1 and NUL-terminated, matching the reader, which decodes to the first NUL. The
        // terminator is counted in the chunk size: a retail NAME chunk holding the 33-character
        // "LOCAL:common/textures/shadow1.tex" records 34 bytes.
        byte[] bytes = new byte[Encoding.Latin1.GetByteCount(entry.Name) + 1];
        _ = Encoding.Latin1.GetBytes(entry.Name, bytes);
        return bytes;
    }
}
