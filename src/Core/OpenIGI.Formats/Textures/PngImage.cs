using System.Buffers.Binary;
using System.IO.Compression;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// Reads PNG images: truecolour, greyscale or palette, with or without alpha, at the bit depths those come in (palette and
/// greyscale at 1, 2, 4 or 8 bits; the rest at 8), without interlacing. What the menus need it for -- a player's Discord
/// avatar -- is always such a PNG; an interlaced or 16-bit one is refused rather than drawn wrong.
/// </summary>
public static class PngImage
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Gets a value indicating whether the bytes begin with the PNG signature.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns><see langword="true"/> for a PNG.</returns>
    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>Decodes a PNG into <see cref="Bgra32"/> pixels.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The image, with one mip level.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a PNG this reader handles.</exception>
    public static TextureImage Read(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data))
        {
            throw new TextureFormatException("Not a PNG.");
        }

        int width = 0, height = 0, depth = 0, colour = -1;
        byte[] palette = [];
        byte[] alphas = [];
        using MemoryStream compressed = new();
        int at = 8;
        while (at + 12 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data[at..]);
            if (length < 0 || at + 12 + length > data.Length)
            {
                throw new TextureFormatException("A PNG chunk runs past the file.");
            }

            ReadOnlySpan<byte> type = data.Slice(at + 4, 4);
            ReadOnlySpan<byte> body = data.Slice(at + 8, length);
            at += 12 + length;
            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                depth = body[8];
                colour = body[9];
                if (body[10] != 0 || body[11] != 0 || body[12] != 0)
                {
                    throw new TextureFormatException("An interlaced or unusual PNG isn't read.");
                }
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = body.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                alphas = body.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        int channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        bool depthFits = colour is 0 or 3 ? depth is 1 or 2 or 4 or 8 : depth == 8;
        if (width <= 0 || height <= 0 || channels == 0 || !depthFits || (long)width * height > 64L * 1024 * 1024)
        {
            throw new TextureFormatException("A PNG of a kind this reader doesn't handle.");
        }

        int bitsPerPixel = channels * depth;
        int stride = ((width * bitsPerPixel) + 7) / 8;
        int step = Math.Max(1, bitsPerPixel / 8);
        byte[] raw = Inflate(compressed, (stride + 1) * height);
        Unfilter(raw, stride, step, height);
        return TextureImage.FromPixels(width, height, Pixels(raw, width, height, stride, depth, colour, palette, alphas));
    }

    private static byte[] Inflate(MemoryStream compressed, int size)
    {
        compressed.Position = 0;
        byte[] raw = new byte[size];
        try
        {
            using ZLibStream inflate = new(compressed, CompressionMode.Decompress);
            inflate.ReadExactly(raw);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            throw new TextureFormatException("A PNG's image data doesn't decompress.", e);
        }

        return raw;
    }

    // Each row's filter undone in place: the row's bytes become the image's.
    private static void Unfilter(byte[] raw, int stride, int step, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = (y * (stride + 1)) + 1;
            int above = row - (stride + 1);
            byte filter = raw[row - 1];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= step ? raw[row + x - step] : 0;
                int b = y > 0 ? raw[above + x] : 0;
                int c = x >= step && y > 0 ? raw[above + x - step] : 0;
                int add = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new TextureFormatException("A PNG row has an unknown filter."),
                };
                raw[row + x] = (byte)(raw[row + x] + add);
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static Bgra32[] Pixels(byte[] raw, int width, int height, int stride, int depth, int colour, byte[] palette, byte[] alphas)
    {
        var pixels = new Bgra32[width * height];
        int scale = depth < 8 ? 255 / ((1 << depth) - 1) : 1;
        for (int y = 0; y < height; y++)
        {
            int row = (y * (stride + 1)) + 1;
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = colour switch
                {
                    2 => Bgra32.FromRgba(raw[row + (x * 3)], raw[row + (x * 3) + 1], raw[row + (x * 3) + 2], 255),
                    6 => Bgra32.FromRgba(raw[row + (x * 4)], raw[row + (x * 4) + 1], raw[row + (x * 4) + 2], raw[row + (x * 4) + 3]),
                    4 => Grey(raw[row + (x * 2)], raw[row + (x * 2) + 1]),
                    0 => Grey((byte)(Sample(raw, row, x, depth) * scale), 255),
                    _ => Indexed(Sample(raw, row, x, depth), palette, alphas),
                };
            }
        }

        return pixels;
    }

    // A pixel's value at a bit depth under a byte: packed from the high bits down.
    private static int Sample(byte[] raw, int row, int x, int depth)
    {
        if (depth == 8)
        {
            return raw[row + x];
        }

        int bit = x * depth;
        return (raw[row + (bit >> 3)] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
    }

    private static Bgra32 Grey(byte value, byte alpha) => Bgra32.FromRgba(value, value, value, alpha);

    private static Bgra32 Indexed(int index, byte[] palette, byte[] alphas)
    {
        if ((index * 3) + 2 >= palette.Length)
        {
            throw new TextureFormatException("A PNG pixel names a colour its palette hasn't.");
        }

        byte alpha = index < alphas.Length ? alphas[index] : (byte)255;
        return Bgra32.FromRgba(palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2], alpha);
    }
}
