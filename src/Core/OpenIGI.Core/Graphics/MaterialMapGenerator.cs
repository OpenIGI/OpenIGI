using System.Buffers;
using System.Numerics;

namespace OpenIGI.Core.Graphics;

/// <summary>
/// Derives the surface maps a physically-based renderer needs from a diffuse texture that has none.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. ships one texture per surface and nothing else — no normal map, no roughness, no statement
/// anywhere about what a surface is made of. That was not a limitation of the art; it is what a
/// renderer of that generation could consume. A physically-based model has four inputs and the data
/// answers one, so the other three have to be derived.
/// </para>
/// <para>
/// <b>This is invented data, and the code should say so.</b> It is a deliberate divergence and nothing
/// under <c>vanilla</c> reads a byte of it. What it buys is the difference between brick that reads as
/// brick when the sun crosses it and brick that reads as a photograph of brick pinned to a flat card —
/// which is a larger visible gap than any amount of indirect lighting.
/// </para>
/// <para>
/// <b>The pipeline, and why it is not just a Sobel filter.</b> Treating luminance as height directly is
/// the obvious approach and it is wrong in a specific, very visible way: it is right wherever a texture
/// is dark because it is recessed, and wrong wherever a texture is dark because somebody painted it
/// that way. A black stripe painted on flat concrete becomes a trench. So the luminance is
/// <b>frequency-separated</b> first — the broad, smooth part of an image is overwhelmingly its colour,
/// and the fine part is overwhelmingly its relief. Height is taken from the fine part alone, and
/// everything else is derived from that height rather than from the picture:
/// </para>
/// <list type="number">
/// <item><description>Luminance, high-passed against a wide blur, becomes the height field.</description></item>
/// <item><description>Scharr gradients over two octaves of that height become the normal.</description></item>
/// <item><description>A horizon search over the height field becomes ambient occlusion.</description></item>
/// <item><description>Local high-frequency energy, plus curvature, becomes roughness.</description></item>
/// </list>
/// <para>
/// <b>Metallic is not attempted and cannot be.</b> Nothing in a diffuse texture distinguishes painted
/// steel from grey paint — the difference is in how each behaves under light, which is exactly the
/// information the texture does not carry. Guessing it would put a mirror finish on half the level.
/// That channel comes from authored data or it stays at zero.
/// </para>
/// <para>
/// <b>Everything wraps.</b> These textures tile — the ground, every wall, every crate — so the pixel
/// left of column zero is genuinely the last column. Clamping instead flattens all four edges, which
/// shows up as a seam grid over the whole level and is the single failure that makes derived maps look
/// homemade.
/// </para>
/// <para>
/// Pure: no device, no GL, no file system. Pixels in, pixels out, so the part hardest to be confident
/// about can be tested directly.
/// </para>
/// </remarks>
public static class MaterialMapGenerator
{
    /// <summary>How the maps are derived.</summary>
    /// <param name="NormalStrength">
    /// How far the derived normal may tilt. Zero is flat, one is the calibrated amount, above that is
    /// caricature.
    /// </param>
    /// <param name="Relief">
    /// The radius, as a fraction of the texture's smaller side, below which detail is taken to be
    /// relief rather than colour. Larger admits broader shapes as shape; smaller keeps only the finest
    /// grain and leaves everything else flat.
    /// </param>
    /// <param name="DetailBalance">
    /// How the normal is split between its fine and coarse octaves, from 0 (coarse only, smooth and
    /// soft) to 1 (fine only, crisp and noisy).
    /// </param>
    /// <param name="RoughnessRange">
    /// How far either side of the material's own roughness the derived value may move it, as a
    /// fraction: 0.4 lets the map multiply it by anything from 0.6 to 1.4. Deliberately mild, because
    /// this is a texture's busyness standing in for a measurement.
    /// <para>
    /// <b>Either side, and it used to be one.</b> The mapping was <c>1 + t * range</c>, so the channel
    /// could only ever make a surface rougher -- measured over 400 of the derived maps, not one texel
    /// in the corpus asked for a surface smoother than its material said, while 14% sat pinned at the
    /// ceiling. On a base of 0.8 that put every surface in the game between 0.8 and 1.0, which is a
    /// twelve-per-cent spread at the matte end of GGX where the lobe is already a hemisphere -- and is
    /// why turning the roughness channel on changed nothing anyone could see.
    /// </para>
    /// </param>
    /// <param name="OcclusionStrength">How deeply the derived occlusion may darken. Zero disables it.</param>
    /// <param name="OcclusionDirections">
    /// How many directions the horizon search sweeps. Eight is enough at these texture sizes; the cost
    /// is linear and the whole game's art is a few seconds either way.
    /// </param>
    public readonly record struct Options(
        float NormalStrength = 1f,
        float Relief = 0.12f,
        float DetailBalance = 0.6f,
        float RoughnessRange = 0.4f,
        float OcclusionStrength = 1f,
        int OcclusionDirections = 8)
    {
        /// <summary>Gets the options every map in the shipped pack is derived with.</summary>
        /// <remarks>
        /// <b>Constructed with a named argument, and it has to be.</b> A record struct always has an
        /// implicit parameterless constructor, and for `new Options()` that one wins overload
        /// resolution over the primary constructor — so it produces the ZERO instance rather than the
        /// declared defaults, and a zero <see cref="NormalStrength"/> means "derive nothing at all".
        /// That is not hypothetical: it is what the terrain's ground tiles were silently getting, and
        /// they came out perfectly flat while every other surface gained detail. Naming one parameter
        /// is what forces the primary constructor and therefore the rest of the defaults.
        /// </remarks>
        public static Options Default { get; } = new(NormalStrength: 1f);
    }

    /// <summary>The derived maps, each one value per pixel, row-major.</summary>
    /// <param name="Width">The map width in pixels.</param>
    /// <param name="Height">The map height in pixels.</param>
    /// <param name="Elevation">The height field, centred on 0.5.</param>
    /// <param name="NormalX">The tangent-space normal's x, in [-1, 1].</param>
    /// <param name="NormalY">The tangent-space normal's y, in [-1, 1]. Positive is up: the OpenGL convention.</param>
    /// <param name="Roughness">A multiplier around one for the material's own roughness.</param>
    /// <param name="Occlusion">How much of the hemisphere reaches this texel, in [0, 1].</param>
    public sealed record Maps(
        int Width,
        int Height,
        float[] Elevation,
        float[] NormalX,
        float[] NormalY,
        float[] Roughness,
        float[] Occlusion);

    /// <summary>Derives every map from a diffuse texture.</summary>
    /// <param name="pixels">The diffuse texture, row-major, <paramref name="width"/> per row.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="settings">
    /// How to derive them, or <see langword="null"/> for <see cref="Options.Default"/>. Nullable rather
    /// than a defaulted <see cref="Options"/>: for a record struct, an omitted argument is the zero
    /// instance, and a zero <see cref="Options.NormalStrength"/> silently derives nothing.
    /// </param>
    /// <returns>The derived maps.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too small.</exception>
    public static Maps Generate(
        ReadOnlySpan<Bgra32> pixels, int width, int height, Options? settings = null)
    {
        Options options = settings ?? Options.Default;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (width > int.MaxValue / height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The dimensions describe more pixels than this process can address.");
        }

        int count = width * height;

        if (pixels.Length < count)
        {
            throw new ArgumentException(
                $"A {width}x{height} texture needs {count} pixels, got {pixels.Length}.", nameof(pixels));
        }

        // Rented rather than allocated. Every buffer between here and the finished maps is scratch, and
        // a level derives hundreds of textures on a worker pool at once -- at a megabyte of garbage per
        // texture that was enough allocation to put a generation-one collection in the middle of a load.
        // Only the five arrays the caller is handed survive this method.
        float[] luminance = ArrayPool<float>.Shared.Rent(count);
        Span<float> luminanceSpan = luminance.AsSpan(0, count);

        for (int i = 0; i < count; i++)
        {
            Bgra32 pixel = pixels[i];

            // Rec. 601, which is what the eye weights these channels at.
            luminanceSpan[i] = ((0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B)) / 255f;
        }

        // The frequency split. The blur radius is a fraction of the texture rather than a constant:
        // these textures run from 8x8 to 128x128, and a fixed radius would be a gentle high-pass on
        // one and erase the other entirely.
        float relief = Math.Clamp(options.Relief, 0.01f, 1f);
        int reliefRadius = Math.Max((int)MathF.Round(Math.Min(width, height) * relief), 1);

        float[] broad = ArrayPool<float>.Shared.Rent(count);
        ElevationFilters.Blur(luminanceSpan, width, height, reliefRadius, 3, broad.AsSpan(0, count));

        float[] highPass = ArrayPool<float>.Shared.Rent(count);
        Span<float> highPassSpan = highPass.AsSpan(0, count);
        ElevationFilters.SubtractInto(luminanceSpan, broad.AsSpan(0, count), highPassSpan);

        ArrayPool<float>.Shared.Return(broad);
        ArrayPool<float>.Shared.Return(luminance);

        // Normalised to a fixed contrast, and this is what makes every constant downstream mean
        // something. What a high-pass leaves behind depends entirely on the texture: a photograph of
        // brick leaves a wide spread, a flat painted panel leaves almost nothing, and the difference
        // between them is a factor of thirty. Deriving a normal from that directly gives the brick a
        // usable tilt and the panel none at all -- which is how the first run of this came out, with
        // every map an almost uniform flat blue.
        //
        // Scaled by the field's own standard deviation rather than its extremes, because one bright
        // speck would otherwise set the scale for the whole texture and flatten everything else.
        float mean = ElevationFilters.Sum(highPassSpan) / count;
        float deviation = MathF.Sqrt(ElevationFilters.SumOfSquaredDeviations(highPassSpan, mean) / count);

        // One standard deviation becomes a fifth of the range, so an ordinary texel sits well inside it
        // and only genuine outliers clamp. A texture with no relief at all -- a single flat colour --
        // has no deviation to scale by and stays flat, which is the right answer rather than a
        // degenerate one.
        float contrast = deviation > 1e-4f ? 0.2f / deviation : 0f;

        // Centred on a half so that "no relief" is the middle of the range rather than the floor,
        // which is what makes the gradients below signed without a second bias.
        ElevationFilters.Recentre(highPassSpan, mean, contrast);

        // Denoised, and this is not a refinement -- without it the whole thing fails. These textures
        // are PHOTOGRAPHS, resampled down to 128 pixels and quantised, so a large part of what survives
        // a high-pass is sensor grain and compression rather than relief. The contrast normalisation
        // above cannot tell the two apart and amplifies both, which turned the first run of this into
        // per-texel confetti: a normal map that was pure noise at the scale the eye actually reads.
        //
        // ONE pass at one pixel, and both halves of that matter. Grain lives at exactly one texel and
        // the mortar line between two bricks is two or three, so the radius is right -- but the three
        // passes this helper runs by default compound to a Gaussian of about one and a half texels,
        // which on a 64-pixel texture is most of the detail. Denoising that hard is what left the
        // finished maps tilting five degrees where they wanted to tilt twenty, so the whole feature was
        // technically working and visually absent.
        float[] elevation = new float[count];
        ElevationFilters.Blur(highPassSpan, width, height, 1, 1, elevation);
        ArrayPool<float>.Shared.Return(highPass);

        // Cut-outs get no relief where there is no surface. In this game the alpha textures are the
        // foliage, the wire fences and the glass, and the shape painted into their alpha is a hole
        // rather than a feature -- deriving a bump map across it puts relief on air.
        for (int i = 0; i < count; i++)
        {
            if (pixels[i].A < 128)
            {
                elevation[i] = 0.5f;
            }
        }

        (float[] normalX, float[] normalY) = NormalDerivation.DeriveNormal(elevation, width, height, options);
        float[] occlusion = OcclusionDerivation.DeriveOcclusion(elevation, width, height, options);
        float[] roughness = DeriveRoughness(elevation, width, height, options);

        return new Maps(width, height, elevation, normalX, normalY, roughness, occlusion);
    }

    /// <summary>Packs the maps into the RGBA texture the G-buffer pass samples.</summary>
    /// <param name="maps">The derived maps.</param>
    /// <param name="destination">
    /// Receives the packed texture. Read through a sampler its channels are, in order: the normal's x
    /// and y biased into [0,1], a roughness multiplier halved so 0.5 means "unchanged", and — in alpha
    /// — <b>occlusion</b>, white for open and dark for a crease.
    /// <para>
    /// <b>Alpha used to hold metallic, inverted, and does not any more.</b> Metal in this game is a
    /// property of a MATERIAL rather than of a texel — a fuel tank is steel over its whole surface and
    /// a wall is not steel anywhere on its — so it belongs in materials.json, where somebody can look
    /// at a texture and say so, and not in a channel derived from a photograph that cannot possibly
    /// know. It was also never written: both callers took the default of zero, so alpha was 255 in
    /// every texel of every map ever produced and the shader's <c>max(metallic, 1 - alpha)</c> was a
    /// literal no-op. Meanwhile <see cref="OcclusionDerivation.DeriveOcclusion"/> ran a real eight-direction horizon search
    /// whose output was written out for inspection and never sampled by anything.
    /// </para>
    /// <para>
    /// Swapping the two costs no migration, which is the happy part of a channel having been dead. An
    /// existing map carries 255 there because its metallic was zero; read as occlusion, 255 is "fully
    /// open", which is exactly the right answer for a map that never measured any. So every pack
    /// already on disk stays correct, and regenerating one only adds the contact shading.
    /// </para>
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void PackDetail(Maps maps, Span<Bgra32> destination) =>
        MaterialMapPacking.PackDetail(maps, destination);

    /// <summary>Packs occlusion, roughness and metallic into the industry's usual three channels.</summary>
    /// <param name="maps">The derived maps.</param>
    /// <param name="destination">Receives the ORM texture: red occlusion, green roughness, blue metallic.</param>
    /// <param name="metallic">What to write into the metallic channel.</param>
    /// <remarks>
    /// Not what the renderer reads — <see cref="PackDetail"/> is. This exists so the maps can be written
    /// out in the arrangement every other tool in the world expects, which is what makes them
    /// inspectable and hand-correctable.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void PackOcclusionRoughnessMetallic(
        Maps maps, Span<Bgra32> destination, float metallic = 0f) =>
        MaterialMapPacking.PackOcclusionRoughnessMetallic(maps, destination, metallic);

    /// <summary>Renders a map as an ordinary greyscale image, for inspection.</summary>
    /// <param name="values">The map, one value per pixel.</param>
    /// <param name="destination">Receives the image.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void PackGreyscale(ReadOnlySpan<float> values, Span<Bgra32> destination) =>
        MaterialMapPacking.PackGreyscale(values, destination);

    /// <summary>Renders the normal as the usual blue-dominant tangent-space image.</summary>
    /// <param name="maps">The derived maps.</param>
    /// <param name="destination">Receives the image.</param>
    /// <remarks>
    /// <b>OpenGL convention: green is +Y.</b> The other convention flips it, and a normal map read under
    /// the wrong one lights every bump from the opposite side — which reads as the surface being
    /// inverted rather than as an error, and is therefore easy to ship.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void PackNormal(Maps maps, Span<Bgra32> destination) =>
        MaterialMapPacking.PackNormal(maps, destination);

    /// <summary>Roughness, from the local standard deviation of the height field.</summary>
    /// <remarks>
    /// <para>
    /// A surface whose neighbourhood varies has structure finer than the geometry can show, and
    /// structure finer than the geometry can show is exactly what roughness means physically. So the
    /// measure is the spread of the height field about its own local mean — the same quantity every
    /// other generator of these maps uses, over a window rather than over the four adjacent texels.
    /// </para>
    /// <para>
    /// <b>The window is what makes it meaningful.</b> Measuring against the four immediate neighbours,
    /// as the first version of this did, does not measure a surface's character at all: at that radius
    /// the only thing that varies is single-texel photographic grain, so every texture in the game came
    /// back at almost the same value and the map was flat. A neighbourhood wide enough to contain a
    /// brick and its mortar is what distinguishes brick from painted steel.
    /// </para>
    /// <para>
    /// Computed from two separable box blurs rather than by sampling the window per texel — the
    /// standard deviation is the square root of the mean of the squares less the square of the mean,
    /// and both means are box filters. That makes the cost independent of the radius, which matters
    /// because a per-texel window over the whole game's art is minutes and this is seconds.
    /// </para>
    /// <para>
    /// Never returns below one: a texture being smooth is not evidence that the SURFACE is polished,
    /// only that nobody painted detail on it, and polishing every blank wall in the game is the wrong
    /// reading. What is authored in <c>materials.json</c> stays the floor.
    /// </para>
    /// </remarks>
    private static float[] DeriveRoughness(float[] elevation, int width, int height, Options options)
    {
        int count = width * height;
        float[] roughness = new float[count];
        float range = Math.Clamp(options.RoughnessRange, 0f, 1f);

        // Wide enough to hold a brick and its mortar, and bounded so a large texture does not measure
        // the whole wall as one surface.
        int radius = Math.Clamp(Math.Min(width, height) / 12, 2, 8);

        float[] squares = ArrayPool<float>.Shared.Rent(count);
        ElevationFilters.SquareInto(elevation, squares.AsSpan(0, count));

        float[] localMean = ArrayPool<float>.Shared.Rent(count);
        float[] localMeanOfSquares = ArrayPool<float>.Shared.Rent(count);

        ElevationFilters.Blur(elevation, width, height, radius, 3, localMean.AsSpan(0, count));
        ElevationFilters.Blur(squares.AsSpan(0, count), width, height, radius, 3, localMeanOfSquares.AsSpan(0, count));

        RoughnessFromVariance(
            localMean.AsSpan(0, count), localMeanOfSquares.AsSpan(0, count), range, roughness);

        ArrayPool<float>.Shared.Return(localMeanOfSquares);
        ArrayPool<float>.Shared.Return(localMean);
        ArrayPool<float>.Shared.Return(squares);

        return roughness;
    }

    /// <summary>Turns a neighbourhood's mean and mean-of-squares into a roughness multiplier.</summary>
    /// <remarks>
    /// The variance is clamped at zero before the root: the two means are blurred independently and
    /// floating point can leave their difference very slightly negative where the field is constant.
    /// </remarks>
    private static void RoughnessFromVariance(
        ReadOnlySpan<float> localMean,
        ReadOnlySpan<float> localMeanOfSquares,
        float range,
        Span<float> roughness)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> zero = Vector<float>.Zero;
            Vector<float> one = Vector<float>.One;
            Vector<float> five = new(5f);
            Vector<float> two = new(2f);
            Vector<float> span = new(range);

            for (; i <= roughness.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> mean = Vector.Create(localMean[i..]);
                Vector<float> variance =
                    Vector.Max(Vector.Create(localMeanOfSquares[i..]) - (mean * mean), zero);

                // The height field is normalised to a standard deviation of a fifth globally, so a
                // neighbourhood at that spread is an ordinarily detailed one and saturates the range.
                Vector<float> deviation = Vector.SquareRoot(variance);

                // Centred on one, so a neighbourhood quieter than the texture's own average asks for a
                // SMOOTHER surface and a busier one asks for a rougher: the two halves of the range are
                // what make this channel able to say anything, and the old form only had the upper one.
                Vector<float> busyness = Vector.Min(deviation * five, one);

                (one + (((busyness * two) - one) * span)).CopyTo(roughness[i..]);
            }
        }

        for (; i < roughness.Length; i++)
        {
            float mean = localMean[i];
            float variance = MathF.Max(localMeanOfSquares[i] - (mean * mean), 0f);

            float busyness = Math.Min(MathF.Sqrt(variance) * 5f, 1f);
            roughness[i] = 1f + (((busyness * 2f) - 1f) * range);
        }
    }
}
