using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// A texture stored block-compressed, with its whole mip chain, exactly as the GPU samples it.
/// </summary>
/// <remarks>
/// <para>
/// What a texture pack ships for every world texture: the chain was built, cut-out alpha widened and its
/// coverage preserved when the pack was made -- the same steps the loader runs on a PNG -- and the result
/// encoded once. Loading one is therefore no work at all. The levels are a slice of the file's own bytes
/// and go to the GPU from there, with nothing decoded and nothing copied on the way.
/// </para>
/// <para>
/// <see cref="Proxy"/> is a small uncompressed copy for whatever reads a texture's pixels on the CPU --
/// the traced albedo, lamp detection, the lightmap tint -- none of which needs more than a summary of it.
/// </para>
/// </remarks>
public sealed class CompressedTextureImage
{
    private readonly int[] _levelOffsets;

    /// <summary>Initialises a new instance of the <see cref="CompressedTextureImage"/> class.</summary>
    /// <param name="format">The block format.</param>
    /// <param name="width">The width of level zero in texels.</param>
    /// <param name="height">The height of level zero in texels.</param>
    /// <param name="mipLevels">How many levels <paramref name="levels"/> holds.</param>
    /// <param name="levels">Every level's blocks, back to back, largest first.</param>
    /// <param name="isCutout">Whether the surface is alpha tested rather than opaque or blended.</param>
    /// <param name="proxy">A small uncompressed copy for CPU readers, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">The levels are smaller than the chain they describe.</exception>
    public CompressedTextureImage(
        BlockFormat format,
        int width,
        int height,
        int mipLevels,
        ReadOnlyMemory<byte> levels,
        bool isCutout,
        TextureImage? proxy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipLevels);

        long needed = BlockCompression.ChainBytes(format, width, height, mipLevels);
        if (levels.Length < needed)
        {
            throw new ArgumentException(
                $"A {width}x{height} {format} chain of {mipLevels} level(s) needs {needed} bytes, got {levels.Length}.",
                nameof(levels));
        }

        Format = format;
        Width = width;
        Height = height;
        MipLevels = mipLevels;
        Levels = levels[..(int)needed];
        IsCutout = isCutout;
        Proxy = proxy;

        _levelOffsets = new int[mipLevels];
        int at = 0;
        for (int level = 0; level < mipLevels; level++)
        {
            _levelOffsets[level] = at;
            at += BlockCompression.LevelBytes(format, Math.Max(1, width >> level), Math.Max(1, height >> level));
        }
    }

    /// <summary>Gets the block format.</summary>
    public BlockFormat Format { get; }

    /// <summary>Gets the width of level zero in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of level zero in texels.</summary>
    public int Height { get; }

    /// <summary>Gets how many levels the chain has.</summary>
    public int MipLevels { get; }

    /// <summary>Gets every level's blocks, back to back, largest first.</summary>
    public ReadOnlyMemory<byte> Levels { get; }

    /// <summary>Gets a value indicating whether the surface is alpha tested.</summary>
    public bool IsCutout { get; }

    /// <summary>Gets a small uncompressed copy for CPU readers, or <see langword="null"/> when there is none.</summary>
    public TextureImage? Proxy { get; }

    /// <summary>Gets one level's blocks.</summary>
    /// <param name="level">The zero-based level.</param>
    /// <returns>That level's blocks, row-major.</returns>
    public ReadOnlyMemory<byte> GetLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, MipLevels);
        (int width, int height) = GetLevelSize(level);
        return Levels.Slice(_levelOffsets[level], BlockCompression.LevelBytes(Format, width, height));
    }

    /// <summary>Gets the size of a level in texels.</summary>
    /// <param name="level">The zero-based level.</param>
    /// <returns>Its width and height.</returns>
    public (int Width, int Height) GetLevelSize(int level) =>
        (Math.Max(1, Width >> level), Math.Max(1, Height >> level));

    /// <summary>Expands the whole chain to uncompressed pixels.</summary>
    /// <returns>The same chain, level for level, as an uncompressed texture.</returns>
    /// <remarks>
    /// For a device that cannot sample block formats, and for the few readers that need a texture's every
    /// pixel. Slow, and four to eight times the memory: the ordinary path never calls it.
    /// </remarks>
    public TextureImage Decode()
    {
        int total = 0;
        for (int level = 0; level < MipLevels; level++)
        {
            (int width, int height) = GetLevelSize(level);
            total += width * height;
        }

        Bgra32[] pixels = new Bgra32[total];
        int[] offsets = new int[MipLevels];
        int at = 0;
        for (int level = 0; level < MipLevels; level++)
        {
            (int width, int height) = GetLevelSize(level);
            offsets[level] = at;
            BlockCodec.Decode(GetLevel(level).Span, width, height, Format, pixels.AsSpan(at, width * height));
            at += width * height;
        }

        return new TextureImage(Width, Height, MipLevels, pixels, offsets);
    }

    /// <inheritdoc/>
    public override string ToString() => $"CompressedTextureImage {Format} {Width}x{Height} mips={MipLevels}";
}
