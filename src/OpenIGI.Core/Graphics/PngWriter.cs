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
/// Deliberately minimal: one colour type, one bit depth, no interlacing, and filter type zero on every
/// scanline. PNG's filters trade encode time for a smaller file, and none of them is a correctness
/// matter — a decoder reads all five. What is here is the format's actual requirements and nothing
/// beyond them, which is why it is a hundred lines rather than a dependency.
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

    /// <summary>Compresses the filtered scanlines into a zlib stream.</summary>
    /// <remarks>
    /// Each row is prefixed with its filter type, which is what makes the raw data one byte per row
    /// longer than the pixels it holds. Zero means "store the bytes as they are"; the other four predict
    /// each byte from its neighbours and are worth having in an encoder that cares about the last twenty
    /// per cent of the file size. This one does not.
    /// </remarks>
    private static byte[] Deflate(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        int stride = (width * 4) + 1;
        byte[] raw = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            raw[row] = 0;

            for (int x = 0; x < width; x++)
            {
                Bgra32 pixel = pixels[(y * width) + x];
                int at = row + 1 + (x * 4);

                // PNG is RGBA and the source is BGRA, so this is the swap as well as the copy.
                raw[at] = pixel.R;
                raw[at + 1] = pixel.G;
                raw[at + 2] = pixel.B;
                raw[at + 3] = pixel.A;
            }
        }

        using MemoryStream compressed = new();

        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        return compressed.ToArray();
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
