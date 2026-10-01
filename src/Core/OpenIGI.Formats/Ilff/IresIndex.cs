using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ilff;

/// <summary>
/// Lists what an <c>IRES</c> archive holds and where, without reading any of it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IresArchive"/> needs the whole file in memory, which is right for the game's own archives
/// — the largest is 15 MB and its entries are handed out as slices of that one buffer. It is wrong for
/// an overrides archive. Replacement art is BGRA8 at four times the size, so a level's archive can pass
/// a quarter of a gigabyte, and the override index has to walk EVERY archive in the folder on every
/// rescan just to learn which names it carries.
/// </para>
/// <para>
/// So this walks the chunk list over a <see cref="Stream"/>, reading the 16-byte headers and the short
/// <c>NAME</c> payloads and seeking past everything else. What comes back is where each entry's bytes
/// are, not the bytes: the caller reads the slice it actually wants, when it wants it.
/// </para>
/// <para>
/// The layout it walks is the one <see cref="IresArchive"/> documents, with the same per-chunk alignment
/// rule <see cref="IresWriter"/> writes.
/// </para>
/// </remarks>
public static class IresIndex
{
    /// <summary>The longest <c>NAME</c> payload this will read, as a guard against a corrupt length.</summary>
    private const int MaxNameBytes = 4096;

    /// <summary>Where one entry's payload sits in the archive.</summary>
    /// <param name="Name">The resource name, normalised, namespace prefix included.</param>
    /// <param name="Kind">Which payload chunk carries it.</param>
    /// <param name="Offset">The payload's offset from the start of the archive.</param>
    /// <param name="Length">The payload's length in bytes.</param>
    public readonly record struct Located(string Name, IresEntryKind Kind, long Offset, int Length);

    /// <summary>Walks an archive and reports every entry's name and extent.</summary>
    /// <param name="stream">A readable, seekable stream positioned anywhere; it is seeked from the start.</param>
    /// <returns>The entries, in file order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IlffFormatException">The stream is not a seekable IRES archive, or its chunk list is malformed.</exception>
    public static IReadOnlyList<Located> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new IlffFormatException("Indexing an archive needs a seekable stream; the payloads are skipped, not read.");
        }

        stream.Position = 0;

        Span<byte> header = stackalloc byte[IlffContainer.HeaderSize];
        stream.ReadExactly(header);

        if (FourCc.FromBytes(header) != IlffTags.Ilff)
        {
            throw new IlffFormatException($"Expected magic 'ILFF', found '{FourCc.FromBytes(header)}'.");
        }

        int size = ReadBounded(header[4..], "container size");
        int align = ReadBounded(header[8..], "container alignment");

        if (FourCc.FromBytes(header[16..]) != IlffTags.Ires)
        {
            throw new IlffFormatException($"Expected an IRES archive, found form type '{FourCc.FromBytes(header[16..])}'.");
        }

        if (!IgiMath.IsPowerOfTwo(align))
        {
            throw new IlffFormatException($"Container alignment {align} is not a power of two.");
        }

        List<Located> entries = [];
        Span<byte> chunk = stackalloc byte[IlffChunk.HeaderSize];
        byte[] name = new byte[MaxNameBytes];
        string? current = null;
        int offset = IgiMath.AlignUp(IlffContainer.HeaderSize, align);

        while (offset + IlffChunk.HeaderSize <= size)
        {
            stream.Position = offset;
            stream.ReadExactly(chunk);

            FourCc id = FourCc.FromBytes(chunk);
            int chunkSize = ReadBounded(chunk[4..], "chunk size");
            int chunkAlign = ReadBounded(chunk[8..], "chunk alignment");
            int next = ReadBounded(chunk[12..], "chunk next-sibling offset");

            if (!IgiMath.IsPowerOfTwo(chunkAlign))
            {
                throw new IlffFormatException($"Chunk '{id}' at offset {offset} has alignment {chunkAlign}, which is not a power of two.");
            }

            // A nested container would need its own walk against a moved base. The game has exactly one
            // archive like that and nothing writes another, so this reports it rather than guessing.
            if (id == IlffTags.Ilff)
            {
                throw new IlffFormatException(
                    $"The archive nests a container at offset {offset}, which this index does not walk.");
            }

            int payload = IgiMath.AlignUp(offset + IlffChunk.HeaderSize, chunkAlign);

            if (payload > size || chunkSize > size - payload)
            {
                throw new IlffFormatException(
                    $"Chunk '{id}' at offset {offset} claims {chunkSize} bytes at offset {payload}, past the container end {size}.");
            }

            if (id == IlffTags.Name)
            {
                current = ResourcePath.Normalize(ReadName(stream, payload, chunkSize, name));
            }
            else if (TryKind(id, out IresEntryKind kind))
            {
                if (current is null)
                {
                    throw new IlffFormatException($"Chunk '{id}' at offset {offset} appears before any NAME chunk.");
                }

                entries.Add(new Located(current, kind, payload, chunkSize));
            }

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

        return entries;
    }

    private static bool TryKind(FourCc id, out IresEntryKind kind)
    {
        if (id == IlffTags.Body)
        {
            kind = IresEntryKind.Body;
            return true;
        }

        if (id == IlffTags.Path)
        {
            kind = IresEntryKind.Path;
            return true;
        }

        if (id == IlffTags.Cstr)
        {
            kind = IresEntryKind.ContextString;
            return true;
        }

        // Unknown ids are skipped, matching the original walker's default branch.
        kind = default;
        return false;
    }

    private static string ReadName(Stream stream, int offset, int length, byte[] buffer)
    {
        int wanted = Math.Min(length, buffer.Length);
        stream.Position = offset;
        stream.ReadExactly(buffer, 0, wanted);

        int terminator = Array.IndexOf(buffer, (byte)0, 0, wanted);
        return Encoding.Latin1.GetString(buffer, 0, terminator < 0 ? wanted : terminator);
    }

    private static int ReadBounded(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);

        if (value > int.MaxValue)
        {
            throw new IlffFormatException($"The {what} {value} is too large to address.");
        }

        return (int)value;
    }
}
