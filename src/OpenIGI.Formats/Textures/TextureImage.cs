using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// A decoded texture: the whole mip chain as <see cref="Bgra32"/> pixels, largest level first.
/// </summary>
/// <remarks>
/// Levels are stored back to back with no padding, exactly as the original's converters lay them out
/// in their scratch buffer, so the whole chain can be handed to a graphics device in one call.
/// </remarks>
public sealed class TextureImage
{
    private readonly Bgra32[] _pixels;
    private readonly int[] _levelOffsets;

    internal TextureImage(int width, int height, int mipLevels, Bgra32[] pixels, int[] levelOffsets)
    {
        Width = width;
        Height = height;
        MipLevels = mipLevels;
        _pixels = pixels;
        _levelOffsets = levelOffsets;
    }

    /// <summary>
    /// Returns this texture with a complete mip chain, building one if it shipped without.
    /// </summary>
    /// <returns>This instance when a chain already exists, otherwise a new image carrying one.</returns>
    /// <remarks>
    /// Most of this game's textures carry a single level, and a backend that generates none then samples
    /// full resolution at every distance -- which sparkles, and makes every fence and railing crawl. See
    /// <see cref="MipChainBuilder"/> for why anisotropy cannot stand in for the missing levels.
    /// </remarks>
    public TextureImage WithMipChain()
    {
        if (MipLevels > 1 || Width <= 0 || Height <= 0)
        {
            return this;
        }

        (Bgra32[] pixels, int[] offsets) = MipChainBuilder.Build(_pixels, Width, Height);
        return new TextureImage(Width, Height, offsets.Length, pixels, offsets);
    }

    /// <summary>
    /// Rescales the mip chain's alpha so an alpha-tested surface keeps the same amount of itself at
    /// every distance.
    /// </summary>
    /// <param name="cutoff">The alpha the shader tests against, as a 0..1 fraction.</param>
    /// <returns><see langword="true"/> when this texture was alpha tested and its levels were rescaled.</returns>
    /// <remarks>
    /// In place, on the decoded chain, before it is handed to a device. See <see cref="AlphaCoverage"/>
    /// for why a threshold test cannot be preserved by any choice of filter and needs a per-level scale.
    /// </remarks>
    public bool PreserveAlphaCoverage(float cutoff)
        => AlphaCoverage.Preserve(_pixels, Width, Height, MipLevels, cutoff);

    /// <summary>Gets the width of mip level zero in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of mip level zero in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of mip levels.</summary>
    public int MipLevels { get; }

    /// <summary>Gets every level's pixels, back to back, largest level first.</summary>
    public ReadOnlySpan<Bgra32> Pixels => _pixels;

    /// <summary>Gets the size of a mip level.</summary>
    /// <param name="level">The zero-based mip level.</param>
    /// <returns>The width and height of that level.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is negative or at least <see cref="MipLevels"/>.</exception>
    public (int Width, int Height) GetLevelSize(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, MipLevels);
        return (Width >> level, Height >> level);
    }

    /// <summary>Gets one mip level's pixels.</summary>
    /// <param name="level">The zero-based mip level.</param>
    /// <returns>That level's pixels, row-major.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is negative or at least <see cref="MipLevels"/>.</exception>
    public ReadOnlySpan<Bgra32> GetLevel(int level)
    {
        (int w, int h) = GetLevelSize(level);
        return _pixels.AsSpan(_levelOffsets[level], w * h);
    }

    /// <inheritdoc/>
    public override string ToString() => $"TextureImage {Width}x{Height} mips={MipLevels}";
}
