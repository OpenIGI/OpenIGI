using Xunit;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Tests that <see cref="PngWriter"/> and <see cref="PngReader"/> agree, and agree with the format.
/// </summary>
/// <remarks>
/// A codec that is wrong in the same way at both ends round-trips perfectly and produces files nothing
/// else can open, so the round-trip alone is not enough — the header and the chunk checks are verified
/// against the format's own rules as well.
/// </remarks>
public sealed class PngCodecTests
{
    /// <summary>Pixels written and read back must be identical, alpha included.</summary>
    [Fact]
    public void RoundTripPreservesEveryChannel()
    {
        const int width = 13;
        const int height = 7;
        Bgra32[] source = new Bgra32[width * height];

        for (int i = 0; i < source.Length; i++)
        {
            source[i] = new Bgra32((byte)(i * 7), (byte)(i * 13), (byte)(i * 29), (byte)(i * 3));
        }

        using MemoryStream stream = new();
        PngWriter.Write(stream, source, width, height);

        (Bgra32[] decoded, int decodedWidth, int decodedHeight) = PngReader.Read(stream.ToArray());

        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.Equal(source, decoded);
    }

    /// <summary>A non-square image must not come back transposed.</summary>
    /// <remarks>
    /// Most of this game's textures are not square — 128x64 and 64x128 are both common — so a width and
    /// height swapped somewhere in the stride arithmetic would corrupt the majority of the pack while
    /// leaving a square test image perfect.
    /// </remarks>
    [Fact]
    public void RoundTripPreservesOrientation()
    {
        const int width = 4;
        const int height = 2;

        Bgra32[] source =
        [
            new(1, 0, 0, 255), new(2, 0, 0, 255), new(3, 0, 0, 255), new(4, 0, 0, 255),
            new(5, 0, 0, 255), new(6, 0, 0, 255), new(7, 0, 0, 255), new(8, 0, 0, 255),
        ];

        using MemoryStream stream = new();
        PngWriter.Write(stream, source, width, height);

        (Bgra32[] decoded, int decodedWidth, int decodedHeight) = PngReader.Read(stream.ToArray());

        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.Equal(4, decoded[3].B);
        Assert.Equal(5, decoded[4].B);
    }

    /// <summary>Every chunk's check value must be the one the format defines.</summary>
    /// <remarks>
    /// The reader here ignores these, as do several lenient decoders, so a wrong CRC round-trips
    /// perfectly and is rejected by every strict viewer — which is exactly the failure that looks like
    /// "the tool produced invalid images" and cannot be reproduced from inside the project.
    /// </remarks>
    [Fact]
    public void EveryChunkCarriesTheCorrectCheckValue()
    {
        Bgra32[] source = new Bgra32[4];
        Array.Fill(source, new Bgra32(10, 20, 30, 40));

        using MemoryStream stream = new();
        PngWriter.Write(stream, source, 2, 2);
        byte[] file = stream.ToArray();

        Assert.Equal<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], file[..8]);

        int at = 8;
        List<string> types = [];

        while (at + 12 <= file.Length)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at, 4));
            string type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            types.Add(type);

            uint stored = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                file.AsSpan(at + 8 + length, 4));

            // The check covers the type and the payload, and specifically not the length before them.
            uint expected = Crc32(file.AsSpan(at + 4, 4 + length));

            Assert.True(stored == expected, $"chunk '{type}' has check {stored:x8}, expected {expected:x8}.");
            at += 12 + length;
        }

        Assert.Equal(["IHDR", "IDAT", "IEND"], types);
    }

    /// <summary>The header must declare 8-bit truecolour with alpha, uninterlaced.</summary>
    [Fact]
    public void HeaderDeclaresEightBitRgbaWithoutInterlacing()
    {
        Bgra32[] source = new Bgra32[1];

        using MemoryStream stream = new();
        PngWriter.Write(stream, source, 1, 1);
        byte[] file = stream.ToArray();

        // Signature, then the length and type of the first chunk, then its payload.
        ReadOnlySpan<byte> header = file.AsSpan(8 + 8, 13);

        Assert.Equal(1, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[..4]));
        Assert.Equal(1, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.Slice(4, 4)));
        Assert.Equal(8, header[8]);     // bits per channel
        Assert.Equal(6, header[9]);     // truecolour with alpha
        Assert.Equal(0, header[10]);    // deflate
        Assert.Equal(0, header[11]);    // adaptive filtering
        Assert.Equal(0, header[12]);    // not interlaced
    }

    /// <summary>Anything that is not a PNG must be refused rather than half-read.</summary>
    [Fact]
    public void ReaderRejectsWhatIsNotAPng() =>
        Assert.Throws<InvalidDataException>(() => PngReader.Read("not a png at all"u8));

    /// <summary>CRC-32 as the format defines it, computed bit by bit.</summary>
    /// <remarks>
    /// Deliberately not the production implementation's approach. That one drives a precomputed table,
    /// and a table built wrongly would agree with itself perfectly — so the check here works from the
    /// polynomial directly and would disagree.
    /// </remarks>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte value in data)
        {
            crc ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Every filter a row can be encoded under survives the trip back.</summary>
    /// <remarks>
    /// The encoder now picks a filter per row, so a picture wide enough and varied enough exercises all
    /// five in one file. A predictor implemented with the wrong neighbour still round-trips ITS OWN rows
    /// under a matching bug, which is why the pixels are checked against the source rather than against
    /// a second encode.
    /// </remarks>
    [Fact]
    public void AnImageUsingEveryFilterComesBackExactly()
    {
        const int Width = 61;
        const int Height = 47;

        Bgra32[] pixels = new Bgra32[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                // Flat bands, horizontal ramps, vertical ramps and noise in one picture, so no single
                // filter wins every row.
                byte value = (y % 4) switch
                {
                    0 => 128,
                    1 => (byte)(x * 4),
                    2 => (byte)(y * 5),
                    _ => (byte)((x * 37) + (y * 17)),
                };

                pixels[(y * Width) + x] = new Bgra32(value, (byte)(255 - value), (byte)(x + y), (byte)(x % 256));
            }
        }

        using MemoryStream stream = new();
        PngWriter.Write(stream, pixels, Width, Height);

        (Bgra32[] Pixels, int Width, int Height) read = PngReader.Read(stream.ToArray());

        Assert.Equal(Width, read.Width);
        Assert.Equal(Height, read.Height);
        Assert.Equal(pixels, read.Pixels);
    }

    /// <summary>Filtering is doing something: a smooth picture encodes far below its raw size.</summary>
    /// <remarks>
    /// The guard against a silent regression to filter zero everywhere, which is correct, passes every
    /// round-trip test, and quietly costs most of the compression on exactly the material a texture pack
    /// is made of. A gradient is the clearest case: left-predicted, it is almost entirely zeroes.
    /// </remarks>
    [Fact]
    public void ASmoothGradientCompressesFarBelowItsRawSize()
    {
        const int Side = 256;

        Bgra32[] pixels = new Bgra32[Side * Side];

        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                pixels[(y * Side) + x] = new Bgra32((byte)x, (byte)y, (byte)((x + y) / 2), 255);
            }
        }

        using MemoryStream stream = new();
        PngWriter.Write(stream, pixels, Side, Side);

        long raw = (long)Side * Side * 4;

        Assert.True(
            stream.Length * 20 < raw,
            $"A {Side}x{Side} gradient encoded to {stream.Length} bytes against {raw} raw, which is not "
                + "the shape of a filtered encode -- filter selection has probably regressed to zero.");
    }
}
