using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenIGI.Core.Graphics;

/// <summary>
/// Writes 8-bit RGBA PNGs.
/// </summary>
/// <remarks>
/// <para>
/// The project writes BMPs where it needs an image out of the renderer, because a BMP is a header and
/// then the pixels and needs no encoder at all. That stops being enough the moment an image is meant to
/// leave the project — a texture export is thousands of files, and thousands of uncompressed BMPs is
/// tens of gigabytes for data that compresses to a fraction of it.
/// </para>
/// <para>
/// Deliberately minimal in every direction but one: one colour type, one bit depth, no interlacing.
/// </para>
/// <para>
/// The exception is FILTERING, which started as filter zero on every row and is now chosen per row. That
/// was not worth doing while the output was a few thousand small textures to look at; it became worth
/// doing when a 4x pack of the same art turned into gigabytes. Measured over 60 of the game's textures
/// enlarged four times, filter zero gives 19.6 MB against 35.4 MB unpacked, and choosing per row gives
/// 11.2 MB — the filters are worth more than the deflate they feed.
/// </para>
/// <para>
/// The heuristic is the one the PNG specification suggests: try all five and keep whichever leaves the
/// smallest sum of absolute signed differences. It is not optimal and does not need to be; it is one
/// pass over five candidate rows and it recovers most of what an exhaustive search would.
/// </para>
/// </remarks>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes pixels to a PNG file, replacing anything already there.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="pixels">The pixels, row-major and top-down, <paramref name="width"/> per row.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too small.</exception>
    public static void Write(string path, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        using FileStream file = File.Create(path);
        Write(file, pixels, width, height);
    }

    /// <summary>Writes pixels to a stream as a PNG.</summary>
    /// <param name="destination">Where to write. Left open.</param>
    /// <param name="pixels">The pixels, row-major and top-down, <paramref name="width"/> per row.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too small.</exception>
    public static void Write(Stream destination, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (width > int.MaxValue / height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The dimensions describe more pixels than this process can address.");
        }

        // Every encoded row has four RGBA bytes per pixel plus its filter byte. Validate the actual
        // staging buffer shape before accepting the pixel span, so a mathematically valid pixel count
        // cannot wrap the allocation that Deflate needs.
        if ((long)height * (((long)width * Bgra32.Size) + 1) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The filtered PNG scanlines exceed this process's addressable buffer limit.");
        }

        int count = width * height;

        if (pixels.Length < count)
        {
            throw new ArgumentException(
                $"A {width}x{height} image needs {count} pixels, got {pixels.Length}.", nameof(pixels));
        }

        destination.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;      // bits per channel
        header[9] = 6;      // truecolour with alpha
        header[10] = 0;     // deflate, the only compression PNG defines
        header[11] = 0;     // adaptive filtering, the only filter method PNG defines
        header[12] = 0;     // not interlaced

        WriteChunk(destination, "IHDR"u8, header);
        WriteChunk(destination, "IDAT"u8, Deflate(pixels, width, height));
        WriteChunk(destination, "IEND"u8, default);
    }

    /// <summary>Filters the scanlines and compresses them into a zlib stream.</summary>
    /// <remarks>
    /// Each row is prefixed with its filter type, which is what makes the encoded data one byte per row
    /// longer than the pixels it holds. Filters predict each byte from the one to its left, the one
    /// above, or both, so what reaches deflate is mostly small differences instead of colours — which is
    /// where the compression actually comes from on smooth, upscaled art.
    /// </remarks>
    private static byte[] Deflate(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        int bytes = width * 4;
        int stride = bytes + 1;

        byte[] raw = new byte[stride * height];
        byte[] current = new byte[bytes];
        byte[] previous = new byte[bytes];
        byte[] candidate = new byte[bytes];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Bgra32 pixel = pixels[(y * width) + x];
                int at = x * 4;

                // PNG is RGBA and the source is BGRA, so this is the swap as well as the copy.
                current[at] = pixel.R;
                current[at + 1] = pixel.G;
                current[at + 2] = pixel.B;
                current[at + 3] = pixel.A;
            }

            int row = y * stride;
            long best = long.MaxValue;

            for (byte filter = 0; filter <= 4; filter++)
            {
                long cost = Apply(filter, current, previous, candidate);

                if (cost < best)
                {
                    best = cost;
                    raw[row] = filter;
                    candidate.CopyTo(raw.AsSpan(row + 1, bytes));
                }
            }

            (previous, current) = (current, previous);
        }

        using MemoryStream compressed = new();

        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        return compressed.ToArray();
    }

    /// <summary>Encodes one row under one filter and reports what it would cost to keep.</summary>
    /// <remarks>
    /// The cost is the sum of absolute SIGNED differences: a filtered byte near zero or near 255 is a
    /// small difference either way, and treating 255 as large is what makes a naive sum pick the wrong
    /// filter on dark art. This is the estimate the PNG specification recommends, and it is an estimate
    /// -- the only exact measure is to deflate all five, which costs far more than it recovers.
    /// </remarks>
    private static long Apply(byte filter, ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, Span<byte> destination)
    {
        const int Bpp = 4;
        long cost = 0;

        for (int i = 0; i < current.Length; i++)
        {
            byte left = i >= Bpp ? current[i - Bpp] : (byte)0;
            byte up = previous[i];
            byte upLeft = i >= Bpp ? previous[i - Bpp] : (byte)0;

            byte value = filter switch
            {
                0 => current[i],
                1 => (byte)(current[i] - left),
                2 => (byte)(current[i] - up),
                3 => (byte)(current[i] - ((left + up) >> 1)),
                _ => (byte)(current[i] - Paeth(left, up, upLeft)),
            };

            destination[i] = value;
            cost += value < 128 ? value : 256 - value;
        }

        return cost;
    }

    /// <summary>The Paeth predictor: whichever neighbour the gradient points closest to.</summary>
    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toUpLeft = Math.Abs(estimate - upLeft);

        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        return toUp <= toUpLeft ? up : upLeft;
    }

    /// <summary>Writes one length-tagged, CRC-checked chunk.</summary>
    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);
        destination.Write(type);
        destination.Write(data);

        // The check covers the type and the payload, and specifically NOT the length that precedes
        // them -- a detail the format is explicit about and an encoder gets exactly one chance to
        // notice, because every reader rejects the file rather than telling you which part was wrong.
        uint crc = 0xFFFFFFFFu;
        crc = Accumulate(crc, type);
        crc = Accumulate(crc, data);

        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc ^ 0xFFFFFFFFu);
        destination.Write(check);
    }

    private static uint Accumulate(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            uint c = n;

            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
