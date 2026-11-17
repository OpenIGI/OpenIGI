using System.Buffers.Binary;
using System.IO.Compression;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The PNG reader: every row filter, palette and greyscale at small bit depths with transparency, read to the pixels that
/// were written; PNGs Skia writes (as a browser or Discord would) read back the same; anything it doesn't handle refused.
/// </summary>
public sealed class PngImageTests
{
    /// <summary>A truecolour image with alpha, its rows written under each of the five filters, reads back exactly.</summary>
    [Fact]
    public void Every_row_filter_is_undone()
    {
        const int width = 7, height = 5;
        byte[] rgba = new byte[width * height * 4];
        new Random(7).NextBytes(rgba);

        // A row each under filter 0 to 4.
        byte[] raw = new byte[height * ((width * 4) + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * ((width * 4) + 1);
            raw[row] = (byte)y;
            for (int x = 0; x < width * 4; x++)
            {
                int a = x >= 4 ? rgba[(y * width * 4) + x - 4] : 0;
                int b = y > 0 ? rgba[((y - 1) * width * 4) + x] : 0;
                int c = x >= 4 && y > 0 ? rgba[((y - 1) * width * 4) + x - 4] : 0;
                int predicted = y switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, c) };
                raw[row + 1 + x] = (byte)(rgba[(y * width * 4) + x] - predicted);
            }
        }

        TextureImage image = PngImage.Read(Png(width, height, 8, 6, raw));
        ReadOnlySpan<Bgra32> pixels = image.GetLevel(0);
        for (int i = 0; i < width * height; i++)
        {
            Assert.Equal(Bgra32.FromRgba(rgba[i * 4], rgba[(i * 4) + 1], rgba[(i * 4) + 2], rgba[(i * 4) + 3]), pixels[i]);
        }
    }

    /// <summary>A 4-bit palette with transparent entries, and 1-bit greyscale, read to the colours and greys they name.</summary>
    [Fact]
    public void Palettes_and_small_greys_are_read()
    {
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] alphas = [128, 255];

        // Three pixels of 4 bits: indices 0, 1, 2, then padding.
        byte[] indexed = [0, 0x01, 0x20];
        TextureImage colours = PngImage.Read(Png(3, 1, 4, 3, indexed, palette, alphas));
        Assert.Equal([Bgra32.FromRgba(255, 0, 0, 128), Bgra32.FromRgba(0, 255, 0, 255), Bgra32.FromRgba(0, 0, 255, 255)], colours.GetLevel(0).ToArray());

        // Eight pixels of 1 bit: 1010 0110.
        TextureImage greys = PngImage.Read(Png(8, 1, 1, 0, [0, 0b1010_0110]));
        Assert.Equal([255, 0, 255, 0, 0, 255, 255, 0], greys.GetLevel(0).ToArray().Select(static p => (int)p.R));
    }

    /// <summary>A PNG Skia writes, opaque and with alpha, reads back to Skia's own pixels.</summary>
    [Theory]
    [InlineData(SKAlphaType.Opaque)]
    [InlineData(SKAlphaType.Unpremul)]
    public void Pngs_skia_writes_read_back_the_same(SKAlphaType alpha)
    {
        using SKBitmap bitmap = new(new SKImageInfo(37, 23, SKColorType.Rgba8888, alpha));
        var random = new Random(23);
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha == SKAlphaType.Opaque ? (byte)255 : (byte)random.Next(1, 256)));
            }
        }

        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        TextureImage image = PngImage.Read(encoded.ToArray());
        Assert.Equal((bitmap.Width, bitmap.Height), (image.Width, image.Height));
        ReadOnlySpan<Bgra32> pixels = image.GetLevel(0);
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor expected = bitmap.GetPixel(x, y);
                Assert.Equal(Bgra32.FromRgba(expected.Red, expected.Green, expected.Blue, expected.Alpha), pixels[(y * bitmap.Width) + x]);
            }
        }
    }

    /// <summary>Not a PNG, an interlaced one, and one whose data is cut short are refused.</summary>
    [Fact]
    public void What_it_cannot_read_is_refused()
    {
        Assert.Throws<TextureFormatException>(() => PngImage.Read([1, 2, 3]));
        byte[] interlaced = Png(1, 1, 8, 2, [0, 1, 2, 3]);
        interlaced[8 + 8 + 12] = 1;
        Assert.Throws<TextureFormatException>(() => PngImage.Read(interlaced));
        Assert.Throws<TextureFormatException>(() => PngImage.Read(Png(4, 4, 8, 6, [0, 1, 2])));
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // A PNG of filtered rows (each with its filter byte), its chunks' checksums left at zero: the reader doesn't check them.
    private static byte[] Png(int width, int height, byte depth, byte colour, byte[] raw, byte[]? palette = null, byte[]? alphas = null)
    {
        using MemoryStream file = new();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = colour;
        Chunk(file, "IHDR", header);
        if (palette is not null)
        {
            Chunk(file, "PLTE", palette);
        }

        if (alphas is not null)
        {
            Chunk(file, "tRNS", alphas);
        }

        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(MemoryStream file, string type, byte[] body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        file.Write(length);
        file.Write(System.Text.Encoding.ASCII.GetBytes(type));
        file.Write(body);
        file.Write(new byte[4]);
    }
}
