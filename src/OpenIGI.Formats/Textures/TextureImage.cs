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

    /// <summary>Wraps already-decoded pixels as a single-level texture.</summary>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="pixels">The image, row-major, <paramref name="width"/> per row.</param>
    /// <returns>The image as a texture with one level, ready to have a chain built onto it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pixels"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too small.</exception>
    /// <remarks>
    /// For pixels that came from somewhere other than a <c>.tex</c> — an upscaled replacement read off
    /// disk, a test fixture — so that everything downstream of the decode is reached by exactly one path.
    /// The correction sequence a texture needs (classify the cut-out, widen its alpha, build the chain,
    /// preserve its coverage) is load-bearing for every fence and canopy in the game, and a second entry
    /// point that skipped it would be a second, subtly different renderer.
    /// </remarks>
    public static TextureImage FromPixels(int width, int height, Bgra32[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int count = width * height;

        if (pixels.Length < count)
        {
            throw new ArgumentException(
                $"A {width}x{height} image needs {count} pixels, got {pixels.Length}.", nameof(pixels));
        }

        return new TextureImage(width, height, 1, pixels, [0]);
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
    /// Returns this texture with a freshly built mip chain, discarding any it shipped with.
    /// </summary>
    /// <returns>A new image whose chain was rebuilt from level zero.</returns>
    /// <remarks>
    /// <see cref="WithMipChain"/> keeps a chain a texture already carries, which is right for an opaque
    /// surface. A cut-out that shipped with a chain, though, carries the black of its transparent gaps in
    /// every level of it — the wire's colour was averaged flat against the holes at import — so it goes
    /// dark at distance no matter how well the alpha is preserved. Rebuilt from level zero, the alpha-
    /// weighted filter in <see cref="MipChainBuilder"/> keeps the wire's own colour instead. Build reads
    /// only level zero of the source, so a chain that is already present is simply replaced.
    /// </remarks>
    public TextureImage RebuildMipChain()
    {
        if (Width <= 0 || Height <= 0)
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

    /// <summary>
    /// Widens this texture's alpha so its partly-covered edge texels survive the shader's test, if it is
    /// a cut-out rather than a real transparency.
    /// </summary>
    /// <param name="cutoff">The alpha the shader tests against, as a 0..1 fraction.</param>
    /// <returns><see langword="true"/> when this texture was a cut-out and was widened.</returns>
    /// <remarks>
    /// Must run BEFORE the chain is built, so that the widened alpha is what gets filtered down: widening
    /// afterwards would lift each level independently and undo the coverage the filter was preserving.
    /// </remarks>
    public bool WidenCutoutAlpha(float cutoff) => AlphaCoverage.WidenCutout(_pixels, cutoff);

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
