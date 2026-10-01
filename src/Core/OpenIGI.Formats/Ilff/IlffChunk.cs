using OpenIGI.Core;

namespace OpenIGI.Formats.Ilff;

/// <summary>
/// One chunk of an <see cref="IlffContainer"/>, together with a zero-copy view of its payload.
/// </summary>
/// <remarks>
/// A chunk header is <c>{ u32 fourcc; u32 size; u32 align; u32 next; }</c>. The first sixteen bytes of
/// a container header have the same shape, which is why a nested container appears in a chunk list as a
/// chunk whose <see cref="Id"/> is <c>ILFF</c> — see <see cref="IsContainer"/>.
/// </remarks>
public readonly struct IlffChunk : IEquatable<IlffChunk>
{
    /// <summary>The size in bytes of a chunk header.</summary>
    public const int HeaderSize = 16;

    internal IlffChunk(
        FourCc id,
        int size,
        int align,
        int next,
        int offsetInParent,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> raw)
    {
        Id = id;
        Size = size;
        Align = align;
        Next = next;
        OffsetInParent = offsetInParent;
        Payload = payload;
        Raw = raw;
    }

    /// <summary>Gets the chunk tag, in file order.</summary>
    public FourCc Id { get; }

    /// <summary>Gets the value of the chunk's <c>size</c> field.</summary>
    /// <remarks>
    /// For a leaf chunk this is the payload length, excluding the header and any trailing padding. For a
    /// nested container (<see cref="IsContainer"/>) it is the container's total size, its own 20-byte
    /// header included, because the two headers share the same four fields.
    /// </remarks>
    public int Size { get; }

    /// <summary>Gets the chunk's alignment, which never exceeds its parent container's alignment.</summary>
    public int Align { get; }

    /// <summary>Gets the distance in bytes from this chunk to the next sibling, or zero at the end of the list.</summary>
    /// <remarks>This already includes the header, payload and padding.</remarks>
    public int Next { get; }

    /// <summary>Gets this chunk's offset from the base of its parent container.</summary>
    /// <remarks>Payload alignment is computed from this offset, not from the start of the file.</remarks>
    public int OffsetInParent { get; }

    /// <summary>Gets the chunk payload.</summary>
    /// <remarks>
    /// For a nested container this is everything after the shared 16-byte header — the form type and the
    /// container's own chunks. Use <see cref="AsContainer"/> to parse it instead of reading it directly.
    /// </remarks>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Gets the chunk including its header and padding, up to the next sibling.</summary>
    /// <remarks>For a nested container this is exactly the byte range <see cref="AsContainer"/> parses.</remarks>
    public ReadOnlyMemory<byte> Raw { get; }

    /// <summary>Gets a value indicating whether this chunk is itself a nested ILFF container.</summary>
    public bool IsContainer => Id == IlffTags.Ilff;

    /// <summary>Determines whether two chunks describe the same range of the same buffer.</summary>
    /// <param name="left">The first chunk.</param>
    /// <param name="right">The second chunk.</param>
    /// <returns><see langword="true"/> if the chunks are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(IlffChunk left, IlffChunk right) => left.Equals(right);

    /// <summary>Determines whether two chunks differ.</summary>
    /// <param name="left">The first chunk.</param>
    /// <param name="right">The second chunk.</param>
    /// <returns><see langword="true"/> if the chunks differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(IlffChunk left, IlffChunk right) => !left.Equals(right);

    /// <summary>Parses this chunk as a nested ILFF container.</summary>
    /// <returns>The nested container.</returns>
    /// <exception cref="IlffFormatException">This chunk is not a nested container, or it is malformed.</exception>
    public IlffContainer AsContainer()
    {
        if (!IsContainer)
        {
            throw new IlffFormatException($"Chunk '{Id}' at offset {OffsetInParent} is not a nested ILFF container.");
        }

        return IlffContainer.Read(Raw);
    }

    /// <inheritdoc/>
    public bool Equals(IlffChunk other) =>
        Id == other.Id
        && Size == other.Size
        && Align == other.Align
        && Next == other.Next
        && OffsetInParent == other.OffsetInParent
        && Payload.Equals(other.Payload);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IlffChunk other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Id, Size, Align, Next, OffsetInParent);

    /// <inheritdoc/>
    public override string ToString() => $"{Id} size={Size} align={Align} @+{OffsetInParent}";
}
