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
    /// How far either side of the material's own roughness the derived value may move it. Deliberately
    /// mild: this is a texture's busyness standing in for a measurement.
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
        float RoughnessRange = 0.35f,
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

        int count = width * height;

        if (pixels.Length < count)
        {
            throw new ArgumentException(
                $"A {width}x{height} texture needs {count} pixels, got {pixels.Length}.", nameof(pixels));
        }

        float[] luminance = new float[count];

        for (int i = 0; i < count; i++)
        {
            Bgra32 pixel = pixels[i];

            // Rec. 601, which is what the eye weights these channels at.
            luminance[i] = ((0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B)) / 255f;
        }

        // The frequency split. The blur radius is a fraction of the texture rather than a constant:
        // these textures run from 8x8 to 128x128, and a fixed radius would be a gentle high-pass on
        // one and erase the other entirely.
        float relief = Math.Clamp(options.Relief, 0.01f, 1f);
        int reliefRadius = Math.Max((int)MathF.Round(Math.Min(width, height) * relief), 1);

        float[] broad = Blur(luminance, width, height, reliefRadius);
        float[] elevation = new float[count];

        for (int i = 0; i < count; i++)
        {
            elevation[i] = luminance[i] - broad[i];
        }

        // Normalised to a fixed contrast, and this is what makes every constant downstream mean
        // something. What a high-pass leaves behind depends entirely on the texture: a photograph of
        // brick leaves a wide spread, a flat painted panel leaves almost nothing, and the difference
        // between them is a factor of thirty. Deriving a normal from that directly gives the brick a
        // usable tilt and the panel none at all -- which is how the first run of this came out, with
        // every map an almost uniform flat blue.
        //
        // Scaled by the field's own standard deviation rather than its extremes, because one bright
        // speck would otherwise set the scale for the whole texture and flatten everything else.
        float mean = 0f;

        for (int i = 0; i < count; i++)
        {
            mean += elevation[i];
        }

        mean /= count;
        float variance = 0f;

        for (int i = 0; i < count; i++)
        {
            float difference = elevation[i] - mean;
            variance += difference * difference;
        }

        float deviation = MathF.Sqrt(variance / count);

        // One standard deviation becomes a fifth of the range, so an ordinary texel sits well inside it
        // and only genuine outliers clamp. A texture with no relief at all -- a single flat colour --
        // has no deviation to scale by and stays flat, which is the right answer rather than a
        // degenerate one.
        float contrast = deviation > 1e-4f ? 0.2f / deviation : 0f;

        for (int i = 0; i < count; i++)
        {
            // Centred on a half so that "no relief" is the middle of the range rather than the floor,
            // which is what makes the gradients below signed without a second bias.
            elevation[i] = Math.Clamp(((elevation[i] - mean) * contrast) + 0.5f, 0f, 1f);
        }

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
        elevation = Blur(elevation, width, height, 1, passes: 1);

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

        (float[] normalX, float[] normalY) = DeriveNormal(elevation, width, height, options);
        float[] occlusion = DeriveOcclusion(elevation, width, height, options);
        float[] roughness = DeriveRoughness(elevation, width, height, options);

        return new Maps(width, height, elevation, normalX, normalY, roughness, occlusion);
    }

    /// <summary>Packs the maps into the RGBA texture the G-buffer pass samples.</summary>
    /// <param name="maps">The derived maps.</param>
    /// <param name="destination">
    /// Receives the packed texture. Read through a sampler its channels are, in order: the normal's x
    /// and y biased into [0,1], a roughness multiplier halved so 0.5 means "unchanged", and — in alpha
    /// — <b>how dielectric the surface is</b>, which is one minus metallic.
    /// <para>
    /// Alpha is inverted for a practical reason rather than a principled one. Metallic is nearly always
    /// zero, so storing it directly makes every one of these files fully transparent, and a fully
    /// transparent PNG opens in an image viewer as nothing at all — which is exactly how the first
    /// build of this was reported, as "the generated images are invalid". Storing its complement makes
    /// the ordinary case opaque and inspectable, and has the useful property that a map flattened to
    /// opaque by some other tool reads back as the safe default rather than as solid metal.
    /// </para>
    /// </param>
    /// <param name="metallic">
    /// What to write into the metallic channel, for the caller that has authored data saying so. It
    /// cannot be derived — see the type's remarks.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="maps"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static void PackDetail(Maps maps, Span<Bgra32> destination, float metallic = 0f)
    {
        ArgumentNullException.ThrowIfNull(maps);

        int count = maps.Width * maps.Height;

        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"A {maps.Width}x{maps.Height} map needs {count} pixels, got {destination.Length}.",
                nameof(destination));
        }

        byte dielectric = Quantise(1f - Math.Clamp(metallic, 0f, 1f));

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
                a: dielectric);
        }
    }

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
        Maps maps, Span<Bgra32> destination, float metallic = 0f)
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

    /// <summary>Renders a map as an ordinary greyscale image, for inspection.</summary>
    /// <param name="values">The map, one value per pixel.</param>
    /// <param name="destination">Receives the image.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
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
    public static void PackNormal(Maps maps, Span<Bgra32> destination)
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

    /// <summary>Gradients of the height field, over two octaves.</summary>
    /// <remarks>
    /// Scharr rather than Sobel. The two differ only in their weights, and Scharr's are the ones fitted
    /// to make the operator's response the same in every direction — with Sobel a diagonal edge reports
    /// a slightly different slope from a horizontal one of the same steepness, which on a tiling texture
    /// accumulates into a visible bias along one axis.
    /// <para>
    /// Two octaves because one is always the wrong choice: the fine one alone is noisy on smooth
    /// surfaces, and the coarse one alone loses the grain that makes a surface read as a material.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How much relief a unit of the normalised height field stands for.
    /// </summary>
    /// <remarks>
    /// The one number in here that is taste rather than derivation. The height field is a contrast-
    /// normalised high-pass of luminance, not a measurement, so nothing in the data says how deep a
    /// dark texel is — this asserts it. Measured by eye against the game's brick, corrugated metal and
    /// gravel at <c>r_material_detail 1</c>; higher looks embossed, lower reads as flat.
    /// </remarks>
    private const float Relief = 20f;

    private static (float[] X, float[] Y) DeriveNormal(
        float[] elevation, int width, int height, Options options)
    {
        int count = width * height;
        float[] x = new float[count];
        float[] y = new float[count];

        float[] coarse = Blur(elevation, width, height, 2);
        float balance = Math.Clamp(options.DetailBalance, 0f, 1f);
        float strength = Math.Max(options.NormalStrength, 0f);

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                (float fineX, float fineY) = Scharr(elevation, width, height, column, row);
                (float coarseX, float coarseY) = Scharr(coarse, width, height, column, row);

                float dx = (fineX * balance) + (coarseX * (1f - balance));
                float dy = (fineY * balance) + (coarseY * (1f - balance));

                // The gradient of a height field is the normal's tilt, negated: ground rising to the
                // right tilts its normal to the left. Scaled by Relief, which is how much height a unit
                // of the normalised field is taken to stand for -- the one number that decides whether
                // this reads as a surface or as a rubbing of one.
                float slopeX = -dx * strength * Relief;
                float slopeY = -dy * strength * Relief;

                // Normalised as a vector rather than clamped per component. Clamping each axis on its
                // own is not a unit vector and not even a direction: a texel sloping hard along both
                // axes came out with a longer normal than one sloping along either, so a diagonal
                // ridge lit differently from the two ridges that compose it.
                float inverse = 1f / MathF.Sqrt((slopeX * slopeX) + (slopeY * slopeY) + 1f);

                x[(row * width) + column] = slopeX * inverse;
                y[(row * width) + column] = slopeY * inverse;
            }
        }

        return (x, y);
    }

    /// <summary>Ambient occlusion, from a horizon search over the height field.</summary>
    /// <remarks>
    /// The same question the screen-space pass asks of the depth buffer, asked here of the texture's own
    /// relief: sweep a set of directions, find the highest thing along each, and the fraction of the
    /// hemisphere left over is what reaches this texel. It resolves what a screen-space search never
    /// can, because the occluder is smaller than a pixel — the mortar between bricks, the gap between
    /// planks.
    /// </remarks>
    private static float[] DeriveOcclusion(float[] elevation, int width, int height, Options options)
    {
        int count = width * height;
        float[] occlusion = new float[count];

        float strength = Math.Max(options.OcclusionStrength, 0f);

        if (strength <= 0f)
        {
            Array.Fill(occlusion, 1f);
            return occlusion;
        }

        int directions = Math.Clamp(options.OcclusionDirections, 1, 32);

        // Short. This is cavity occlusion, not a horizon over a landscape: what it exists to find is
        // the mortar between two bricks and the gap between two planks, both of which are two or three
        // texels wide. Sweeping a tenth of the texture instead averages those grooves away against all
        // the flat face around them, which is how the first run came out at a mean of 251 out of 255 --
        // an occlusion map that occluded nothing.
        int reach = Math.Max(Math.Min(width, height) / 16, 3);

        // Deeper than the normal's relief, and for a reason rather than to taste. The normal is built
        // from a gradient between ADJACENT texels; this compares texels several apart, over which the
        // same physical groove presents a much shallower average slope. The factor is what puts the two
        // back on the same footing, so a surface does not report a deep groove in one map and a flat
        // face in the other.
        const float relief = Relief * 3f;

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                float centre = elevation[(row * width) + column];
                float open = 0f;

                for (int direction = 0; direction < directions; direction++)
                {
                    float angle = MathF.Tau * direction / directions;
                    float stepX = MathF.Cos(angle);
                    float stepY = MathF.Sin(angle);
                    float highest = 0f;

                    for (int step = 1; step <= reach; step++)
                    {
                        int sampleX = column + (int)MathF.Round(stepX * step);
                        int sampleY = row + (int)MathF.Round(stepY * step);
                        float difference = At(elevation, width, height, sampleX, sampleY) - centre;

                        // The tangent of the angle this neighbour subtends. Divided by its distance,
                        // because the same height twice as far away hides half as much sky.
                        highest = Math.Max(highest, difference * relief / step);
                    }

                    // The visible fraction of this direction's quarter-hemisphere. cos(atan(t)) is
                    // 1/sqrt(1+t^2), which is the same answer without the trigonometry.
                    open += 1f / MathF.Sqrt(1f + (highest * highest));
                }

                open /= directions;
                occlusion[(row * width) + column] = Math.Clamp(1f - ((1f - open) * strength), 0f, 1f);
            }
        }

        return occlusion;
    }

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

        float[] squares = new float[count];

        for (int i = 0; i < count; i++)
        {
            squares[i] = elevation[i] * elevation[i];
        }

        float[] localMean = Blur(elevation, width, height, radius);
        float[] localMeanOfSquares = Blur(squares, width, height, radius);

        for (int i = 0; i < count; i++)
        {
            // Clamped at zero before the root: the two means are computed independently and floating
            // point can leave their difference very slightly negative where the field is constant.
            float variance = MathF.Max(localMeanOfSquares[i] - (localMean[i] * localMean[i]), 0f);
            float deviation = MathF.Sqrt(variance);

            // The height field is normalised to a standard deviation of a fifth globally, so a
            // neighbourhood at that spread is an ordinarily detailed one and saturates the range.
            roughness[i] = 1f + (Math.Min(deviation * 5f, 1f) * range);
        }

        return roughness;
    }

    /// <summary>The Scharr gradient at a texel.</summary>
    private static (float X, float Y) Scharr(float[] field, int width, int height, int x, int y)
    {
        float topLeft = At(field, width, height, x - 1, y - 1);
        float top = At(field, width, height, x, y - 1);
        float topRight = At(field, width, height, x + 1, y - 1);
        float left = At(field, width, height, x - 1, y);
        float right = At(field, width, height, x + 1, y);
        float bottomLeft = At(field, width, height, x - 1, y + 1);
        float bottom = At(field, width, height, x, y + 1);
        float bottomRight = At(field, width, height, x + 1, y + 1);

        // Normalised by the kernel's total weight so the result is a slope rather than a slope times
        // thirty-two, which is what lets NormalStrength mean the same thing at every texture size.
        const float scale = 1f / 32f;

        float dx = ((3f * topRight) + (10f * right) + (3f * bottomRight))
            - ((3f * topLeft) + (10f * left) + (3f * bottomLeft));

        // Y grows downwards in an image and upwards in tangent space, so this subtracts the rows the
        // other way round. That single sign is the OpenGL-versus-DirectX convention.
        float dy = ((3f * topLeft) + (10f * top) + (3f * topRight))
            - ((3f * bottomLeft) + (10f * bottom) + (3f * bottomRight));

        return (dx * scale, dy * scale);
    }

    /// <summary>A separable box blur, run three times to approximate a Gaussian.</summary>
    /// <remarks>
    /// Three box passes is the standard approximation and is indistinguishable from a true Gaussian at
    /// this scale, while costing a constant number of operations per pixel regardless of radius. The
    /// alternative — a real Gaussian kernel — is quadratic in the radius, and the radius here is a
    /// fraction of the texture rather than a small constant.
    /// </remarks>
    private static float[] Blur(float[] field, int width, int height, int radius, int passes = 3)
    {
        if (radius <= 0 || passes <= 0)
        {
            return (float[])field.Clone();
        }

        float[] source = (float[])field.Clone();
        float[] scratch = new float[field.Length];

        for (int pass = 0; pass < passes; pass++)
        {
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    float sum = 0f;

                    for (int offset = -radius; offset <= radius; offset++)
                    {
                        sum += At(source, width, height, column + offset, row);
                    }

                    scratch[(row * width) + column] = sum / ((2 * radius) + 1);
                }
            }

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    float sum = 0f;

                    for (int offset = -radius; offset <= radius; offset++)
                    {
                        sum += At(scratch, width, height, column, row + offset);
                    }

                    source[(row * width) + column] = sum / ((2 * radius) + 1);
                }
            }
        }

        return source;
    }

    /// <summary>Reads a value, wrapping at every edge because these textures tile.</summary>
    private static float At(float[] field, int width, int height, int x, int y)
    {
        int wrappedX = ((x % width) + width) % width;
        int wrappedY = ((y % height) + height) % height;
        return field[(wrappedY * width) + wrappedX];
    }

    private static byte Quantise(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
