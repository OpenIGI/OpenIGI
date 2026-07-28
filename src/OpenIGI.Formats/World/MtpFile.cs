using System.Buffers.Binary;
using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>
/// The raw chunk list of an <c>.mtp</c> resource pack: an EA IFF-85 <c>FORM</c> of type <c>MTP </c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the original's walker at <c>0x4CEAA0</c>: the first chunk sits at offset 12, chunk headers are
/// eight bytes (<c>tag</c> then <c>size</c>), sizes are <strong>big-endian</strong>, and each payload is
/// padded to a four-byte boundary. The bound is <c>8 + FORM.size</c>, which equals the file length for
/// every retail pack.
/// </para>
/// <para>
/// The endianness is mixed and this is the trap: only the IFF chunk sizes are big-endian. Every integer
/// inside a payload is little-endian like the rest of the game's data.
/// </para>
/// <para>Parsing is zero-copy; payloads are slices of the buffer passed to <see cref="Read"/>.</para>
/// </remarks>
public sealed class MtpFile
{
    /// <summary>The size in bytes of the <c>FORM</c> header that precedes the first chunk.</summary>
    public const int HeaderSize = 12;

    /// <summary>The size in bytes of a chunk header.</summary>
    public const int ChunkHeaderSize = 8;

    private readonly Dictionary<uint, MtpChunk> _byTag;

    private MtpFile(ReadOnlyMemory<byte> data, FourCc formType, int size, IReadOnlyList<MtpChunk> chunks)
    {
        Data = data;
        FormType = formType;
        Size = size;
        Chunks = chunks;

        _byTag = [];
        foreach (MtpChunk chunk in chunks)
        {
            // First tag wins, matching the original's forward scan.
            _ = _byTag.TryAdd(chunk.Id.Value, chunk);
        }
    }

    /// <summary>Gets the pack bytes, trimmed to the length the <c>FORM</c> header declares.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the form type, <c>MTP </c> for a resource pack.</summary>
    public FourCc FormType { get; }

    /// <summary>Gets the declared content size: the file length minus the eight bytes before it.</summary>
    public int Size { get; }

    /// <summary>Gets the chunks, in file order.</summary>
    public IReadOnlyList<MtpChunk> Chunks { get; }

    /// <summary>Determines whether <paramref name="data"/> starts with an IFF <c>FORM</c> header.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the first four bytes are <c>FORM</c>.</returns>
    public static bool IsForm(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderSize && FourCc.FromBytes(data) == MtpTags.Form;

    /// <summary>Parses an <c>.mtp</c> pack.</summary>
    /// <param name="data">The pack bytes, starting at the <c>FORM</c> header.</param>
    /// <returns>The parsed chunk list.</returns>
    /// <exception cref="PackFormatException">The bytes are not a well-formed <c>FORM</c>+<c>MTP </c> file.</exception>
    public static MtpFile Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderSize)
        {
            throw new PackFormatException($"An MTP pack needs at least {HeaderSize} bytes, got {span.Length}.");
        }

        FourCc magic = FourCc.FromBytes(span);
        if (magic != MtpTags.Form)
        {
            throw new PackFormatException($"Expected magic 'FORM', found '{magic}'.");
        }

        uint declared = BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
        if (declared > int.MaxValue)
        {
            throw new PackFormatException($"The FORM size {declared} is too large to address.");
        }

        int size = (int)declared;
        long end = 8L + size;
        if (end > span.Length)
        {
            throw new PackFormatException($"The FORM claims {end} bytes but only {span.Length} are available.");
        }

        FourCc formType = FourCc.FromBytes(span[8..]);
        if (formType != MtpTags.Mtp)
        {
            throw new PackFormatException($"Expected form type 'MTP ', found '{formType}'.");
        }

        ReadOnlyMemory<byte> trimmed = data[..(int)end];
        return new MtpFile(trimmed, formType, size, ReadChunks(trimmed));
    }

    /// <summary>Finds the first chunk with the given tag.</summary>
    /// <param name="id">The chunk tag, spelled as it appears on disk.</param>
    /// <param name="chunk">Receives the chunk when one is found.</param>
    /// <returns><see langword="true"/> when the pack contains the tag.</returns>
    public bool TryFindChunk(FourCc id, out MtpChunk chunk) => _byTag.TryGetValue(id.Value, out chunk);

    /// <summary>Finds the first chunk with the given tag.</summary>
    /// <param name="id">The chunk tag, spelled as it appears on disk.</param>
    /// <returns>The chunk.</returns>
    /// <exception cref="PackFormatException">The pack has no such chunk.</exception>
    public MtpChunk GetChunk(FourCc id) =>
        TryFindChunk(id, out MtpChunk chunk) ? chunk : throw new PackFormatException($"The pack has no '{id}' chunk.");

    /// <inheritdoc/>
    public override string ToString() => $"MTP pack, {Chunks.Count} chunks, {Data.Length} bytes";

    private static ReadOnlyCollection<MtpChunk> ReadChunks(ReadOnlyMemory<byte> data)
    {
        List<MtpChunk> chunks = [];
        ReadOnlySpan<byte> span = data.Span;
        int offset = HeaderSize;

        while (offset + ChunkHeaderSize <= span.Length)
        {
            FourCc id = FourCc.FromBytes(span[offset..]);
            uint declared = BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 4)..]);
            if (declared > int.MaxValue)
            {
                throw new PackFormatException($"Chunk '{id}' at offset {offset} declares {declared} bytes, too large to address.");
            }

            int size = (int)declared;
            int payload = offset + ChunkHeaderSize;
            if (size > span.Length - payload)
            {
                throw new PackFormatException(
                    $"Chunk '{id}' at offset {offset} claims {size} payload bytes, past the pack end {span.Length}.");
            }

            chunks.Add(new MtpChunk(id, offset, data.Slice(payload, size)));
            offset = payload + IgiMath.AlignUp(size, 4);
        }

        return new ReadOnlyCollection<MtpChunk>(chunks);
    }
}
