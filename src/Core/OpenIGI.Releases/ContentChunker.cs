using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;

namespace OpenIGI.Releases;

/// <summary>
/// Cuts a file into content-defined chunks, and stores and restores a chunk as the server keeps it.
/// </summary>
/// <remarks>
/// <para>
/// A cut is placed where a rolling hash of the bytes just read meets a condition (FastCDC, with a gear
/// table), not at a fixed offset. Bytes inserted or removed in one place therefore move only the cuts near
/// them: past the change, the cuts land on the same bytes as before and the chunks come out identical. A
/// texture pack's level archive is a quarter of a gigabyte, and one rebuilt texture shifts every offset after
/// it; with fixed-size blocks that is the rest of the archive to download again, with these it is a chunk or
/// two.
/// </para>
/// <para>
/// The chunk sizes run from <see cref="MinSize"/> to <see cref="MaxSize"/>, about <see cref="AverageSize"/> on
/// average, with the normalised cut condition (a harder one before the average, an easier one after) that
/// keeps them close to it. The table and the sizes are part of the format: the publish tool and the launcher
/// must cut the same bytes the same way, so none of them may change without a new manifest schema.
/// </para>
/// </remarks>
public static class ContentChunker
{
    /// <summary>The smallest chunk, except a file's last.</summary>
    public const int MinSize = 256 * 1024;

    /// <summary>The size chunks come out at on average.</summary>
    public const int AverageSize = 1024 * 1024;

    /// <summary>The largest chunk; a cut is forced there.</summary>
    public const int MaxSize = 4 * 1024 * 1024;

    // The hash shifts left a bit per byte, so its high bits depend on the last 64 bytes read. The conditions
    // test those: 22 bits must be zero before the average size, 18 after (FastCDC's normalisation level 2
    // around 2^20).
    private const ulong HardMask = 0xFFFFFC0000000000UL;
    private const ulong EasyMask = 0xFFFFC00000000000UL;

    private static readonly ulong[] Gear = BuildGear();

    /// <summary>Cuts a stream into chunks, handing each to a callback in order.</summary>
    /// <param name="input">The bytes to cut, read to the end.</param>
    /// <param name="chunk">Called with each chunk's bytes; the span is only valid during the call.</param>
    public static void Split(Stream input, Action<ReadOnlySpan<byte>> chunk)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(chunk);

        // Twice the largest chunk: a chunk is always whole in the buffer when it is cut.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxSize * 2);
        try
        {
            int start = 0;
            int end = 0;
            bool exhausted = false;

            while (true)
            {
                // Top the buffer up so it holds at least a whole largest chunk, or everything that is left.
                if (!exhausted && end - start < MaxSize)
                {
                    if (start > 0)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                        end -= start;
                        start = 0;
                    }

                    while (end < buffer.Length)
                    {
                        int read = input.Read(buffer, end, buffer.Length - end);
                        if (read == 0)
                        {
                            exhausted = true;
                            break;
                        }

                        end += read;
                    }
                }

                if (start == end)
                {
                    return;
                }

                int length = Cut(buffer.AsSpan(start, end - start));
                chunk(buffer.AsSpan(start, length));
                start += length;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Finds where the first chunk of some bytes ends.</summary>
    /// <param name="data">The bytes, starting at a chunk boundary; all that is left when shorter than the largest chunk.</param>
    /// <returns>The first chunk's length.</returns>
    public static int Cut(ReadOnlySpan<byte> data)
    {
        if (data.Length <= MinSize)
        {
            return data.Length;
        }

        int limit = Math.Min(data.Length, MaxSize);
        int normal = Math.Min(limit, AverageSize);
        ulong hash = 0;

        // The bytes before the smallest size never cut, so the hash need not see them; it fills in 64 bytes.
        int i = MinSize;
        for (; i < normal; i++)
        {
            hash = (hash << 1) + Gear[data[i]];
            if ((hash & HardMask) == 0)
            {
                return i + 1;
            }
        }

        for (; i < limit; i++)
        {
            hash = (hash << 1) + Gear[data[i]];
            if ((hash & EasyMask) == 0)
            {
                return i + 1;
            }
        }

        return limit;
    }

    /// <summary>The name a chunk is stored under: the SHA-256 of its bytes, lowercase hex.</summary>
    /// <param name="data">The chunk.</param>
    /// <returns>Its hash.</returns>
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>Compresses a chunk the way the server stores it.</summary>
    /// <param name="data">The chunk.</param>
    /// <returns>The stored object.</returns>
    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        // Brotli at quality 9 with a 4 MiB window: a chunk is never larger, so it is all one window.
        byte[] output = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
        if (!BrotliEncoder.TryCompress(data, output, out int written, quality: 9, window: 22))
        {
            throw new InvalidOperationException("A chunk did not fit its compression bound.");
        }

        return output.AsSpan(0, written).ToArray();
    }

    /// <summary>Restores a chunk from its stored object and checks it is the chunk it should be.</summary>
    /// <param name="stored">The stored object.</param>
    /// <param name="expected">The chunk the manifest names.</param>
    /// <returns>The chunk's bytes.</returns>
    /// <exception cref="InvalidDataException">The object does not decode to that chunk.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> stored, ContentChunk expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        byte[] data = new byte[expected.Size];
        if (!BrotliDecoder.TryDecompress(stored, data, out int written) || written != expected.Size)
        {
            throw new InvalidDataException($"Chunk {expected.Hash} does not decompress to {expected.Size} bytes.");
        }

        if (!string.Equals(Hash(data), expected.Hash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Chunk {expected.Hash} does not match its hash.");
        }

        return data;
    }

    // SplitMix64 from a fixed seed: 256 well-mixed constants that every build computes the same.
    private static ulong[] BuildGear()
    {
        ulong[] table = new ulong[256];
        ulong state = 0x4F70656E49474931UL;
        for (int i = 0; i < table.Length; i++)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            table[i] = z ^ (z >> 31);
        }

        return table;
    }
}
