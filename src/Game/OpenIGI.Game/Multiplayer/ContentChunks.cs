using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenIGI.Game.Multiplayer;

/// <summary>One piece of a file as <see cref="ContentChunks"/> cuts it: its length and what is in it.</summary>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Hash">The first eight bytes of its SHA-256, as a number.</param>
public readonly record struct ContentChunk(int Length, ulong Hash);

/// <summary>
/// A file cut where its content says rather than every so many bytes, so that two copies of a file differing in one place --
/// a mesh changed in an archive, a few bytes inserted in a script -- share every piece but the few around the change.
/// </summary>
/// <remarks>
/// <para>
/// Content-defined chunking with a gear hash (the FastCDC scheme): a rolling value over the bytes cuts where its masked bits
/// are all zero, a stricter mask before the average length and a looser one after, so the pieces cluster near it. Cuts
/// depend only on the bytes near them, so an insertion moves the cuts after it along with the bytes and the pieces stay
/// equal.
/// </para>
/// <para>
/// A host sends the table of its file's pieces; a player cuts its own copy the same way and asks only for the pieces it
/// lacks. The whole file's SHA-256 still decides whether what was put together is the host's.
/// </para>
/// </remarks>
public static class ContentChunks
{
    /// <summary>The shortest piece but a file's last.</summary>
    public const int MinLength = 2 * 1024;

    /// <summary>The length pieces cluster near.</summary>
    public const int AverageLength = 8 * 1024;

    /// <summary>The longest piece; also the largest file sent whole, with no table.</summary>
    public const int MaxLength = 32 * 1024;

    /// <summary>The most pieces one file is cut into: what a table message can carry.</summary>
    public const int MaxCount = 20_000;

    // Fifteen bits set before the average, eleven after: FastCDC's normalised masks for an eight-kilobyte average.
    private const ulong MaskSmall = 0x0003_5907_0353_0000UL;
    private const ulong MaskLarge = 0x0000_D900_0353_0000UL;

    // The gear table: 256 fixed random numbers, made by SplitMix64 from a fixed seed, the same on every machine.
    private static readonly ulong[] Gear = MakeGear();

    /// <summary>Cuts bytes into pieces.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The pieces, in order; one for a file no longer than <see cref="MaxLength"/>.</returns>
    public static IReadOnlyList<ContentChunk> Cut(ReadOnlySpan<byte> data)
    {
        List<ContentChunk> chunks = new((data.Length / AverageLength) + 1);
        int at = 0;
        do
        {
            int length = data.Length <= MaxLength ? data.Length : NextCut(data[at..]);
            chunks.Add(new ContentChunk(length, HashOf(data.Slice(at, length))));
            at += length;
        }
        while (at < data.Length);

        return chunks;
    }

    /// <summary>Gets a piece's hash: the first eight bytes of its SHA-256.</summary>
    /// <param name="data">The piece's bytes.</param>
    /// <returns>The hash.</returns>
    public static ulong HashOf(ReadOnlySpan<byte> data)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    // Where the next piece ends, from the start of what is left.
    private static int NextCut(ReadOnlySpan<byte> rest)
    {
        if (rest.Length <= MinLength)
        {
            return rest.Length;
        }

        int end = Math.Min(rest.Length, MaxLength);
        int normal = Math.Min(end, AverageLength);
        ulong hash = 0;
        int i = MinLength;
        for (; i < normal; i++)
        {
            hash = (hash << 1) + Gear[rest[i]];
            if ((hash & MaskSmall) == 0)
            {
                return i + 1;
            }
        }

        for (; i < end; i++)
        {
            hash = (hash << 1) + Gear[rest[i]];
            if ((hash & MaskLarge) == 0)
            {
                return i + 1;
            }
        }

        return end;
    }

    private static ulong[] MakeGear()
    {
        ulong[] gear = new ulong[256];
        ulong state = 0x4F70_656E_4947_4921UL;
        for (int i = 0; i < gear.Length; i++)
        {
            state += 0x9E37_79B9_7F4A_7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
            gear[i] = z ^ (z >> 31);
        }

        return gear;
    }
}
