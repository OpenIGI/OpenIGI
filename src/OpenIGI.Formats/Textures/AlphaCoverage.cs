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

    // Where the coverage taper begins and ends, in texels of the level. Full coverage is held while a
    // level is at least this large -- it still holds the pattern, and this is where the medium-distance
    // "the fence must not thin too fast" fix lives. Below it, a level is too few texels to represent a
    // fraction of coverage at all, so forcing it to keep the pattern's coverage renders the whole quad
    // solid in the averaged wire colour -- a grey panel where an extreme-distance fence should be fading
    // to nothing. Across the smallest levels the target is tapered to zero so it fades instead.
    private const int CoverageFullTexels = 64; // 8x8 and larger: coverage preserved in full.
    private const int CoverageGoneTexels = 1;  // 1x1: faded out entirely.

    // What counts as "a hole" and "solid" when deciding whether a texture is a stencil or a transparency.
    // Deliberately generous at both ends: a converted texture's holes are rarely exactly zero.
    private const byte CutoutLowAlpha = 8;
    private const byte CutoutHighAlpha = 248;

    // How much of a texture may sit between those before it is treated as a real transparency rather than
    // a cut-out. A stencil has partial alpha only along the edge of its shapes, which for even a fine
    // chain-link is a small share of the whole.
    private const float MaxPartialFractionForCutout = 0.25f;

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

            // Full coverage while the level is large enough to hold the pattern; tapered toward zero across
            // the smallest levels so an extreme-distance fence fades to nothing rather than being forced
            // solid in its averaged wire colour (a grey panel). See CoverageFullTexels.
            float fade = Math.Clamp(
                (count - CoverageGoneTexels) / (float)(CoverageFullTexels - CoverageGoneTexels), 0f, 1f);
            float levelTarget = target * fade;

            float scale = FindScale(data, threshold, levelTarget);
            if (scale is > 1.0001f or < 0.9999f)
            {
                ApplyScale(data, scale);
                rescaled = true;
            }
        }

        return rescaled;
    }

    /// <summary>
    /// Widens a cut-out texture's alpha so its partly-covered edge texels survive the shader's test.
    /// </summary>
    /// <param name="pixels">The whole mip chain, modified in place.</param>
    /// <param name="cutoff">The alpha the shader tests against, as a 0..1 fraction.</param>
    /// <returns><see langword="true"/> when the texture was a cut-out and was widened.</returns>
    /// <remarks>
    /// <para>
    /// The reference clips its masked surfaces at essentially zero — every texel carrying any alpha at
    /// all is drawn. Ours clips at a half, which throws away the partly-covered texels along the edge of
    /// every wire, slat and leaf. The difference is visible: the reference's barbed wire is perceptibly
    /// thicker than ours against the same sky, and its chain-link holds together at distances where ours
    /// has thinned to a haze.
    /// </para>
    /// <para>
    /// <b>Why this is not simply a lower cutoff.</b> Lowering it for everything is catastrophic, and was
    /// measured to be: the game's window glass carries partial alpha as a genuine transparency, so a low
    /// cutoff turns a pane opaque and the office window falls from 156 to 38 out of 255. The reference
    /// does not have that problem because its own glass alpha is binary. So the two cases have to be told
    /// apart, and the texture itself is what tells them apart: a cut-out's alpha is nearly all zero or
    /// nearly all full, with only edge texels in between, while glass lives in the middle band. Only the
    /// first kind is widened, and it is widened at load rather than in the shader — which needs no
    /// per-draw state, no seam change, and cannot affect a surface that was not classified.
    /// </para>
    /// </remarks>
    public static bool WidenCutout(Span<Bgra32> pixels, float cutoff)
    {
        if (pixels.Length == 0)
        {
            return false;
        }

        byte threshold = (byte)Math.Clamp((int)MathF.Round(cutoff * 255f), 1, 255);

        int transparent = 0;
        int opaque = 0;
        int partial = 0;
        foreach (Bgra32 pixel in pixels)
        {
            if (pixel.A <= CutoutLowAlpha)
            {
                transparent++;
            }
            else if (pixel.A >= CutoutHighAlpha)
            {
                opaque++;
            }
            else
            {
                partial++;
            }
        }

        // Nothing is cut out of it, so there is no edge to widen.
        if (transparent == 0)
        {
            return false;
        }

        // Glass, smoke, a decal fading out: the alpha is being used as a continuous transparency rather
        // than as a stencil, and widening it would turn a see-through surface solid.
        if (partial > (pixels.Length * MaxPartialFractionForCutout))
        {
            return false;
        }

        // Everything the reference would have drawn is drawn. A texel carrying any meaningful alpha is
        // lifted to the cutoff so the shader keeps it; one carrying none is left alone so the holes stay
        // holes. The colour channels are untouched -- this decides coverage, not shade.
        bool widened = false;
        for (int i = 0; i < pixels.Length; i++)
        {
            Bgra32 pixel = pixels[i];
            if (pixel.A > CutoutLowAlpha && pixel.A < threshold)
            {
                pixels[i] = new Bgra32(pixel.B, pixel.G, pixel.R, threshold);
                widened = true;
            }
        }

        return widened;
    }

    /// <summary>
    /// Whether a texture is an alpha-tested cut-out — holes plus solid, an edge between — rather than an
    /// opaque surface or a continuous transparency.
    /// </summary>
    /// <param name="pixels">The texels to classify; level zero is enough.</param>
    /// <returns><see langword="true"/> for a cut-out (fence, foliage, railing, grille).</returns>
    /// <remarks>
    /// The same test <see cref="WidenCutout"/> makes, exposed so a caller can decide whether a texture
    /// needs its mip chain rebuilt with alpha-weighted colour — a cut-out that shipped with black-in-the-
    /// gap mips goes dark at distance otherwise, and rebuilding from level zero is the only way to undo it.
    /// </remarks>
    public static bool IsCutout(ReadOnlySpan<Bgra32> pixels)
    {
        if (pixels.Length == 0)
        {
            return false;
        }

        int transparent = 0;
        int partial = 0;
        foreach (Bgra32 pixel in pixels)
        {
            if (pixel.A <= CutoutLowAlpha)
            {
                transparent++;
            }
            else if (pixel.A < CutoutHighAlpha)
            {
                partial++;
            }
        }

        // Some texels have to be holes, and the in-between band has to be a small share of the whole -- a
        // continuous transparency (glass, smoke) lives in that band and is not a cut-out.
        return transparent > 0 && partial <= (pixels.Length * MaxPartialFractionForCutout);
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
