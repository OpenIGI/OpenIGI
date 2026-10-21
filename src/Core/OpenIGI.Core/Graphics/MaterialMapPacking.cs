namespace OpenIGI.Core.Graphics;

/// <summary>Packs <see cref="MaterialMapGenerator"/>'s maps into textures, for the renderer and for inspection.</summary>
/// <remarks>
/// The arrangement the G-buffer samples, the ORM layout every other tool expects, and plain images of
/// one map at a time. Reached through <see cref="MaterialMapGenerator"/>'s own methods of the same names,
/// which carry the documentation.
/// </remarks>
internal static class MaterialMapPacking
{
    /// <inheritdoc cref="MaterialMapGenerator.PackDetail"/>
    public static void PackDetail(MaterialMapGenerator.Maps maps, Span<Bgra32> destination)
    {
        ArgumentNullException.ThrowIfNull(maps);

        int count = maps.Width * maps.Height;

        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"A {maps.Width}x{maps.Height} map needs {count} pixels, got {destination.Length}.",
                nameof(destination));
        }

        for (int i = 0; i < count; i++)
        {
            // Named arguments, and not for tidiness. Bgra32's constructor takes its channels in MEMORY
            // order, so its first positional parameter is blue — while a sampler, and every other
            // Pack method here, calls the first channel red. Writing this positionally is what put the
            // roughness where the normal's x belonged, and the result still looked like a plausible
            // picture, which is the worst way for it to be wrong.
            destination[i] = new Bgra32(
                r: Quantise((maps.NormalX[i] * 0.5f) + 0.5f),
                g: Quantise((maps.NormalY[i] * 0.5f) + 0.5f),
                b: Quantise(maps.Roughness[i] * 0.5f),
                a: Quantise(maps.Occlusion[i]));
        }
    }

    /// <inheritdoc cref="MaterialMapGenerator.PackOcclusionRoughnessMetallic"/>
    public static void PackOcclusionRoughnessMetallic(
        MaterialMapGenerator.Maps maps, Span<Bgra32> destination, float metallic = 0f)
    {
        ArgumentNullException.ThrowIfNull(maps);

        int count = maps.Width * maps.Height;

        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"A {maps.Width}x{maps.Height} map needs {count} pixels, got {destination.Length}.",
                nameof(destination));
        }

        byte packedMetallic = Quantise(Math.Clamp(metallic, 0f, 1f));

        for (int i = 0; i < count; i++)
        {
            destination[i] = new Bgra32(
                r: Quantise(maps.Occlusion[i]),
                g: Quantise(maps.Roughness[i] * 0.5f),
                b: packedMetallic,
                a: 255);
        }
    }

    /// <inheritdoc cref="MaterialMapGenerator.PackGreyscale"/>
    public static void PackGreyscale(ReadOnlySpan<float> values, Span<Bgra32> destination)
    {
        if (destination.Length < values.Length)
        {
            throw new ArgumentException(
                $"A {values.Length}-value map needs as many pixels, got {destination.Length}.",
                nameof(destination));
        }

        for (int i = 0; i < values.Length; i++)
        {
            byte level = Quantise(values[i]);
            destination[i] = new Bgra32(level, level, level, 255);
        }
    }

    /// <inheritdoc cref="MaterialMapGenerator.PackNormal"/>
    public static void PackNormal(MaterialMapGenerator.Maps maps, Span<Bgra32> destination)
    {
        ArgumentNullException.ThrowIfNull(maps);

        int count = maps.Width * maps.Height;

        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"A {maps.Width}x{maps.Height} map needs {count} pixels, got {destination.Length}.",
                nameof(destination));
        }

        for (int i = 0; i < count; i++)
        {
            float x = maps.NormalX[i];
            float y = maps.NormalY[i];

            // Rebuilt rather than stored: a unit vector's third component is not independent
            // information. The max guards the case where the first two already over-run the unit
            // sphere, which quantisation can produce at full strength.
            float z = MathF.Sqrt(Math.Max(1f - (x * x) - (y * y), 0f));

            destination[i] = new Bgra32(
                r: Quantise((x * 0.5f) + 0.5f),
                g: Quantise((y * 0.5f) + 0.5f),
                b: Quantise((z * 0.5f) + 0.5f),
                a: 255);
        }
    }

    /// <summary>Quantises a unit value to a byte channel, rounding and clamping.</summary>
    internal static byte Quantise(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
