using System.Buffers.Binary;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>The TGA reader, on files built here so every path is exercised.</summary>
/// <remarks>
/// The retail corpus is all uncompressed, so the run-length paths would otherwise never run. These
/// build the cases the shipped files do not cover.
/// </remarks>
public sealed class TgaImageTests
{
    /// <summary>A 24-bit image is read as opaque BGR, bottom row first.</summary>
    /// <remarks>
    /// The commonest shape by far — 162 of the corpus's 197 files. The flip is the part worth checking:
    /// a TGA with the origin bit clear stores its bottom row first, and a reader that skipped the flip
    /// would produce an image that looks plausible and is upside down.
    /// </remarks>
    [Fact]
    public void A_24_bit_image_is_read_bottom_row_first()
    {
        // Two rows: the stored first row is the image's bottom.
        byte[] tga = Build(imageType: 2, depth: 24, width: 2, height: 2, descriptor: 0,
        [
            10, 20, 30, 11, 21, 31,   // bottom row
            40, 50, 60, 41, 51, 61,   // top row
        ]);

        TextureImage image = TgaImage.Read(tga);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);

        // Top-left of the result is the first pixel of the LAST stored row.
        Assert.Equal(new Bgra32(40, 50, 60, 255), image.Pixels[0]);
        Assert.Equal(new Bgra32(10, 20, 30, 255), image.Pixels[2]);
    }

    /// <summary>A 32-bit image keeps its alpha.</summary>
    /// <remarks>31 of the corpus's files are this, and they are the ones with cut-outs — clouds, foliage.</remarks>
    [Fact]
    public void A_32_bit_image_keeps_its_alpha()
    {
        byte[] tga = Build(imageType: 2, depth: 32, width: 1, height: 1, descriptor: 8, [1, 2, 3, 128]);

        Assert.Equal(new Bgra32(1, 2, 3, 128), TgaImage.Read(tga).Pixels[0]);
    }

    /// <summary>The origin bit is honoured, so a top-down file is not flipped again.</summary>
    [Fact]
    public void A_top_down_image_is_not_flipped()
    {
        byte[] tga = Build(imageType: 2, depth: 24, width: 1, height: 2, descriptor: 0x20, [1, 1, 1, 9, 9, 9]);

        TextureImage image = TgaImage.Read(tga);
        Assert.Equal(new Bgra32(1, 1, 1, 255), image.Pixels[0]);
    }

    /// <summary>A greyscale image spreads its one channel across all three.</summary>
    [Fact]
    public void A_greyscale_image_spreads_its_channel()
    {
        byte[] tga = Build(imageType: 3, depth: 8, width: 1, height: 1, descriptor: 0, [77]);

        Assert.Equal(new Bgra32(77, 77, 77, 255), TgaImage.Read(tga).Pixels[0]);
    }

    /// <summary>A run-length image decodes its runs and its literals.</summary>
    /// <remarks>
    /// No retail file is run-length, so this is the only thing that runs that half of the reader. A run
    /// packet has the high bit set and one pixel after it; a literal packet does not and has as many
    /// pixels as it counts.
    /// </remarks>
    [Fact]
    public void A_run_length_image_decodes_runs_and_literals()
    {
        byte[] body =
        [
            0x82, 5, 6, 7,           // a run of three
            0x01, 1, 1, 1, 2, 2, 2,  // a literal of two
        ];

        byte[] tga = Build(imageType: 10, depth: 24, width: 5, height: 1, descriptor: 0x20, body);
        TextureImage image = TgaImage.Read(tga);

        Assert.Equal(new Bgra32(5, 6, 7, 255), image.Pixels[0]);
        Assert.Equal(new Bgra32(5, 6, 7, 255), image.Pixels[2]);
        Assert.Equal(new Bgra32(1, 1, 1, 255), image.Pixels[3]);
        Assert.Equal(new Bgra32(2, 2, 2, 255), image.Pixels[4]);
    }

    /// <summary>A colour-mapped image resolves its indices.</summary>
    [Fact]
    public void A_colour_mapped_image_resolves_its_indices()
    {
        byte[] tga =
        [
            0, 1, 1,                      // no id, a colour map, colour-mapped image
            0, 0, 2, 0, 24,               // map: first 0, 2 entries, 24 bits each
            0, 0, 0, 0,                   // origin
            2, 0, 1, 0,                   // 2x1
            8, 0x20,                      // 8 bits, top-down
            9, 8, 7,                      // entry 0
            1, 2, 3,                      // entry 1
            1, 0,                         // pixels: index 1, index 0
        ];

        TextureImage image = TgaImage.Read(tga);

        Assert.Equal(new Bgra32(1, 2, 3, 255), image.Pixels[0]);
        Assert.Equal(new Bgra32(9, 8, 7, 255), image.Pixels[1]);
    }

    /// <summary>Truncated and unsupported files are refused rather than half-read.</summary>
    [Fact]
    public void A_malformed_file_is_refused()
    {
        Assert.Throws<TextureFormatException>(() => TgaImage.Read(new byte[4]));

        // Declares more pixels than it carries.
        byte[] short_ = Build(imageType: 2, depth: 24, width: 8, height: 8, descriptor: 0, [1, 2, 3]);
        Assert.Throws<TextureFormatException>(() => TgaImage.Read(short_));

        // An image type no reader handles.
        byte[] unknown = Build(imageType: 4, depth: 24, width: 1, height: 1, descriptor: 0, [1, 2, 3]);
        Assert.Throws<TextureFormatException>(() => TgaImage.Read(unknown));
    }

    /// <summary>The shape check accepts a TGA and rejects other things.</summary>
    /// <remarks>
    /// TGA has no magic number, so this is the weakest of the format probes and only sound after the
    /// formats that do have one have been ruled out — which is how the texture loader uses it.
    /// </remarks>
    [Fact]
    public void The_shape_check_is_honest_about_what_it_can_tell()
    {
        Assert.True(TgaImage.IsTga(Build(imageType: 2, depth: 24, width: 1, height: 1, descriptor: 0, [1, 2, 3])));
        Assert.False(TgaImage.IsTga([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(TgaImage.IsTga([]));
    }

    private static byte[] Build(int imageType, int depth, int width, int height, int descriptor, byte[] body)
    {
        byte[] header = new byte[TgaImage.HeaderSize];
        header[2] = (byte)imageType;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = (byte)depth;
        header[17] = (byte)descriptor;

        return [.. header, .. body];
    }
}
