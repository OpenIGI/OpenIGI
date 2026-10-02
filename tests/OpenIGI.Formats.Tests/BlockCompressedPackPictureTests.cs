using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The block-compressed form of a pack picture: a stored chain the game uploads as it is.
/// </summary>
public sealed class BlockCompressedPackPictureTests
{
    [Fact]
    public void ACompressedFrameComesBackAsTheSameChainWithItsProxy()
    {
        CompressedTextureImage stored = Compress(Gradient(16, 8, alpha: 255), BlockFormat.Bc1);
        TextureImage proxy = Gradient(4, 2, alpha: 255);
        CompressedTextureImage withProxy = new(
            stored.Format, stored.Width, stored.Height, stored.MipLevels, stored.Levels, isCutout: true, proxy);

        byte[] file = PackPicture.Write(new PackPicture.Content(
            [new PackPicture.Frame(proxy, 16, 8, Compressed: withProxy)], 16, 8, 0, 0, default));

        PackPicture.Frame frame = PackPicture.ReadFirstFrame((ReadOnlyMemory<byte>)file);

        Assert.NotNull(frame.Compressed);
        Assert.Equal(BlockFormat.Bc1, frame.Compressed!.Format);
        Assert.Equal(16, frame.Compressed.Width);
        Assert.Equal(8, frame.Compressed.Height);
        Assert.Equal(stored.MipLevels, frame.Compressed.MipLevels);
        Assert.True(frame.Compressed.IsCutout);
        Assert.Equal(stored.Levels.ToArray(), frame.Compressed.Levels.ToArray());
        Assert.Equal(proxy.GetLevel(0).ToArray(), frame.Image.GetLevel(0).ToArray());
    }

    [Fact]
    public void TheChainIsASliceOfTheFileNotACopy()
    {
        // The point of the format: a level's stored chains go to the GPU from the registry's own bytes.
        CompressedTextureImage stored = Compress(Gradient(8, 8, alpha: 255), BlockFormat.Bc1);
        byte[] file = PackPicture.Write(new PackPicture.Content(
            [new PackPicture.Frame(Gradient(2, 2, 255), 8, 8, Compressed: stored)], 8, 8, 0, 0, default));

        PackPicture.Frame frame = PackPicture.ReadFirstFrame((ReadOnlyMemory<byte>)file);

        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(
            frame.Compressed!.Levels, out ArraySegment<byte> segment));
        Assert.Same(file, segment.Array);
    }

    [Fact]
    public void TheSurfaceMapTravelsCompressedBesideThePicture()
    {
        CompressedTextureImage picture = Compress(Gradient(8, 8, alpha: 255), BlockFormat.Bc1);
        CompressedTextureImage map = Compress(Gradient(8, 8, alpha: 128), BlockFormat.Bc7);

        byte[] file = PackPicture.Write(new PackPicture.Content(
            [new PackPicture.Frame(Gradient(2, 2, 255), 8, 8, Compressed: picture, CompressedSurface: map)],
            8, 8, 0, 0, default));

        SurfaceAsset asset = SurfaceAssetReader.ReadKeepingBlocks(file);

        Assert.NotNull(asset.CompressedDiffuse);
        Assert.NotNull(asset.CompressedDetail);
        Assert.Equal(BlockFormat.Bc7, asset.CompressedDetail!.Format);
        Assert.Equal(map.Levels.ToArray(), asset.CompressedDetail.Levels.ToArray());
    }

    [Fact]
    public void AReaderThatWantsPixelsGetsTheChainExpanded()
    {
        // A ramp along one axis: a block stores its colours along a line, so this is what a block format
        // can represent faithfully, and what a channel or row mix-up would wreck.
        Bgra32[] ramp = new Bgra32[16 * 16];
        for (int i = 0; i < ramp.Length; i++)
        {
            byte value = (byte)((i % 16) * 17);
            ramp[i] = new Bgra32(value, (byte)(255 - value), 64, 255);
        }

        TextureImage source = TextureImage.FromPixels(16, 16, ramp);
        CompressedTextureImage stored = Compress(source, BlockFormat.Bc7);
        byte[] file = PackPicture.Write(new PackPicture.Content(
            [new PackPicture.Frame(Gradient(2, 2, 255), 16, 16, Compressed: stored)], 16, 16, 0, 0, default));

        TextureImage expanded = TexFile.Load(file);

        Assert.Equal(16, expanded.Width);
        Assert.Equal(stored.MipLevels, expanded.MipLevels);

        // Within a few levels of the source; a decode that mixed up channel order or rows would be off by
        // a hundred or more.
        ReadOnlySpan<Bgra32> original = source.GetLevel(0);
        ReadOnlySpan<Bgra32> decoded = expanded.GetLevel(0);
        long error = 0;
        for (int i = 0; i < original.Length; i++)
        {
            int r = Math.Abs(original[i].R - decoded[i].R);
            int g = Math.Abs(original[i].G - decoded[i].G);
            int b = Math.Abs(original[i].B - decoded[i].B);
            Assert.InRange(Math.Max(r, Math.Max(g, b)), 0, 12);
            error += r + g + b;
        }

        Assert.InRange(error / (3.0 * original.Length), 0.0, 3.0);
    }

    [Fact]
    public void FramesMayNotMixCompressedAndPng()
    {
        CompressedTextureImage stored = Compress(Gradient(4, 4, alpha: 255), BlockFormat.Bc1);
        PackPicture.Frame[] frames =
        [
            new(Gradient(4, 4, 255), 4, 4, Compressed: stored),
            new(Gradient(4, 4, 255), 4, 4),
        ];

        Assert.Throws<TextureFormatException>(() =>
            PackPicture.Write(new PackPicture.Content(frames, 4, 4, 0, 0, default)));
    }

    [Fact]
    public void ChainSizesCountPartialBlocksWhole()
    {
        // 16x8 -> 16x8, 8x4, 4x2, 2x1, 1x1: 8 + 2 + 1 + 1 + 1 blocks of eight bytes.
        Assert.Equal(13 * 8, BlockCompression.ChainBytes(BlockFormat.Bc1, 16, 8, 5));
        Assert.Equal(16, BlockCompression.LevelBytes(BlockFormat.Bc7, 1, 1));
        Assert.Equal(32, BlockCompression.RowBytes(BlockFormat.Bc1, 13));
    }

    private static CompressedTextureImage Compress(TextureImage image, BlockFormat format)
    {
        TextureImage chain = image.WithMipChain();
        using MemoryStream blocks = new();
        for (int level = 0; level < chain.MipLevels; level++)
        {
            (int width, int height) = chain.GetLevelSize(level);
            blocks.Write(BlockCodec.Encode(chain.GetLevel(level), width, height, format, BlockQuality.Fast));
        }

        return new CompressedTextureImage(
            format, chain.Width, chain.Height, chain.MipLevels, blocks.ToArray(), isCutout: false, proxy: null);
    }

    private static TextureImage Gradient(int width, int height, byte alpha)
    {
        Bgra32[] pixels = new Bgra32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = new Bgra32(
                    (byte)(x * 255 / Math.Max(1, width - 1)),
                    (byte)(y * 255 / Math.Max(1, height - 1)),
                    128,
                    alpha);
            }
        }

        return TextureImage.FromPixels(width, height, pixels);
    }
}
