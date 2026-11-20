using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// The drawings a bomb's sprites are made of, built from noise at first use: four billows of smoke, four tongues of fire and
/// one flash, each on a texture of its own, two by two where there are four.
/// </summary>
/// <remarks>
/// <para>
/// A billow is a ball of folded noise: dense through its middle, its rim broken into lumps rather than a soft disc, so a pile
/// of them reads as one cloud and not as a heap of circles. Its colour carries the noise too, a little, so the folds show
/// once the vertex colour has lit it; its alpha carries the shape.
/// </para>
/// <para>
/// A tongue of fire is the same noise, sharper and hollower, and the flash a smooth glow; both white, their shape in alpha
/// alone. The world's forward pass decodes a draw's colour from the display's encoding before it adds it, which would bend a
/// shape held in colour into a much narrower one and lose every soft edge; alpha it takes as it is, so an added draw --
/// colour times alpha -- keeps the shape exactly.
/// </para>
/// </remarks>
internal static class BlastSpriteTextures
{
    /// <summary>The edge of one drawing, in texels.</summary>
    internal const int CellSize = 128;

    /// <summary>Builds the four billows of smoke.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The texture, two by two.</returns>
    internal static TextureHandle CreateSmoke(IGraphicsDevice device) => BuildAtlas(device, static (dx, dy, distance, seed) =>
    {
        float fold = Fbm((dx * 2.3f) + seed, (dy * 2.3f) - seed, 5);
        float edge = 1f - distance;
        float shape = Smooth(-0.05f, 0.55f, edge + ((fold - 0.5f) * 0.75f));
        float shade = 0.72f + (0.28f * Fbm((dx * 4.1f) - seed, (dy * 4.1f) + (seed * 0.5f), 3));
        byte value = Byte(shade);
        return new Bgra32(value, value, value, Byte(shape * shape * (3f - (2f * shape))));
    });

    /// <summary>Builds the four tongues of fire.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The texture, two by two.</returns>
    internal static TextureHandle CreateFire(IGraphicsDevice device) => BuildAtlas(device, static (dx, dy, distance, seed) =>
    {
        float fold = Fbm((dx * 3.1f) + seed, (dy * 3.1f) + (seed * 1.7f), 5);
        float edge = 1f - distance;
        float shape = Smooth(0.05f, 0.7f, edge + ((fold - 0.5f) * 1.1f));
        shape *= 0.55f + (0.45f * fold);
        return new Bgra32(255, 255, 255, Byte(Math.Clamp(shape, 0f, 1f)));
    });

    /// <summary>Builds the flash's glow.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The texture.</returns>
    internal static TextureHandle CreateFlare(IGraphicsDevice device)
    {
        const int size = CellSize;
        var pixels = new Bgra32[size * size];
        float centre = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));

                // A hot core and a long soft skirt, nought at the rim.
                float core = MathF.Exp(-distance * distance * 18f);
                float skirt = MathF.Pow(Math.Clamp(1f - distance, 0f, 1f), 1.8f) * 0.8f;
                pixels[(y * size) + x] = new Bgra32(255, 255, 255, Byte(Math.Clamp(core + skirt, 0f, 1f)));
            }
        }

        return device.CreateTexture(new TextureDescription(size, size, MipLevels: 1, TextureFilter.Linear, TextureAddressMode.Clamp), pixels);
    }

    // Four drawings, two by two, each from its own seed; dx and dy run -1..1 across a drawing.
    private static TextureHandle BuildAtlas(IGraphicsDevice device, Func<float, float, float, float, Bgra32> texel)
    {
        const int cell = CellSize;
        const int size = cell * 2;
        var pixels = new Bgra32[size * size];
        float centre = (cell - 1) * 0.5f;
        for (int variant = 0; variant < 4; variant++)
        {
            int ox = (variant & 1) * cell;
            int oy = (variant >> 1) * cell;
            float seed = 17.31f * (variant + 1);
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    float dx = (x - centre) / centre;
                    float dy = (y - centre) / centre;
                    float distance = MathF.Sqrt((dx * dx) + (dy * dy));

                    // The outermost texel ring is left empty, so a cell never bleeds into its neighbour when filtered.
                    bool rim = x == 0 || y == 0 || x == cell - 1 || y == cell - 1;
                    pixels[((oy + y) * size) + ox + x] = rim ? default : texel(dx, dy, distance, seed);
                }
            }
        }

        return device.CreateTexture(new TextureDescription(size, size, MipLevels: 1, TextureFilter.Linear, TextureAddressMode.Clamp), pixels);
    }

    // Fractal value noise, nought to one.
    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f;
        float amplitude = 0.5f;
        float total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += ValueNoise(x, y) * amplitude;
            total += amplitude;
            x = (x * 2.03f) + 11.7f;
            y = (y * 2.03f) - 5.3f;
            amplitude *= 0.5f;
        }

        return sum / total;
    }

    private static float ValueNoise(float x, float y)
    {
        int ix = (int)MathF.Floor(x);
        int iy = (int)MathF.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float ux = fx * fx * (3f - (2f * fx));
        float uy = fy * fy * (3f - (2f * fy));
        float a = Hash(ix, iy);
        float b = Hash(ix + 1, iy);
        float c = Hash(ix, iy + 1);
        float d = Hash(ix + 1, iy + 1);
        return a + ((b - a) * ux) + ((c - a) * uy) + ((a - b - c + d) * ux * uy);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
