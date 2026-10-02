using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// Builds the mip chain for a texture that shipped without one.
/// </summary>
/// <remarks>
/// <para>
/// Most of this game's textures carry a single level. That was reasonable for the hardware it shipped
/// on and is not reasonable now: a surface drawn smaller than its texture samples a scattering of
/// unrelated texels, so it sparkles as the camera moves and every thin thing on it — a fence, a railing,
/// a ladder — crawls. No amount of anisotropy fixes that; anisotropy chooses how many samples to take
/// ALONG the footprint, and with one level there is nothing but the full-resolution texture to take them
/// from. It is the missing levels that do the averaging.
/// </para>
/// <para>
/// <b>Filtered in linear light.</b> These textures are display-encoded, and averaging display-encoded
/// values is averaging the wrong numbers — the mean of two encoded values is not the encoding of their
/// mean. Doing it directly darkens every level a little more than the last, so a wall visibly changes
/// tone with distance. Each level is therefore decoded, averaged, and re-encoded. Alpha is already
/// linear and is averaged as it stands.
/// </para>
/// <para>
/// <b>Colour is averaged weighted by alpha.</b> A cut-out stores black or junk RGB wherever its alpha is
/// zero, because nothing samples the colour of a hole — so a flat average lets those black gaps darken the
/// wire's colour a level at a time, and the wire ends up passing the alpha test (coverage is restored) yet
/// drawn black. Weighting each texel's colour by its own alpha keeps a transparent texel out of the colour
/// sum entirely. An opaque texture, every alpha equal, is unaffected.
/// </para>
/// <para>
/// Coverage for alpha-tested surfaces is restored separately, by <see cref="AlphaCoverage"/>, once the
/// chain exists — averaging is what costs the coverage, so it cannot be fixed while building.
/// </para>
/// </remarks>
public static class MipChainBuilder
{
    // Decoding through a table rather than a pow per channel per texel: a full chain over a level's worth
    // of pixels is a few million of them, and the input is a byte, so there are only 256 answers.
    private static readonly float[] SrgbToLinear = BuildSrgbToLinear();

    // The way back, without a pow per channel per texel -- which was most of what building a chain cost,
    // three pows a texel. EncodeThresholds[k] is the smallest linear value that encodes to byte k or above,
    // found by bisecting the very function the pow path computes, so a lookup returns the byte that path
    // would have returned, bit for bit. The coarse table only picks where to start looking.
    private const int CoarseSteps = 4096;
    private static readonly float[] EncodeThresholds = BuildEncodeThresholds();
    private static readonly byte[] CoarseEncode = BuildCoarseEncode();

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

    /// <summary>Builds every level below the one given, largest level first.</summary>
    /// <param name="levelZero">The full-resolution pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The whole chain including level zero, and the offset of each level.</returns>
    public static (Bgra32[] Pixels, int[] Offsets) Build(ReadOnlySpan<Bgra32> levelZero, int width, int height)
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

        Bgra32[] pixels = new Bgra32[ChainLength(width, height)];
        levelZero[..pixelCount].CopyTo(pixels);
        return (pixels, BuildInPlace(pixels, width, height));
    }

    /// <summary>Gets how many pixels a complete chain for this size holds, every level included.</summary>
    /// <param name="width">The width of level zero in pixels.</param>
    /// <param name="height">The height of level zero in pixels.</param>
    /// <returns>The pixel count of the whole chain.</returns>
    public static int ChainLength(int width, int height)
    {
        int total = 0;
        int levels = LevelCount(width, height);
        for (int level = 0; level < levels; level++)
        {
            total += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        return total;
    }

    /// <summary>Builds every level below level zero in place, in a buffer that already starts with level zero.</summary>
    /// <param name="chain">At least <see cref="ChainLength"/> pixels, level zero first.</param>
    /// <param name="width">The width of level zero in pixels.</param>
    /// <param name="height">The height of level zero in pixels.</param>
    /// <returns>The offset of each level.</returns>
    /// <remarks>
    /// The same chain <see cref="Build"/> returns, without the copy of level zero it makes: a texture
    /// decoded straight into a chain-sized buffer has its chain built where it lies.
    /// </remarks>
    public static int[] BuildInPlace(Span<Bgra32> chain, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int levels = LevelCount(width, height);

        int total = 0;
        int[] offsets = new int[levels];
        for (int level = 0; level < levels; level++)
        {
            offsets[level] = total;
            total += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        if (chain.Length < total)
        {
            throw new ArgumentException(
                $"A {width}x{height} chain needs {total} pixels, the buffer holds {chain.Length}.", nameof(chain));
        }

        Span<Bgra32> pixels = chain;

        for (int level = 1; level < levels; level++)
        {
            int sourceWidth = Math.Max(1, width >> (level - 1));
            int sourceHeight = Math.Max(1, height >> (level - 1));
            int targetWidth = Math.Max(1, width >> level);
            int targetHeight = Math.Max(1, height >> level);

            ReadOnlySpan<Bgra32> source = pixels.Slice(offsets[level - 1], sourceWidth * sourceHeight);
            Span<Bgra32> target = pixels.Slice(offsets[level], targetWidth * targetHeight);

            Halve(source, sourceWidth, sourceHeight, target, targetWidth, targetHeight);
        }

        return offsets;
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

                // Colour is averaged WEIGHTED BY ALPHA, which matters for a cut-out. A chain-link texture
                // is a bright wire on fully transparent gaps, and those gaps almost always carry black (or
                // junk) RGB, because nothing was ever meant to sample the colour where the alpha is zero.
                // Average the four colours flat and the wire mixes with three black gaps and darkens toward
                // black a level at a time -- while the coverage restore keeps its alpha high, so the wire
                // still passes the alpha test but is drawn black. Weighting each texel's colour by its own
                // alpha keeps a transparent texel from contributing its colour at all, so the wire keeps
                // the wire's colour down the whole chain. An opaque texture (every alpha equal) is averaged
                // exactly as before; the change only bites where alpha varies, which is precisely the
                // cut-outs that were going black.
                float wa = a.A;
                float wb = b.A;
                float wc = c.A;
                float wd = d.A;
                float wsum = wa + wb + wc + wd;

                float blue;
                float green;
                float red;
                if (wsum > 0f)
                {
                    blue = ((SrgbToLinear[a.B] * wa) + (SrgbToLinear[b.B] * wb) + (SrgbToLinear[c.B] * wc) + (SrgbToLinear[d.B] * wd)) / wsum;
                    green = ((SrgbToLinear[a.G] * wa) + (SrgbToLinear[b.G] * wb) + (SrgbToLinear[c.G] * wc) + (SrgbToLinear[d.G] * wd)) / wsum;
                    red = ((SrgbToLinear[a.R] * wa) + (SrgbToLinear[b.R] * wb) + (SrgbToLinear[c.R] * wc) + (SrgbToLinear[d.R] * wd)) / wsum;
                }
                else
                {
                    // A wholly transparent quad draws nothing, so its colour is never sampled -- keep the
                    // flat average so a genuinely opaque texture that merely happens to be black here, and
                    // any surface that is not alpha tested at all, come out byte-for-byte as they used to.
                    blue = (SrgbToLinear[a.B] + SrgbToLinear[b.B] + SrgbToLinear[c.B] + SrgbToLinear[d.B]) * 0.25f;
                    green = (SrgbToLinear[a.G] + SrgbToLinear[b.G] + SrgbToLinear[c.G] + SrgbToLinear[d.G]) * 0.25f;
                    red = (SrgbToLinear[a.R] + SrgbToLinear[b.R] + SrgbToLinear[c.R] + SrgbToLinear[d.R]) * 0.25f;
                }

                int alpha = (a.A + b.A + c.A + d.A + 2) / 4;

                target[(y * targetWidth) + x] = new Bgra32(
                    LinearToSrgbByte(blue),
                    LinearToSrgbByte(green),
                    LinearToSrgbByte(red),
                    (byte)alpha);
            }
        }
    }

    private static float[] BuildSrgbToLinear()
    {
        float[] table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float encoded = i / 255f;
            table[i] = encoded <= 0.04045f
                ? encoded / 12.92f
                : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
        }

        return table;
    }

    /// <summary>Encodes a linear value to its display byte, through the tables.</summary>
    /// <remarks>Identical to <see cref="EncodeExactly"/> for every input; see <see cref="EncodeThresholds"/>.</remarks>
    internal static byte LinearToSrgbByte(float linear)
    {
        if (!(linear > 0f))
        {
            return EncodeExactly(linear);
        }

        int start = linear >= 1f ? CoarseSteps : (int)(linear * CoarseSteps);
        int k = CoarseEncode[start];

        // A step or two either way at most: the coarse table is only a starting point.
        while (k < 255 && linear >= EncodeThresholds[k + 1])
        {
            k++;
        }

        while (k > 0 && linear < EncodeThresholds[k])
        {
            k--;
        }

        return (byte)k;
    }

    /// <summary>Encodes a linear value to its display byte the slow way, with a pow.</summary>
    internal static byte EncodeExactly(float linear)
    {
        float encoded = linear <= 0.0031308f
            ? linear * 12.92f
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    private static float[] BuildEncodeThresholds()
    {
        // Bisected over the bit patterns of the positive floats, which order the same way as the values,
        // so each threshold is exact rather than merely close. The encoding only ever rises with its input.
        float[] thresholds = new float[256];
        thresholds[0] = float.NegativeInfinity;

        for (int k = 1; k < 256; k++)
        {
            int low = 0;
            int high = BitConverter.SingleToInt32Bits(2f);
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (EncodeExactly(BitConverter.Int32BitsToSingle(middle)) >= k)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            thresholds[k] = BitConverter.Int32BitsToSingle(low);
        }

        return thresholds;
    }

    private static byte[] BuildCoarseEncode()
    {
        byte[] coarse = new byte[CoarseSteps + 1];
        for (int i = 0; i <= CoarseSteps; i++)
        {
            coarse[i] = EncodeExactly(i / (float)CoarseSteps);
        }

        return coarse;
    }
}
