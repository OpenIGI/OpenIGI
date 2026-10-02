using System.Buffers;
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
    public static (Bgra32[] Pixels, int Width, int Height) Read(ReadOnlySpan<byte> data) => Read(data, capacity: null);

    /// <summary>Reads a PNG from memory into an array with room left after the image.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <param name="capacity">
    /// How many pixels the returned array holds, given the image's width and height; at least the image's
    /// own. Null for exactly the image. A caller about to build a mip chain asks for the whole chain, so
    /// level zero is decoded where the chain starts rather than into an array that is then copied.
    /// </param>
    /// <returns>The pixels, row-major and top-down from index zero, with the image's dimensions.</returns>
    /// <exception cref="InvalidDataException">The data is not a PNG this reader supports.</exception>
    public static (Bgra32[] Pixels, int Width, int Height) Read(ReadOnlySpan<byte> data, Func<int, int, int>? capacity)
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

        // Where the image data lies, chunk by chunk; gathered into one buffer once the walk is done.
        List<(int Offset, int Length)> imageData = [];
        long compressedLength = 0;

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
                if (compressedLength > MaxCompressedBytes - length)
                {
                    throw new InvalidDataException("PNG compressed data exceeds the reader's size limit.");
                }

                imageData.Add((payload, length));
                compressedLength += length;
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

        // The decompressed size is known from the header, so it is inflated straight into a buffer of that
        // size rather than into a stream that grows by doubling. The growing stream was the largest single
        // allocator in a profile of the menu: every texture-pack picture of every backdrop went through
        // it, at several times the picture's final size counting the copies it left behind. Both buffers
        // are pooled, because nothing outlives this call but the pixels.
        long expectedLength = (((long)width * channels * bytesPerSample) + 1) * height;
        if (expectedLength > MaxDecodedBytes)
        {
            throw new InvalidDataException("PNG decompressed data exceeds the reader's size limit.");
        }

        byte[] compressed = ArrayPool<byte>.Shared.Rent((int)compressedLength);
        byte[] raw = ArrayPool<byte>.Shared.Rent((int)expectedLength);
        try
        {
            int gathered = 0;
            foreach ((int offset, int length) in imageData)
            {
                data.Slice(offset, length).CopyTo(compressed.AsSpan(gathered));
                gathered += length;
            }

            int inflated;
            using (MemoryStream source = new(compressed, 0, gathered, writable: false))
            using (ZLibStream inflate = new(source, CompressionMode.Decompress))
            {
                inflated = inflate.ReadAtLeast(raw.AsSpan(0, (int)expectedLength), (int)expectedLength, throwOnEndOfStream: false);
                DrainLimited(inflate, inflated, MaxDecodedBytes);
            }

            int room = Math.Max(width * height, capacity?.Invoke(width, height) ?? 0);
            return (Unfilter(raw, inflated, width, height, channels, bytesPerSample, room), width, height);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    /// <summary>Reads past whatever the image needed, holding the whole stream to the size limit.</summary>
    /// <remarks>
    /// The pixels are all in hand by now; this keeps the old guarantee that a stream inflating past the
    /// limit is refused rather than silently cut short. A well-formed file has nothing left to read.
    /// </remarks>
    private static void DrainLimited(Stream source, int alreadyRead, int limit)
    {
        Span<byte> scratch = stackalloc byte[512];
        int total = alreadyRead;

        while (true)
        {
            int read = source.Read(scratch);
            if (read == 0)
            {
                return;
            }

            if (read > limit - total)
            {
                throw new InvalidDataException("PNG decompressed data exceeds the reader's size limit.");
            }

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
    private static Bgra32[] Unfilter(byte[] raw, int length, int width, int height, int channels, int bytesPerSample, int room)
    {
        // The filters predict a byte from the byte one WHOLE PIXEL to its left, not one sample: at
        // sixteen bits that is two bytes per sample, so the stride between a byte and its predictor
        // doubles. Using the sample count here instead is the classic way to decode a sixteen-bit PNG
        // into noise that still has roughly the right shape.
        int bytesPerPixel = channels * bytesPerSample;
        int rowBytes = width * bytesPerPixel;
        int stride = rowBytes + 1;

        if (length < stride * height)
        {
            throw new InvalidDataException(
                $"Truncated PNG: {length} bytes of pixel data, {stride * height} expected.");
        }

        Bgra32[] pixels = new Bgra32[room];

        // Undone in place, row after row: each row's prediction reads the row above AFTER that row has
        // been undone, which is exactly what the buffer holds by the time the walk reaches it. The first
        // row's "above" is all zeroes.
        byte[] zeroRow = new byte[rowBytes];
        for (int row = 0; row < height; row++)
        {
            int at = row * stride;
            byte filter = raw[at];
            Span<byte> current = raw.AsSpan(at + 1, rowBytes);
            ReadOnlySpan<byte> previous = row == 0 ? zeroRow : raw.AsSpan(at - stride + 1, rowBytes);

            UnfilterRow(filter, current, previous, bytesPerPixel);
            ConvertRow(current, pixels.AsSpan(row * width, width), channels, bytesPerSample);
        }

        return pixels;
    }

    /// <summary>Undoes one scanline's prediction in place.</summary>
    /// <remarks>
    /// One loop per filter rather than a switch per byte: the filter is the same across the row, and the
    /// per-byte switch was most of what decoding a texture-pack picture cost. The first pixel of a row
    /// has no left neighbour and no above-left one, both of which read as zero -- which turns the average
    /// into half the byte above, and Paeth into the byte above.
    /// </remarks>
    private static void UnfilterRow(byte filter, Span<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        int first = Math.Min(bytesPerPixel, current.Length);

        switch (filter)
        {
            case 0:
                break;

            case 1:
                for (int i = bytesPerPixel; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + current[i - bytesPerPixel]);
                }

                break;

            case 2:
                for (int i = 0; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + previous[i]);
                }

                break;

            case 3:
                for (int i = 0; i < first; i++)
                {
                    current[i] = (byte)(current[i] + (previous[i] / 2));
                }

                for (int i = bytesPerPixel; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + ((current[i - bytesPerPixel] + previous[i]) / 2));
                }

                break;

            case 4:
                for (int i = 0; i < first; i++)
                {
                    current[i] = (byte)(current[i] + previous[i]);
                }

                for (int i = bytesPerPixel; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + Paeth(current[i - bytesPerPixel], previous[i], previous[i - bytesPerPixel]));
                }

                break;

            default:
                throw new InvalidDataException($"PNG filter type {filter} is not defined.");
        }
    }

    /// <summary>Turns one undone scanline into pixels.</summary>
    private static void ConvertRow(ReadOnlySpan<byte> row, Span<Bgra32> pixels, int channels, int bytesPerSample)
    {
        // The two shapes nearly every texture is, without the per-pixel switch.
        if (bytesPerSample == 1 && channels == 4)
        {
            for (int column = 0, source = 0; column < pixels.Length; column++, source += 4)
            {
                pixels[column] = new Bgra32(row[source + 2], row[source + 1], row[source], row[source + 3]);
            }

            return;
        }

        if (bytesPerSample == 1 && channels == 3)
        {
            for (int column = 0, source = 0; column < pixels.Length; column++, source += 3)
            {
                pixels[column] = new Bgra32(row[source + 2], row[source + 1], row[source], 255);
            }

            return;
        }

        int bytesPerPixel = channels * bytesPerSample;
        for (int column = 0; column < pixels.Length; column++)
        {
            // The high byte of each sample, which for a big-endian sixteen-bit sample is the byte it
            // starts on -- so one index step covers both depths.
            int source = column * bytesPerPixel;
            int step = bytesPerSample;

            (byte r, byte g, byte b, byte a) = channels switch
            {
                1 => (row[source], row[source], row[source], (byte)255),
                2 => (row[source], row[source], row[source], row[source + step]),
                3 => (row[source], row[source + step], row[source + (2 * step)], (byte)255),
                _ => (row[source], row[source + step], row[source + (2 * step)], row[source + (3 * step)]),
            };

            pixels[column] = new Bgra32(b, g, r, a);
        }
    }

    /// <summary>The Paeth predictor: whichever neighbour the gradient points closest to.</summary>
    /// <remarks>
    /// Inlined, with the distances worked out without the estimate itself: estimate minus left is above
    /// minus the corner, and so on. A profile of the menu had a third of all decoding inside calls to
    /// this and to <see cref="Math.Abs(int)"/>, at three of each per byte of every texture-pack picture.
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int Paeth(int left, int above, int aboveLeft)
    {
        int toLeft = Magnitude(above - aboveLeft);
        int toAbove = Magnitude(left - aboveLeft);
        int toCorner = Magnitude(left + above - aboveLeft - aboveLeft);

        return toLeft <= toAbove && toLeft <= toCorner ? left
            : toAbove <= toCorner ? above
            : aboveLeft;
    }

    /// <summary>The absolute value, without the overflow check <see cref="Math.Abs(int)"/> makes; the inputs are small.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int Magnitude(int value)
    {
        int sign = value >> 31;
        return (value ^ sign) - sign;
    }
}
