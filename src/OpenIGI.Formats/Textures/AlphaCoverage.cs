using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// Rescales the alpha of a mip chain so that an alpha-tested surface keeps the same amount of itself at
/// every distance — the reference's alpha coverage preservation.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this exists for.</b> A chain-link fence is drawn as a flat quad with a texture whose
/// alpha is the wire. Halving that texture averages neighbouring texels, and averaging a thin bright wire
/// against a wide transparent gap gives something dimmer than the wire was. Do it five or six times and
/// most of the wire's alpha has fallen below whatever cutoff the shader tests against — so the fence
/// thins out with distance, and then flickers, because which texels survive the cutoff changes as the
/// mip level crosses over. The far fence goes faint while the near one stays solid, and neither is what
/// the artist drew.
/// </para>
/// <para>
/// <b>The fix, and why it is a rescale rather than a better filter.</b> No averaging filter can preserve
/// a threshold test, because the test is not linear: what has to be preserved is the FRACTION OF TEXELS
/// THAT PASS IT, and that is a property of the whole level rather than of any neighbourhood. So each
/// level is filtered as usual and then its alpha is scaled by a single number chosen so that the fraction
/// passing the cutoff matches the top level's. The reference does exactly this at import, and the port's
/// own textures carry the setting switched on.
/// </para>
/// <para>
/// <b>Why the scale is searched rather than computed.</b> Coverage as a function of the scale is a step
/// function — it only changes when some texel crosses the cutoff — so it cannot be inverted. It is
/// monotonic, though, which is what makes a bisection both correct and quick.
/// </para>
/// </remarks>
public static class AlphaCoverage
{
    // Enough halvings to land within a fraction of a texel's worth of coverage. Each step halves the
    // bracket, so this resolves the scale to about one part in ten thousand.
    private const int SearchSteps = 14;

    // A wire that has faded to a tenth of its alpha needs a scale of ten to come back. Past that the
    // level has essentially no coverage left to recover and scaling only amplifies filtering noise.
    private const float MaxScale = 16f;

    /// <summary>
    /// Rescales each mip level's alpha so the fraction of texels passing <paramref name="cutoff"/>
    /// matches level zero's.
    /// </summary>
    /// <param name="pixels">The whole mip chain, largest level first, modified in place.</param>
    /// <param name="width">Level zero's width in pixels.</param>
    /// <param name="height">Level zero's height in pixels.</param>
    /// <param name="mipLevels">How many levels the chain holds.</param>
    /// <param name="cutoff">The alpha the shader tests against, as a 0..1 fraction.</param>
    /// <returns><see langword="true"/> when the chain was alpha tested and levels were rescaled.</returns>
    public static bool Preserve(Span<Bgra32> pixels, int width, int height, int mipLevels, float cutoff)
    {
        if (mipLevels < 2 || width <= 0 || height <= 0)
        {
            return false;
        }

        byte threshold = (byte)Math.Clamp((int)MathF.Round(cutoff * 255f), 1, 255);

        int levelZeroCount = width * height;
        if (levelZeroCount > pixels.Length)
        {
            return false;
        }

        Span<Bgra32> levelZero = pixels[..levelZeroCount];
        int passing = CountPassing(levelZero, threshold);

        // An opaque texture, or one that is entirely holes. Neither is alpha tested in any meaningful
        // sense, and scaling their alpha would only corrupt a channel nothing was reading as coverage.
        if (passing == 0 || passing == levelZeroCount)
        {
            return false;
        }

        float target = passing / (float)levelZeroCount;
        int offset = levelZeroCount;
        bool rescaled = false;

        for (int level = 1; level < mipLevels; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            int count = levelWidth * levelHeight;
            if (offset + count > pixels.Length)
            {
                break;
            }

            Span<Bgra32> data = pixels.Slice(offset, count);
            offset += count;

            float scale = FindScale(data, threshold, target);
            if (scale is > 1.0001f or < 0.9999f)
            {
                ApplyScale(data, scale);
                rescaled = true;
            }
        }

        return rescaled;
    }

    private static int CountPassing(ReadOnlySpan<Bgra32> data, byte threshold)
    {
        int passing = 0;
        foreach (Bgra32 pixel in data)
        {
            if (pixel.A >= threshold)
            {
                passing++;
            }
        }

        return passing;
    }

    private static float CoverageAt(ReadOnlySpan<Bgra32> data, byte threshold, float scale)
    {
        int passing = 0;
        foreach (Bgra32 pixel in data)
        {
            if (Math.Min(pixel.A * scale, 255f) >= threshold)
            {
                passing++;
            }
        }

        return passing / (float)data.Length;
    }

    // Coverage rises with the scale and never falls, so the scale that best matches the target is found
    // by bisection. The bracket starts at [0, MaxScale] rather than [1, MaxScale] because a level can
    // also come out with MORE coverage than the top -- a texture whose gaps are only just transparent
    // gains coverage when it is averaged, and it has to be pulled back down.
    private static float FindScale(ReadOnlySpan<Bgra32> data, byte threshold, float target)
    {
        float low = 0f;
        float high = MaxScale;

        for (int i = 0; i < SearchSteps; i++)
        {
            float mid = 0.5f * (low + high);
            if (CoverageAt(data, threshold, mid) < target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return 0.5f * (low + high);
    }

    private static void ApplyScale(Span<Bgra32> data, float scale)
    {
        for (int i = 0; i < data.Length; i++)
        {
            Bgra32 pixel = data[i];
            byte alpha = (byte)Math.Clamp((int)MathF.Round(pixel.A * scale), 0, 255);
            data[i] = new Bgra32(pixel.B, pixel.G, pixel.R, alpha);
        }
    }
}
