using OpenIGI.Core.Graphics;

namespace OpenIGI.Benchmarks;

/// <summary>Synthetic diffuse textures shaped like the game's own art.</summary>
/// <remarks>
/// <para>
/// The retail textures cannot live in this repository, so the input is generated instead — but it is
/// generated to have the properties the code under test reacts to, because a benchmark over a flat
/// colour or over white noise measures the wrong thing. Real IGI art is a photograph resampled to
/// somewhere between 8 and 128 pixels, so it carries broad shape (the bricks), mid detail (the mortar)
/// and per-texel grain (the sensor and the quantiser) all at once, and the derivation's cost is spread
/// across all three: the frequency split reacts to the first, the occlusion sweep to the second and the
/// denoise pass to the third.
/// </para>
/// <para>
/// Deterministic, and tiling in both axes, so the wrap paths are exercised the same way on every run.
/// </para>
/// </remarks>
internal static class TextureFixtures
{
    /// <summary>Builds a tiling pseudo-photographic diffuse texture.</summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <param name="opaque">Whether every texel is opaque; false punches a cut-out shape into the alpha.</param>
    /// <returns>The pixels, row-major.</returns>
    public static Bgra32[] Diffuse(int size, bool opaque = true)
    {
        Bgra32[] pixels = new Bgra32[size * size];
        uint state = 0x9E3779B9u;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Three octaves of a tiling sinusoid stand in for the broad shape and the mid detail.
                // Sinusoids rather than value noise because they tile exactly at any size, which keeps
                // the fixture honest about the wrap paths at every size benchmarked.
                float u = MathF.Tau * x / size;
                float v = MathF.Tau * y / size;

                float broad = 0.45f + (0.18f * MathF.Sin(u)) + (0.12f * MathF.Cos(v));
                float mortar = 0.10f * MathF.Sin(4f * u) * MathF.Cos(4f * v);
                float grain = 0.06f * MathF.Sin(11f * u + 3f) * MathF.Sin(13f * v + 1f);

                // Plus per-texel noise, which is the part the denoise pass exists for.
                state = (state * 1664525u) + 1013904223u;
                float speckle = (((state >> 8) & 0xFFFF) / 65535f - 0.5f) * 0.05f;

                float level = Math.Clamp(broad + mortar + grain + speckle, 0f, 1f);

                byte red = Quantise(level);
                byte green = Quantise(level * 0.92f);
                byte blue = Quantise(level * 0.80f);
                byte alpha = opaque || ((x + y) % 5) != 0 ? (byte)255 : (byte)0;

                pixels[(y * size) + x] = new Bgra32(blue, green, red, alpha);
            }
        }

        return pixels;
    }

    private static byte Quantise(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
