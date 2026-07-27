using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.TestSupport;

/// <summary>
/// Builds small ILFF containers so parsers can be tested without shipping original game files.
/// </summary>
/// <remarks>
/// <para>
/// The writer deliberately reimplements the layout rules from the format spec rather than calling into
/// the production reader, so a test built on it fails when the reader and the spec disagree:
/// </para>
/// <list type="bullet">
/// <item><description>the header's <c>size</c> field counts the 20-byte header;</description></item>
/// <item><description>the first chunk sits at <c>alignUp(20, container.align)</c>;</description></item>
/// <item><description>
/// a chunk payload sits at <c>alignUp(chunkOffsetInParent + 16, chunk.align)</c>, measured from the
/// container base — which is what makes nested containers interesting.
/// </description></item>
/// </list>
/// </remarks>
public sealed class IlffWriter
{
    private const int ContainerHeaderSize = 20;
    private const int ChunkHeaderSize = 16;

    private readonly List<PendingChunk> _chunks = [];
    private readonly string _formType;
    private readonly int _align;

    /// <summary>Initializes a new instance of the <see cref="IlffWriter"/> class.</summary>
    /// <param name="formType">The four-character form type, for example <c>IRES</c>.</param>
    /// <param name="align">The container alignment; must be a power of two.</param>
    public IlffWriter(string formType, int align = 4)
    {
        ArgumentNullException.ThrowIfNull(formType);
        if (formType.Length != 4)
        {
            throw new ArgumentException("A form type is four characters.", nameof(formType));
        }

        if (align <= 0 || (align & (align - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(align), align, "Alignment must be a power of two.");
        }

        _formType = formType;
        _align = align;
    }

    /// <summary>Appends a chunk.</summary>
    /// <param name="fourCc">The four-character chunk tag.</param>
    /// <param name="payload">The chunk payload.</param>
    /// <param name="align">The chunk alignment; defaults to the container alignment and must not exceed it.</param>
    /// <returns>This writer, for chaining.</returns>
    public IlffWriter AddChunk(string fourCc, ReadOnlySpan<byte> payload, int? align = null)
    {
        ArgumentNullException.ThrowIfNull(fourCc);
        if (fourCc.Length != 4)
        {
            throw new ArgumentException("A chunk tag is four characters.", nameof(fourCc));
        }

        int chunkAlign = align ?? _align;
        if (chunkAlign <= 0 || (chunkAlign & (chunkAlign - 1)) != 0 || chunkAlign > _align)
        {
            throw new ArgumentOutOfRangeException(
                nameof(align),
                chunkAlign,
                "A chunk alignment must be a power of two no larger than the container alignment.");
        }

        _chunks.Add(new PendingChunk(fourCc, payload.ToArray(), chunkAlign));
        return this;
    }

    /// <summary>Appends a <c>NAME</c> chunk holding a NUL-terminated name.</summary>
    /// <param name="name">The resource name.</param>
    /// <param name="align">The chunk alignment; defaults to the container alignment.</param>
    /// <returns>This writer, for chaining.</returns>
    public IlffWriter AddName(string name, int? align = null) =>
        AddChunk("NAME", NullTerminated(name), align);

    /// <summary>Appends a <c>BODY</c> chunk.</summary>
    /// <param name="payload">The payload bytes.</param>
    /// <param name="align">The chunk alignment; defaults to the container alignment.</param>
    /// <returns>This writer, for chaining.</returns>
    public IlffWriter AddBody(ReadOnlySpan<byte> payload, int? align = null) =>
        AddChunk("BODY", payload, align);

    /// <summary>Appends a <c>CSTR</c> chunk holding a NUL-terminated string.</summary>
    /// <param name="text">The context string.</param>
    /// <param name="align">The chunk alignment; defaults to the container alignment.</param>
    /// <returns>This writer, for chaining.</returns>
    public IlffWriter AddContextString(string text, int? align = null) =>
        AddChunk("CSTR", NullTerminated(text), align);

    /// <summary>Appends another container as a nested chunk.</summary>
    /// <param name="nested">The container to nest; its alignment must not exceed this container's.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <remarks>
    /// A nested container is written as a chunk whose tag is <c>ILFF</c>: the first sixteen bytes of a
    /// container header have the same shape as a chunk header, which is how the original walker
    /// recurses. The chunk therefore inherits the nested container's own alignment.
    /// </remarks>
    public IlffWriter AddNested(IlffWriter nested)
    {
        ArgumentNullException.ThrowIfNull(nested);
        if (nested._align > _align)
        {
            throw new ArgumentException(
                "A nested container may not be aligned more strictly than its parent.",
                nameof(nested));
        }

        byte[] bytes = nested.ToArray();

        // Skip the magic, size, align and next fields: the enclosing chunk header supplies them, and
        // the rest (form type onwards) becomes the chunk payload.
        _chunks.Add(new PendingChunk("ILFF", bytes[ChunkHeaderSize..], nested._align, IsContainer: true));
        return this;
    }

    /// <summary>Renders the container.</summary>
    /// <returns>The container bytes, header included.</returns>
    public byte[] ToArray()
    {
        List<byte> buffer = [];
        Grow(buffer, AlignUp(ContainerHeaderSize, _align));

        for (int i = 0; i < _chunks.Count; i++)
        {
            PendingChunk chunk = _chunks[i];
            int chunkOffset = buffer.Count;
            Grow(buffer, chunkOffset + ChunkHeaderSize);

            // A nested container's content follows its header immediately: the chunk header and the
            // container header are the same sixteen bytes, so no padding may come between them.
            int payloadOffset = chunk.IsContainer
                ? chunkOffset + ChunkHeaderSize
                : AlignUp(chunkOffset + ChunkHeaderSize, chunk.Align);
            Grow(buffer, payloadOffset);
            buffer.AddRange(chunk.Payload);

            int end = AlignUp(buffer.Count, chunk.Align);
            Grow(buffer, end);

            bool isLast = i == _chunks.Count - 1;
            int next = isLast ? 0 : end - chunkOffset;

            // A nested container's size counts its own header; a leaf chunk's size is its payload.
            int size = chunk.IsContainer ? chunk.Payload.Length + ChunkHeaderSize : chunk.Payload.Length;

            WriteHeader(buffer, chunkOffset, chunk.FourCc, size, chunk.Align, next);
        }

        Grow(buffer, AlignUp(buffer.Count, _align));

        byte[] result = [.. buffer];
        WriteHeader(result, 0, "ILFF", result.Length, _align, 0);
        Encoding.Latin1.GetBytes(_formType).CopyTo(result, 16);
        return result;
    }

    private static byte[] NullTerminated(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] bytes = new byte[text.Length + 1];
        Encoding.Latin1.GetBytes(text).CopyTo(bytes, 0);
        return bytes;
    }

    private static int AlignUp(int value, int alignment) => (value + (alignment - 1)) & ~(alignment - 1);

    private static void Grow(List<byte> buffer, int size)
    {
        while (buffer.Count < size)
        {
            buffer.Add(0);
        }
    }

    private static void WriteHeader(IList<byte> buffer, int offset, string fourCc, int size, int align, int next)
    {
        Span<byte> header = stackalloc byte[ChunkHeaderSize];
        Encoding.Latin1.GetBytes(fourCc).CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)align);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)next);

        for (int i = 0; i < ChunkHeaderSize; i++)
        {
            buffer[offset + i] = header[i];
        }
    }

    private sealed record PendingChunk(string FourCc, byte[] Payload, int Align, bool IsContainer = false);
}
