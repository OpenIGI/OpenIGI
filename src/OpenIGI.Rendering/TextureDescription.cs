namespace OpenIGI.Rendering;

/// <summary>
/// Everything a device needs to allocate a texture: its size, its mip count and how it is sampled.
/// </summary>
/// <param name="Width">The width of mip level 0 in pixels.</param>
/// <param name="Height">The height of mip level 0 in pixels.</param>
/// <param name="MipLevels">The number of mip levels, at least one.</param>
/// <param name="Filter">How the texture is sampled between texels.</param>
/// <param name="AddressMode">How coordinates outside the unit square are resolved.</param>
/// <remarks>
/// Pixels always arrive as <see cref="OpenIGI.Core.Graphics.Bgra32"/>: the file's own format code is
/// decoded once at load time, so the device never sees a palette or a packed 16-bit pixel. See
/// the original.
/// </remarks>
public readonly record struct TextureDescription(
    int Width,
    int Height,
    int MipLevels,
    TextureFilter Filter,
    TextureAddressMode AddressMode)
{
    /// <summary>Initializes a new instance of the <see cref="TextureDescription"/> struct with one mip level and the defaults used by 2D drawing.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public TextureDescription(int width, int height)
        : this(width, height, 1, TextureFilter.Linear, TextureAddressMode.Clamp)
    {
    }

    /// <summary>Gets the number of pixels in mip level 0.</summary>
    public int PixelCount => Width * Height;

    /// <summary>Gets the size of the mip level at <paramref name="level"/>.</summary>
    /// <param name="level">The zero-based mip level.</param>
    /// <returns>The width and height of that level.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is negative or at least <see cref="MipLevels"/>.</exception>
    /// <remarks>
    /// Both axes halve together and neither is clamped: the original's chain stops as soon as
    /// <em>either</em> axis would reach zero, so a 128×64 texture has seven levels ending at 2×1, not
    /// eight ending at 1×1. Backends must cap their maximum level accordingly rather than assume a
    /// chain that runs to 1×1.
    /// </remarks>
    public (int Width, int Height) GetMipSize(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, MipLevels);
        return (Width >> level, Height >> level);
    }
}
