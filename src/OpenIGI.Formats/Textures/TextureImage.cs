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
