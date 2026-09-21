using System.Buffers.Binary;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// <see cref="BitmapImage"/> against hand-built files, so the decode and every refusal are covered on a
/// checkout with no game data.
/// </summary>
public sealed class BitmapImageTests
{
    /// <summary>A bottom-up 24-bit bitmap comes back top-down, with blue in byte zero.</summary>
    /// <remarks>
    /// Both halves matter. A bitmap row already stores blue, green, red, so no swizzle is needed — and a
    /// positive height means the file stores its last row first, which is the part that is silently wrong
    /// if it is missed.
    /// </remarks>
    [Fact]
    public void A_bottom_up_bitmap_is_flipped_and_keeps_its_channel_order()
    {
        // Two rows of one pixel: the file's first row is the image's bottom.
        byte[] file = Build(1, 2, bitsPerPixel: 24, bottomUp: true, rows:
        [
            [0x10, 0x20, 0x30],
            [0x40, 0x50, 0x60],
        ]);

        BitmapImage image = BitmapImage.Read(file);

        Assert.Equal(1, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new Bgra32(0x40, 0x50, 0x60, 255), image.GetPixel(0, 0));
        Assert.Equal(new Bgra32(0x10, 0x20, 0x30, 255), image.GetPixel(0, 1));
    }

    /// <summary>A negative height means the rows are already top-down and are not flipped again.</summary>
    [Fact]
    public void A_top_down_bitmap_is_not_flipped()
    {
        byte[] file = Build(1, 2, bitsPerPixel: 24, bottomUp: false, rows:
        [
            [0x10, 0x20, 0x30],
            [0x40, 0x50, 0x60],
        ]);

        BitmapImage image = BitmapImage.Read(file);

        Assert.Equal(new Bgra32(0x10, 0x20, 0x30, 255), image.GetPixel(0, 0));
        Assert.Equal(new Bgra32(0x40, 0x50, 0x60, 255), image.GetPixel(0, 1));
    }

    /// <summary>Rows are padded to four bytes, so a three-pixel 24-bit row is twelve bytes, not nine.</summary>
    /// <remarks>
    /// The classic bitmap trap. Reading a row as <c>width * 3</c> shifts every row after the first by
    /// three bytes, which shears the image rather than corrupting it — easy to miss on a photograph and
    /// impossible to miss on a cube-map seam.
    /// </remarks>
    [Fact]
    public void Rows_are_padded_to_four_bytes()
    {
        byte[] file = Build(3, 2, bitsPerPixel: 24, bottomUp: false, rows:
        [
            [1, 1, 1, 2, 2, 2, 3, 3, 3, 0xFF, 0xFF, 0xFF],
            [4, 4, 4, 5, 5, 5, 6, 6, 6, 0xFF, 0xFF, 0xFF],
        ]);

        BitmapImage image = BitmapImage.Read(file);

        Assert.Equal(3, image.GetPixel(2, 0).B);
        Assert.Equal(4, image.GetPixel(0, 1).B);
        Assert.Equal(6, image.GetPixel(2, 1).B);
    }

    /// <summary>A 32-bit bitmap keeps the alpha the file stored.</summary>
    [Fact]
    public void A_thirty_two_bit_bitmap_keeps_its_alpha()
    {
        byte[] file = Build(1, 1, bitsPerPixel: 32, bottomUp: false, rows: [[0x10, 0x20, 0x30, 0x77]]);

        BitmapImage image = BitmapImage.Read(file);

        Assert.Equal(new Bgra32(0x10, 0x20, 0x30, 0x77), image.GetPixel(0, 0));
    }

    /// <summary>Bytes that do not begin with the magic are refused.</summary>
    [Fact]
    public void A_file_that_is_not_a_bitmap_is_refused()
    {
        Assert.Throws<BitmapFormatException>(() => BitmapImage.Read(new byte[64]));
    }

    /// <summary>A pixel format this reader does not support is refused, not approximated.</summary>
    /// <param name="bitsPerPixel">The depth to declare.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    public void An_unsupported_depth_is_refused(int bitsPerPixel)
    {
        byte[] file = Build(1, 1, bitsPerPixel: 24, bottomUp: false, rows: [[0, 0, 0]]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), (ushort)bitsPerPixel);

        Assert.Throws<BitmapFormatException>(() => BitmapImage.Read(file));
    }

    /// <summary>A compressed bitmap is refused.</summary>
    [Fact]
    public void A_compressed_bitmap_is_refused()
    {
        byte[] file = Build(1, 1, bitsPerPixel: 24, bottomUp: false, rows: [[0, 0, 0]]);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(30), 1);

        Assert.Throws<BitmapFormatException>(() => BitmapImage.Read(file));
    }

    /// <summary>A file too short for the image it declares is refused rather than read past its end.</summary>
    [Fact]
    public void A_truncated_bitmap_is_refused()
    {
        byte[] file = Build(4, 4, bitsPerPixel: 24, bottomUp: false, rows: [[0, 0, 0]]);

        Assert.Throws<BitmapFormatException>(() => BitmapImage.Read(file));
    }

    /// <summary>Sampling outside the image throws.</summary>
    [Fact]
    public void Sampling_outside_the_image_throws()
    {
        BitmapImage image = BitmapImage.Read(Build(1, 1, 24, false, [[0, 0, 0]]));

        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.GetPixel(0, -1));
    }

    /// <summary>Builds a bitmap, padding each supplied row out to the four-byte stride a file uses.</summary>
    private static byte[] Build(int width, int height, int bitsPerPixel, bool bottomUp, byte[][] rows)
    {
        int pixelOffset = BitmapImage.FileHeaderSize + BitmapImage.InfoHeaderSize;
        int stride = ((width * (bitsPerPixel / 8)) + 3) & ~3;
        int payload = rows.Sum(r => Math.Max(r.Length, stride));
        byte[] file = new byte[pixelOffset + payload];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), pixelOffset);

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), BitmapImage.InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), bottomUp ? height : -height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), (ushort)bitsPerPixel);

        int at = pixelOffset;
        foreach (byte[] row in rows)
        {
            row.CopyTo(file, at);
            at += Math.Max(row.Length, stride);
        }

        return file;
    }
}
