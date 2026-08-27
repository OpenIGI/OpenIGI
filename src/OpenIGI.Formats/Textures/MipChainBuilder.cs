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
/// Coverage for alpha-tested surfaces is restored separately, by <see cref="AlphaCoverage"/>, once the
/// chain exists — averaging is what costs the coverage, so it cannot be fixed while building.
/// </para>
/// </remarks>
public static class MipChainBuilder
{
    // Decoding through a table rather than a pow per channel per texel: a full chain over a level's worth
    // of pixels is a few million of them, and the input is a byte, so there are only 256 answers.
    private static readonly float[] SrgbToLinear = BuildSrgbToLinear();

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
        int levels = LevelCount(width, height);

        int total = 0;
        int[] offsets = new int[levels];
        for (int level = 0; level < levels; level++)
        {
            offsets[level] = total;
            total += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        Bgra32[] pixels = new Bgra32[total];
        levelZero[..(width * height)].CopyTo(pixels);

        for (int level = 1; level < levels; level++)
        {
            int sourceWidth = Math.Max(1, width >> (level - 1));
            int sourceHeight = Math.Max(1, height >> (level - 1));
            int targetWidth = Math.Max(1, width >> level);
            int targetHeight = Math.Max(1, height >> level);

            ReadOnlySpan<Bgra32> source = pixels.AsSpan(offsets[level - 1], sourceWidth * sourceHeight);
            Span<Bgra32> target = pixels.AsSpan(offsets[level], targetWidth * targetHeight);

            Halve(source, sourceWidth, sourceHeight, target, targetWidth, targetHeight);
        }

        return (pixels, offsets);
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

                float blue = (SrgbToLinear[a.B] + SrgbToLinear[b.B] + SrgbToLinear[c.B] + SrgbToLinear[d.B]) * 0.25f;
                float green = (SrgbToLinear[a.G] + SrgbToLinear[b.G] + SrgbToLinear[c.G] + SrgbToLinear[d.G]) * 0.25f;
                float red = (SrgbToLinear[a.R] + SrgbToLinear[b.R] + SrgbToLinear[c.R] + SrgbToLinear[d.R]) * 0.25f;
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

    private static byte LinearToSrgbByte(float linear)
    {
        float encoded = linear <= 0.0031308f
            ? linear * 12.92f
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }
}
