using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenIGI.Core.Graphics;

/// <summary>
/// Reads 8-bit PNGs back into pixels.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="PngWriter"/>, and needed for the same reason in reverse: the surface
/// maps this project derives are written out so they can be looked at and corrected, and a map somebody
/// has corrected is worth nothing unless it can be read back. It is also what lets a map produced by a
/// different tool entirely be dropped in.
/// </para>
/// <para>
/// <b>Every filter type is supported, unlike the writer, which only emits one.</b> That asymmetry is
/// deliberate: an encoder may choose whichever filter it likes and a decoder may not, so a reader that
/// handled only filter zero would work perfectly on this project's own output and fail on every file
/// from anywhere else — which is the half of the job that actually matters here.
/// </para>
/// <para>
/// Not supported, and rejected rather than mis-read: 16-bit channels, palettes, and interlacing. None
/// is a format a texture pipeline produces, and guessing at one would return a plausible wrong picture.
/// </para>
/// </remarks>
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    // A texture above this is not usable by the renderer and is large enough to turn a malformed PNG
    // into an allocation or decompression bomb before the caller can reject it.
    private const int MaxPixelCount = 16 * 1024 * 1024;
    private const int MaxCompressedBytes = 64 * 1024 * 1024;
    private const int MaxDecodedBytes = 64 * 1024 * 1024;

    /// <summary>Reads a PNG file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The pixels, row-major and top-down, with the image's dimensions.</returns>
    /// <exception cref="InvalidDataException">The file is not a PNG this reader supports.</exception>
    public static (Bgra32[] Pixels, int Width, int Height) Read(string path) =>
        Read(File.ReadAllBytes(path));

    /// <summary>Reads a PNG from memory.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The pixels, row-major and top-down, with the image's dimensions.</returns>
    /// <exception cref="InvalidDataException">The data is not a PNG this reader supports.</exception>
    public static (Bgra32[] Pixels, int Width, int Height) Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG: the signature does not match.");
        }

        int width = 0;
        int height = 0;
        int channels = 0;
        int bytesPerSample = 1;
        bool seenHeader = false;
        bool seenData = false;
        bool seenEnd = false;
        using MemoryStream compressed = new();

        int at = Signature.Length;

        while (at + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(at, 4));
            ReadOnlySpan<byte> type = data.Slice(at + 4, 4);
            int payload = at + 8;

            if (length < 0 || payload > data.Length - length - 4)
            {
                throw new InvalidDataException("Truncated PNG: a chunk runs past the end of the file.");
            }

            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payload + length, 4));
            uint actualCrc = Crc(type, data.Slice(payload, length));
            if (storedCrc != actualCrc)
            {
                throw new InvalidDataException("Invalid PNG: a chunk CRC does not match its contents.");
            }

            if (!seenHeader && !type.SequenceEqual("IHDR"u8))
            {
                throw new InvalidDataException("Invalid PNG: IHDR must be the first chunk.");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (seenHeader)
                {
                    throw new InvalidDataException("Invalid PNG: IHDR must be the first and only header chunk.");
                }

                if (length != 13)
                {
                    throw new InvalidDataException("Invalid PNG: IHDR must be 13 bytes.");
                }

                width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(payload, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(payload + 4, 4));
                byte depth = data[payload + 8];
                byte colourType = data[payload + 9];
                byte interlace = data[payload + 12];

                // Eight and sixteen bits a sample. Sixteen is narrowed to eight on the way in — the
                // pixel this returns is four bytes and there is nowhere to put the low half — which is
                // exact for the authoring tools that write sixteen-bit files holding eight bits of real
                // range, and a clean round-down for the ones that do not. Refusing them outright meant
                // an asset exported at sixteen bits simply did not load, and the caller drew whatever
                // it falls back to without ever saying why.
                if (depth is not (8 or 16))
                {
                    throw new InvalidDataException(
                        $"Only 8- and 16-bit PNGs are supported, this one is {depth}-bit.");
                }

                bytesPerSample = depth / 8;

                if (interlace != 0)
                {
                    throw new InvalidDataException("Interlaced PNGs are not supported.");
                }

                channels = colourType switch
                {
                    0 => 1,     // greyscale
                    2 => 3,     // truecolour
                    4 => 2,     // greyscale with alpha
                    6 => 4,     // truecolour with alpha
                    _ => throw new InvalidDataException($"PNG colour type {colourType} is not supported."),
                };

                if (width <= 0 || height <= 0 || (long)width * height > MaxPixelCount)
                {
                    throw new InvalidDataException("PNG dimensions exceed the reader's pixel limit.");
                }

                seenHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!seenHeader)
                {
                    throw new InvalidDataException("Invalid PNG: IDAT appears before IHDR.");
                }

                // Concatenated, because the pixel data may be split across any number of chunks and
                // the zlib stream runs across all of them as one.
                if (compressed.Length > MaxCompressedBytes - length)
                {
                    throw new InvalidDataException("PNG compressed data exceeds the reader's size limit.");
                }

                compressed.Write(data.Slice(payload, length));
                seenData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!seenHeader || !seenData || length != 0)
                {
                    throw new InvalidDataException("Invalid PNG: IEND must close a header and image data.");
                }

                seenEnd = true;
                at = payload + length + 4;
                break;
            }
            else if ((type[0] & 0x20) == 0)
            {
                throw new InvalidDataException("PNG has an unsupported critical chunk.");
            }

            // Chunk, plus its four-byte length and four-byte check.
            at = payload + length + 4;
        }

        if (!seenEnd)
        {
            throw new InvalidDataException("Invalid PNG: the mandatory IEND chunk is missing.");
        }

        if (at != data.Length)
        {
            throw new InvalidDataException("Invalid PNG: bytes follow the IEND chunk.");
        }

        if (width <= 0 || height <= 0 || channels == 0)
        {
            throw new InvalidDataException("The PNG has no usable header.");
        }

        compressed.Position = 0;
        using MemoryStream raw = new();

        using (ZLibStream inflate = new(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            CopyLimited(inflate, raw, MaxDecodedBytes);
        }

        return (Unfilter(raw.GetBuffer(), (int)raw.Length, width, height, channels, bytesPerSample), width, height);
    }

    private static void CopyLimited(Stream source, Stream destination, int limit)
    {
        byte[] buffer = new byte[81920];
        int total = 0;

        while (true)
        {
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return;
            }

            if (read > limit - total)
            {
                throw new InvalidDataException("PNG decompressed data exceeds the reader's size limit.");
            }

            destination.Write(buffer, 0, read);
            total += read;
        }
    }

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        uint crc = Accumulate(0xFFFFFFFFu, type);
        return Accumulate(crc, payload) ^ 0xFFFFFFFFu;
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

        for (uint n = 0; n < table.Length; n++)
        {
            uint crc = n;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }

            table[n] = crc;
        }

        return table;
    }

    /// <summary>Undoes the per-scanline prediction and converts to pixels.</summary>
    /// <remarks>
    /// Each of the five filters predicts a byte from some combination of the one to its left, the one
    /// above, and the one above-left, and stores only the difference. Undoing them has to run in order
    /// and in place, because each row's prediction refers to the row above it <em>after</em> that row
    /// has itself been undone.
    /// </remarks>
    private static Bgra32[] Unfilter(byte[] raw, int length, int width, int height, int channels, int bytesPerSample)
    {
        // The filters predict a byte from the byte one WHOLE PIXEL to its left, not one sample: at
        // sixteen bits that is two bytes per sample, so the stride between a byte and its predictor
        // doubles. Using the sample count here instead is the classic way to decode a sixteen-bit PNG
        // into noise that still has roughly the right shape.
        int bytesPerPixel = channels * bytesPerSample;
        int stride = (width * bytesPerPixel) + 1;

        if (length < stride * height)
        {
            throw new InvalidDataException(
                $"Truncated PNG: {length} bytes of pixel data, {stride * height} expected.");
        }

        byte[] previous = new byte[width * bytesPerPixel];
        byte[] current = new byte[width * bytesPerPixel];
        Bgra32[] pixels = new Bgra32[width * height];

        for (int row = 0; row < height; row++)
        {
            int at = row * stride;
            byte filter = raw[at];
            Array.Copy(raw, at + 1, current, 0, current.Length);

            for (int i = 0; i < current.Length; i++)
            {
                int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                int above = previous[i];
                int aboveLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;

                current[i] = filter switch
                {
                    0 => current[i],
                    1 => (byte)(current[i] + left),
                    2 => (byte)(current[i] + above),
                    3 => (byte)(current[i] + ((left + above) / 2)),
                    4 => (byte)(current[i] + Paeth(left, above, aboveLeft)),
                    _ => throw new InvalidDataException($"PNG filter type {filter} is not defined."),
                };
            }

            for (int column = 0; column < width; column++)
            {
                // The high byte of each sample, which for a big-endian sixteen-bit sample is the byte
                // it starts on -- so one index step covers both depths.
                int source = column * bytesPerPixel;
                int step = bytesPerSample;

                (byte r, byte g, byte b, byte a) = channels switch
                {
                    1 => (current[source], current[source], current[source], (byte)255),
                    2 => (current[source], current[source], current[source], current[source + step]),
                    3 => (current[source], current[source + step], current[source + (2 * step)], (byte)255),
                    _ => (current[source], current[source + step], current[source + (2 * step)],
                          current[source + (3 * step)]),
                };

                pixels[(row * width) + column] = new Bgra32(b, g, r, a);
            }

            (previous, current) = (current, previous);
        }

        return pixels;
    }

    /// <summary>The Paeth predictor: whichever neighbour the gradient points closest to.</summary>
    private static int Paeth(int left, int above, int aboveLeft)
    {
        int estimate = left + above - aboveLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toCorner = Math.Abs(estimate - aboveLeft);

        return toLeft <= toAbove && toLeft <= toCorner ? left
            : toAbove <= toCorner ? above
            : aboveLeft;
    }
}
