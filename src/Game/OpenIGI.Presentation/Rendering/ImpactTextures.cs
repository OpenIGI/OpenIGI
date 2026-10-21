using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The procedural sprites the impact particles draw with: spark glow, dust blob and glass shard.</summary>
/// <remarks>Each is built once on first use by <see cref="ImpactParticleRenderer"/>, which owns and destroys the handle.</remarks>
internal static class ImpactTextures
{
    private const int TextureSize = ImpactParticleRenderer.TextureSize;

    // The spark sprite: a hot radial glow. Profile split as its square root across colour and alpha
    // so the additive multiply restores it linearly and the rim's ALPHA is zero -- the shaped-alpha
    // rule the tracer's black-square hunt established.
    internal static TextureHandle CreateSpark(IGraphicsDevice device) =>
        BuildTexture(device, (dx, dy, distance) =>
        {
            float glow = Math.Clamp(1f - distance, 0f, 1f);
            glow = MathF.Pow(glow, 1.4f);
            byte value = (byte)(MathF.Sqrt(glow) * 255f);
            return new Bgra32(value, value, value, value);
        });

    // The dust sprite: a soft blob with a little procedural mottle, alpha-shaped, colour solid so
    // the vertex tint decides the hue and the texture only the shape.
    internal static TextureHandle CreateDust(IGraphicsDevice device) =>
        BuildTexture(device, (dx, dy, distance) =>
        {
            float falloff = Math.Clamp(1f - distance, 0f, 1f);
            falloff *= falloff;

            // A cheap deterministic mottle so a plume is a cloud rather than a disc.
            float noise = 0.75f + (0.25f * MathF.Sin((dx * 12.9898f) + (dy * 78.233f))
                * MathF.Sin((dx * 39.4227f) - (dy * 27.1717f)));

            byte alpha = (byte)(Math.Clamp(falloff * noise, 0f, 1f) * 255f);
            return new Bgra32(255, 255, 255, alpha);
        });

    // The glass sprite: the shard's alpha mask.
    internal static TextureHandle CreateGlass(IGraphicsDevice device) =>
        BuildTexture(device, (dx, dy, _) =>
        {
            // A triangular alpha mask reads as a shard even though the batch remains a quad mesh.
            // The broad translucent edge is the retail pane's light-catching fragment, not a round
            // smoke billboard accidentally tinted blue.
            float x = (dx + 1f) * 0.5f;
            float y = (dy + 1f) * 0.5f;
            float left = 0.08f + (0.84f * y);
            float right = 0.92f - (0.84f * y);
            float edge = MathF.Min(x - left, right - x);
            float alpha = Math.Clamp(edge * 80f, 0f, 1f) * Math.Clamp(1f - (y * 0.35f), 0f, 1f);
            return new Bgra32(255, 255, 255, (byte)(alpha * 220f));
        });

    private static TextureHandle BuildTexture(IGraphicsDevice device, Func<float, float, float, Bgra32> texel)
    {
        Bgra32[] pixels = new Bgra32[TextureSize * TextureSize];
        float centre = (TextureSize - 1) * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                pixels[(y * TextureSize) + x] = texel(dx, dy, distance);
            }
        }

        return device.CreateTexture(
            new TextureDescription(
                TextureSize,
                TextureSize,
                MipLevels: 1,
                TextureFilter.Linear,
                TextureAddressMode.Clamp),
            pixels);
    }
}
