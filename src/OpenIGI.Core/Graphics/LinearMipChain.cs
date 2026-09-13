namespace OpenIGI.Core.Graphics;

/// <summary>
/// Builds a mip chain for a texture whose channels are numbers rather than colours.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <c>MipChainBuilder</c>, which is the right filter for a picture and the wrong one
/// here. That builder decodes each channel from the display encoding, averages, and re-encodes — correct
/// for a photograph of a wall, and a systematic bias on a map that carries a normal's x and y, a
/// roughness and a material flag. Decoding a normal's x as though it were a brightness and re-encoding
/// the mean pushes the average towards the larger of its inputs, so two opposing slopes no longer cancel
/// and the surface leans a little further with every level.
/// </para>
/// <para>
/// It also drops the alpha weighting, which exists so a cut-out's transparent gaps cannot darken the
/// wire's colour. A detail map's alpha is a material channel and every texel of it counts.
/// </para>
/// <para>
/// <b>Why a chain at all.</b> A surface drawn smaller than its map samples a scattering of unrelated
/// texels, and for a normal map each of those is a different lighting direction — which reads as
/// sparkle, not as detail. Without a chain the sampler also falls back to nearest mip selection and
/// loses anisotropy entirely, because both are choices between levels and there is only one.
/// </para>
/// </remarks>
public static class LinearMipChain
{
    /// <summary>Gets how many levels a texture of this size has, down to a single texel.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The number of levels in a complete chain.</returns>
    public static int LevelCount(int width, int height)
    {
        int levels = 1;
        for (int size = Math.Max(width, height); size > 1; size >>= 1)
        {
            levels++;
        }

        return levels;
    }

    /// <summary>Builds the whole chain from level zero, averaging every channel as it stands.</summary>
    /// <param name="levelZero">The full-resolution pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>Level zero followed by every level below it, and how many there are.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="levelZero"/> is too small.</exception>
    public static (Bgra32[] Pixels, int Levels) Build(ReadOnlySpan<Bgra32> levelZero, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (width > int.MaxValue / height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The dimensions describe more pixels than this process can address.");
        }

        int pixelCount = width * height;

        if (levelZero.Length < pixelCount)
        {
            throw new ArgumentException(
                $"A {width}x{height} texture needs {pixelCount} pixels, got {levelZero.Length}.",
                nameof(levelZero));
        }

        int levels = LevelCount(width, height);

        int total = 0;
        int[] offsets = new int[levels];
        for (int level = 0; level < levels; level++)
        {
            offsets[level] = total;
            total += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        Bgra32[] pixels = new Bgra32[total];
        levelZero[..pixelCount].CopyTo(pixels);

        for (int level = 1; level < levels; level++)
        {
            int sourceWidth = Math.Max(1, width >> (level - 1));
            int sourceHeight = Math.Max(1, height >> (level - 1));
            int targetWidth = Math.Max(1, width >> level);
            int targetHeight = Math.Max(1, height >> level);

            Halve(
                pixels.AsSpan(offsets[level - 1], sourceWidth * sourceHeight),
                sourceWidth,
                sourceHeight,
                pixels.AsSpan(offsets[level], targetWidth * targetHeight),
                targetWidth,
                targetHeight);
        }

        return (pixels, levels);
    }

    private static void Halve(
        ReadOnlySpan<Bgra32> source,
        int sourceWidth,
        int sourceHeight,
        Span<Bgra32> target,
        int targetWidth,
        int targetHeight)
    {
        for (int y = 0; y < targetHeight; y++)
        {
            // A level whose source is one texel tall on an axis samples that row twice rather than
            // stepping off the end of it, which is what keeps a long thin texture from tearing as its
            // chain runs out on the short axis first.
            int y0 = Math.Min((y * 2) + 0, sourceHeight - 1);
            int y1 = Math.Min((y * 2) + 1, sourceHeight - 1);

            for (int x = 0; x < targetWidth; x++)
            {
                int x0 = Math.Min((x * 2) + 0, sourceWidth - 1);
                int x1 = Math.Min((x * 2) + 1, sourceWidth - 1);

                Bgra32 a = source[(y0 * sourceWidth) + x0];
                Bgra32 b = source[(y0 * sourceWidth) + x1];
                Bgra32 c = source[(y1 * sourceWidth) + x0];
                Bgra32 d = source[(y1 * sourceWidth) + x1];

                // Plus two, so the division rounds to nearest rather than always down. Half a code value
                // per level sounds like nothing; on the channel holding a normal's x it is a surface
                // leaning steadily one way as it recedes, which is exactly the artefact a chain is here
                // to remove.
                //
                // Positionally, and the order is blue, green, red, alpha: Bgra32's constructor takes its
                // channels in MEMORY order. Writing this in the order a sampler names them is what put
                // the roughness where the normal's x belonged once already.
                target[(y * targetWidth) + x] = new Bgra32(
                    (byte)((a.B + b.B + c.B + d.B + 2) / 4),
                    (byte)((a.G + b.G + c.G + d.G + 2) / 4),
                    (byte)((a.R + b.R + c.R + d.R + 2) / 4),
                    (byte)((a.A + b.A + c.A + d.A + 2) / 4));
            }
        }
    }
}
