using System.Runtime.InteropServices;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>How hard the encoder searches for each block.</summary>
public enum BlockQuality
{
    /// <summary>Quickest; visibly coarser on gradients.</summary>
    Fast,

    /// <summary>The default: near the best quality at a fraction of its time.</summary>
    Balanced,

    /// <summary>Exhaustive; for a final pack build that has the time.</summary>
    Best,
}

/// <summary>
/// Encodes pixels into block formats and decodes them back, one level at a time.
/// </summary>
/// <remarks>
/// A seam over BCnEncoder.Net, so nothing else names it. Encoding happens when a pack is built; decoding
/// only where a device cannot sample a block format, so the run-time cost of having this at all is zero
/// on every adapter the game targets.
/// </remarks>
public static class BlockCodec
{
    /// <summary>Encodes one level.</summary>
    /// <param name="pixels">The level's pixels, row-major.</param>
    /// <param name="width">Its width in texels.</param>
    /// <param name="height">Its height in texels.</param>
    /// <param name="format">The block format to encode to.</param>
    /// <param name="quality">How hard to search.</param>
    /// <returns>The level's blocks, row-major, partial blocks padded.</returns>
    public static byte[] Encode(ReadOnlySpan<Bgra32> pixels, int width, int height, BlockFormat format, BlockQuality quality)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length < width * height)
        {
            throw new ArgumentException($"A {width}x{height} level needs {width * height} pixels.", nameof(pixels));
        }

        BcEncoder encoder = new(Compression(format));
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = quality switch
        {
            BlockQuality.Fast => CompressionQuality.Fast,
            BlockQuality.Best => CompressionQuality.BestQuality,
            _ => CompressionQuality.Balanced,
        };

        // One texture at a time per encoder; whoever encodes many runs them side by side.
        encoder.Options.IsParallel = false;

        byte[] blocks = encoder.EncodeToRawBytes(
            MemoryMarshal.AsBytes(pixels[..(width * height)]), width, height, PixelFormat.Bgra32, 0, out _, out _);

        int expected = BlockCompression.LevelBytes(format, width, height);
        if (blocks.Length != expected)
        {
            throw new TextureFormatException(
                $"Encoding a {width}x{height} level to {format} gave {blocks.Length} bytes, expected {expected}.");
        }

        return blocks;
    }

    /// <summary>Decodes one level.</summary>
    /// <param name="blocks">The level's blocks.</param>
    /// <param name="width">Its width in texels.</param>
    /// <param name="height">Its height in texels.</param>
    /// <param name="format">The block format.</param>
    /// <param name="destination">Where the pixels go, at least <paramref name="width"/> times <paramref name="height"/>.</param>
    public static void Decode(ReadOnlySpan<byte> blocks, int width, int height, BlockFormat format, Span<Bgra32> destination)
    {
        if (destination.Length < width * height)
        {
            throw new ArgumentException($"A {width}x{height} level needs {width * height} pixels.", nameof(destination));
        }

        BcDecoder decoder = new();
        decoder.Options.IsParallel = false;
        ColorRgba32[] decoded = decoder.DecodeRaw(blocks.ToArray(), width, height, Compression(format));

        for (int i = 0; i < width * height; i++)
        {
            ColorRgba32 texel = decoded[i];
            destination[i] = new Bgra32(texel.b, texel.g, texel.r, texel.a);
        }
    }

    private static CompressionFormat Compression(BlockFormat format) => format switch
    {
        BlockFormat.Bc1 => CompressionFormat.Bc1,
        BlockFormat.Bc7 => CompressionFormat.Bc7,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a block format."),
    };
}
