using System.Buffers.Binary;
using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ilff;

/// <summary>
/// A parsed ILFF container: the archive format behind <c>.res</c>, <c>.mef</c> and <c>.fnt</c> files.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is zero-copy. The container keeps a <see cref="ReadOnlyMemory{T}"/> view of its own bytes,
/// and every chunk payload is a slice of that view.
/// </para>
/// <para>
/// The layout is documented in <c>docs/formats/ilff.md</c>. Two rules matter more than the rest:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The header's <c>size</c> field is the <em>total</em> container size, header included — verified
/// against retail archives, where it equals the file length exactly.
/// </description>
/// </item>
/// <item>
/// <description>
/// A chunk's payload starts at <c>alignUp(chunkOffsetInParent + 16, chunk.align)</c>, where the offset
/// is measured from the <em>parent container's base</em> — not from the start of the file and not from
/// the chunk. For a nested container the base moves, so the same chunk bytes align differently.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class IlffContainer
{
    /// <summary>The size in bytes of a container header.</summary>
    public const int HeaderSize = 20;

    private IlffContainer(
        ReadOnlyMemory<byte> data,
        FourCc formType,
        int size,
        int align,
        int next,
        IReadOnlyList<IlffChunk> chunks)
    {
        Data = data;
        FormType = formType;
        Size = size;
        Align = align;
        Next = next;
        Chunks = chunks;
    }

    /// <summary>Gets the container bytes, header included, trimmed to <see cref="Size"/>.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the form type: <c>IRES</c> for archives, <c>OCEM</c> for models, <c>FONT</c> for fonts.</summary>
    public FourCc FormType { get; }

    /// <summary>Gets the total container size in bytes, including the 20-byte header.</summary>
    public int Size { get; }

    /// <summary>Gets the container alignment, a power of two that bounds the alignment of every chunk inside it.</summary>
    public int Align { get; }

    /// <summary>Gets the distance to the next sibling container, or zero for a root container.</summary>
    public int Next { get; }

    /// <summary>Gets the top-level chunks, in file order.</summary>
    public IReadOnlyList<IlffChunk> Chunks { get; }

    /// <summary>Gets the container content: everything after the header.</summary>
    public ReadOnlyMemory<byte> Content => Data[HeaderSize..];

    /// <summary>Determines whether <paramref name="data"/> starts with an ILFF header.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the first four bytes are <c>ILFF</c>.</returns>
    /// <remarks>Cheap enough to use as the container-type probe when registering an unknown resource.</remarks>
    public static bool IsIlff(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && FourCc.FromBytes(data) == IlffTags.Ilff;

    /// <summary>Parses an ILFF container.</summary>
    /// <param name="data">The container bytes, starting at its header. Trailing bytes beyond the container are ignored.</param>
    /// <returns>The parsed container.</returns>
    /// <exception cref="IlffFormatException">The bytes do not form a valid container.</exception>
    public static IlffContainer Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderSize)
        {
            throw new IlffFormatException($"An ILFF container needs at least {HeaderSize} bytes, got {span.Length}.");
        }

        FourCc magic = FourCc.FromBytes(span);
        if (magic != IlffTags.Ilff)
        {
            throw new IlffFormatException($"Expected magic 'ILFF', found '{magic}'.");
        }

        int size = ReadBoundedUInt32(span[4..], "container size");
        int align = ReadBoundedUInt32(span[8..], "container alignment");
        int next = ReadBoundedUInt32(span[12..], "container next-sibling offset");
        FourCc formType = FourCc.FromBytes(span[16..]);

        if (!IgiMath.IsPowerOfTwo(align))
        {
            throw new IlffFormatException($"Container alignment {align} is not a power of two.");
        }

        if (size < HeaderSize)
        {
            throw new IlffFormatException($"Container size {size} is smaller than the {HeaderSize}-byte header.");
        }

        if (size > span.Length)
        {
            throw new IlffFormatException($"Container claims {size} bytes but only {span.Length} are available.");
        }

        ReadOnlyMemory<byte> trimmed = data[..size];
        return new IlffContainer(trimmed, formType, size, align, next, ReadChunks(trimmed, size, align));
    }

    /// <summary>Finds the first top-level chunk with the given tag.</summary>
    /// <param name="id">The chunk tag to look for, in file order.</param>
    /// <param name="chunk">Receives the chunk when one is found.</param>
    /// <returns><see langword="true"/> when a matching chunk exists.</returns>
    public bool TryFindChunk(FourCc id, out IlffChunk chunk)
    {
        foreach (IlffChunk candidate in Chunks)
        {
            if (candidate.Id == id)
            {
                chunk = candidate;
                return true;
            }
        }

        chunk = default;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"ILFF {FormType} size={Size} align={Align} chunks={Chunks.Count}";

    private static ReadOnlyCollection<IlffChunk> ReadChunks(ReadOnlyMemory<byte> data, int size, int containerAlign)
    {
        List<IlffChunk> chunks = [];
        ReadOnlySpan<byte> span = data.Span;

        // The first child sits at the container base plus its own aligned header length.
        int offset = IgiMath.AlignUp(HeaderSize, containerAlign);

        while (offset + IlffChunk.HeaderSize <= size)
        {
            FourCc id = FourCc.FromBytes(span[offset..]);
            int chunkSize = ReadBoundedUInt32(span[(offset + 4)..], "chunk size");
            int chunkAlign = ReadBoundedUInt32(span[(offset + 8)..], "chunk alignment");
            int next = ReadBoundedUInt32(span[(offset + 12)..], "chunk next-sibling offset");

            if (!IgiMath.IsPowerOfTwo(chunkAlign))
            {
                throw new IlffFormatException($"Chunk '{id}' at offset {offset} has alignment {chunkAlign}, which is not a power of two.");
            }

            if (chunkAlign > containerAlign)
            {
                throw new IlffFormatException(
                    $"Chunk '{id}' at offset {offset} has alignment {chunkAlign}, which exceeds its container's alignment {containerAlign}.");
            }

            // A nested container occupies the chunk header slot with its own header, so its content
            // starts right after those sixteen bytes and its size counts the 20-byte container header.
            bool isContainer = id == IlffTags.Ilff;
            int payloadOffset;
            int payloadLength;
            if (isContainer)
            {
                if (chunkSize < HeaderSize)
                {
                    throw new IlffFormatException(
                        $"Nested container at offset {offset} claims {chunkSize} bytes, less than its own {HeaderSize}-byte header.");
                }

                payloadOffset = offset + IlffChunk.HeaderSize;
                payloadLength = chunkSize - IlffChunk.HeaderSize;
            }
            else
            {
                // Parent-relative: the alignment origin is the container base, so `offset` is used as-is.
                payloadOffset = IgiMath.AlignUp(offset + IlffChunk.HeaderSize, chunkAlign);
                payloadLength = chunkSize;
            }

            if (payloadOffset > size || payloadLength > size - payloadOffset)
            {
                throw new IlffFormatException(
                    $"Chunk '{id}' at offset {offset} claims {payloadLength} payload bytes at offset {payloadOffset}, past the container end {size}.");
            }

            int rawEnd = next != 0 ? offset + next : payloadOffset + payloadLength;
            if (rawEnd > size)
            {
                throw new IlffFormatException(
                    $"Chunk '{id}' at offset {offset} extends to {rawEnd}, past the container end {size}.");
            }

            chunks.Add(new IlffChunk(
                id,
                chunkSize,
                chunkAlign,
                next,
                offset,
                data.Slice(payloadOffset, payloadLength),
                data[offset..rawEnd]));

            if (next == 0)
            {
                break;
            }

            if (next < IlffChunk.HeaderSize)
            {
                throw new IlffFormatException(
                    $"Chunk '{id}' at offset {offset} advances by only {next} bytes, which cannot hold a chunk header.");
            }

            offset += next;
        }

        return new ReadOnlyCollection<IlffChunk>(chunks);
    }

    private static int ReadBoundedUInt32(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (value > int.MaxValue)
        {
            throw new IlffFormatException($"The {what} {value} is too large to address.");
        }

        return (int)value;
    }
}
