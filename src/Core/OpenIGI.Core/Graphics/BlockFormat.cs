namespace OpenIGI.Core.Graphics;

/// <summary>
/// A GPU block-compression format: texels stored as four-by-four blocks of a fixed size.
/// </summary>
/// <remarks>
/// <para>
/// A texture pack's pictures are four times the retail resolution, and uncompressed that is four bytes a
/// texel in memory, in upload staging and on the GPU. A block format is what the GPU samples directly at a
/// quarter or an eighth of that -- the texture is never expanded anywhere.
/// </para>
/// <para>
/// <see cref="Bc1"/> for what has no alpha, at half a byte a texel; <see cref="Bc7"/> for anything that
/// has, and for the derived surface maps whose four channels are all data, at one byte a texel.
/// </para>
/// </remarks>
public enum BlockFormat
{
    /// <summary>No block compression.</summary>
    None = 0,

    /// <summary>BC1: eight bytes a block, colour with at most one bit of alpha.</summary>
    Bc1 = 1,

    /// <summary>BC7: sixteen bytes a block, colour and alpha.</summary>
    Bc7 = 7,
}

/// <summary>Sizes of block-compressed textures.</summary>
public static class BlockCompression
{
    /// <summary>The edge of a block, in texels.</summary>
    public const int BlockSize = 4;

    /// <summary>How many bytes one block occupies.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The block's size in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is <see cref="BlockFormat.None"/> or unknown.</exception>
    public static int BlockBytes(BlockFormat format) => format switch
    {
        BlockFormat.Bc1 => 8,
        BlockFormat.Bc7 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a block format."),
    };

    /// <summary>How many bytes one row of blocks occupies, for a level this many texels wide.</summary>
    /// <param name="format">The format.</param>
    /// <param name="width">The level's width in texels.</param>
    /// <returns>The row pitch in bytes.</returns>
    public static int RowBytes(BlockFormat format, int width) =>
        Blocks(width) * BlockBytes(format);

    /// <summary>How many bytes one level occupies.</summary>
    /// <param name="format">The format.</param>
    /// <param name="width">The level's width in texels.</param>
    /// <param name="height">The level's height in texels.</param>
    /// <returns>The level's size in bytes.</returns>
    /// <remarks>Partial blocks count whole: a two-by-two level is still one block.</remarks>
    public static int LevelBytes(BlockFormat format, int width, int height) =>
        Blocks(width) * Blocks(height) * BlockBytes(format);

    /// <summary>How many bytes a whole chain occupies, every level from this size down to one texel.</summary>
    /// <param name="format">The format.</param>
    /// <param name="width">The width of level zero.</param>
    /// <param name="height">The height of level zero.</param>
    /// <param name="levels">How many levels.</param>
    /// <returns>The chain's size in bytes.</returns>
    public static long ChainBytes(BlockFormat format, int width, int height, int levels)
    {
        long total = 0;
        for (int level = 0; level < levels; level++)
        {
            total += LevelBytes(format, Math.Max(1, width >> level), Math.Max(1, height >> level));
        }

        return total;
    }

    private static int Blocks(int texels) => (Math.Max(1, texels) + BlockSize - 1) / BlockSize;
}
