using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        Blur(luminanceSpan, width, height, reliefRadius, 3, broad.AsSpan(0, count));

        float[] highPass = ArrayPool<float>.Shared.Rent(count);
        Span<float> highPassSpan = highPass.AsSpan(0, count);
        SubtractInto(luminanceSpan, broad.AsSpan(0, count), highPassSpan);

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
        float mean = Sum(highPassSpan) / count;
        float deviation = MathF.Sqrt(SumOfSquaredDeviations(highPassSpan, mean) / count);

        // One standard deviation becomes a fifth of the range, so an ordinary texel sits well inside it
        // and only genuine outliers clamp. A texture with no relief at all -- a single flat colour --
        // has no deviation to scale by and stays flat, which is the right answer rather than a
        // degenerate one.
        float contrast = deviation > 1e-4f ? 0.2f / deviation : 0f;

        // Centred on a half so that "no relief" is the middle of the range rather than the floor,
        // which is what makes the gradients below signed without a second bias.
        Recentre(highPassSpan, mean, contrast);

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
        Blur(highPassSpan, width, height, 1, 1, elevation);
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
    /// — <b>occlusion</b>, white for open and dark for a crease.
    /// <para>
    /// <b>Alpha used to hold metallic, inverted, and does not any more.</b> Metal in this game is a
    /// property of a MATERIAL rather than of a texel — a fuel tank is steel over its whole surface and
    /// a wall is not steel anywhere on its — so it belongs in materials.json, where somebody can look
    /// at a texture and say so, and not in a channel derived from a photograph that cannot possibly
    /// know. It was also never written: both callers took the default of zero, so alpha was 255 in
    /// every texel of every map ever produced and the shader's <c>max(metallic, 1 - alpha)</c> was a
    /// literal no-op. Meanwhile <see cref="DeriveOcclusion"/> ran a real eight-direction horizon search
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
    public static void PackDetail(Maps maps, Span<Bgra32> destination)
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

        float[] coarse = ArrayPool<float>.Shared.Rent(count);
        Blur(elevation, width, height, 2, 3, coarse.AsSpan(0, count));
        float balance = Math.Clamp(options.DetailBalance, 0f, 1f);
        float strength = Math.Max(options.NormalStrength, 0f);

        // The gradient of a height field is the normal's tilt, negated: ground rising to the right
        // tilts its normal to the left. Scaled by Relief, which is how much height a unit of the
        // normalised field is taken to stand for -- the one number that decides whether this reads as a
        // surface or as a rubbing of one. The two octaves' weights fold into the same constants, so the
        // whole blend is two multiply-adds rather than a blend followed by a scale.
        float fineWeight = -balance * strength * Relief;
        float coarseWeight = -(1f - balance) * strength * Relief;

        float[] rented = ArrayPool<float>.Shared.Rent(10 * width);
        Span<float> scratch = rented.AsSpan(0, 6 * width);
        Span<float> fineX = rented.AsSpan(6 * width, width);
        Span<float> fineY = rented.AsSpan(7 * width, width);
        Span<float> coarseX = rented.AsSpan(8 * width, width);
        Span<float> coarseY = rented.AsSpan(9 * width, width);

        for (int row = 0; row < height; row++)
        {
            // A row of gradients at a time rather than a texel at a time. The Scharr operator reads a
            // 3x3 neighbourhood, which per texel is eight wrapped lookups into a field far larger than
            // a cache line -- and this ran it twice, once per octave, for sixteen scattered loads per
            // texel. Row-wise the same nine values are three contiguous rows and their shifts, so the
            // whole operator becomes element-wise over spans.
            ScharrRow(elevation, width, height, row, scratch, fineX, fineY);
            ScharrRow(coarse, width, height, row, scratch, coarseX, coarseY);

            BlendNormalRow(
                fineX, fineY, coarseX, coarseY, fineWeight, coarseWeight,
                x.AsSpan(row * width, width), y.AsSpan(row * width, width));
        }

        ArrayPool<float>.Shared.Return(rented);
        ArrayPool<float>.Shared.Return(coarse);

        return (x, y);
    }

    /// <summary>The Scharr gradient across a whole row.</summary>
    /// <param name="field">The height field.</param>
    /// <param name="width">The field's width.</param>
    /// <param name="height">The field's height.</param>
    /// <param name="row">Which row to differentiate.</param>
    /// <param name="scratch">Six rows' worth of working space, for the shifted neighbours.</param>
    /// <param name="dx">Receives the gradient along x.</param>
    /// <param name="dy">Receives the gradient along y.</param>
    private static void ScharrRow(
        ReadOnlySpan<float> field, int width, int height, int row,
        Span<float> scratch, Span<float> dx, Span<float> dy)
    {
        ReadOnlySpan<float> top = field.Slice(Wrap(row - 1, height) * width, width);
        ReadOnlySpan<float> middle = field.Slice(row * width, width);
        ReadOnlySpan<float> bottom = field.Slice(Wrap(row + 1, height) * width, width);

        Span<float> topLeft = scratch[..width];
        Span<float> topRight = scratch.Slice(width, width);
        Span<float> middleLeft = scratch.Slice(2 * width, width);
        Span<float> middleRight = scratch.Slice(3 * width, width);
        Span<float> bottomLeft = scratch.Slice(4 * width, width);
        Span<float> bottomRight = scratch.Slice(5 * width, width);

        ShiftRow(top, -1, topLeft);
        ShiftRow(top, 1, topRight);
        ShiftRow(middle, -1, middleLeft);
        ShiftRow(middle, 1, middleRight);
        ShiftRow(bottom, -1, bottomLeft);
        ShiftRow(bottom, 1, bottomRight);

        // Normalised by the kernel's total weight so the result is a slope rather than a slope times
        // thirty-two, which is what lets NormalStrength mean the same thing at every texture size.
        const float scale = 1f / 32f;

        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> three = new(3f * scale);
            Vector<float> ten = new(10f * scale);

            for (; i <= width - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> tl = Vector.Create((ReadOnlySpan<float>)topLeft[i..]);
                Vector<float> tr = Vector.Create((ReadOnlySpan<float>)topRight[i..]);
                Vector<float> ml = Vector.Create((ReadOnlySpan<float>)middleLeft[i..]);
                Vector<float> mr = Vector.Create((ReadOnlySpan<float>)middleRight[i..]);
                Vector<float> bl = Vector.Create((ReadOnlySpan<float>)bottomLeft[i..]);
                Vector<float> br = Vector.Create((ReadOnlySpan<float>)bottomRight[i..]);

                ((three * ((tr - tl) + (br - bl))) + (ten * (mr - ml))).CopyTo(dx[i..]);

                // Y grows downwards in an image and upwards in tangent space, so this subtracts the rows
                // the other way round. That single sign is the OpenGL-versus-DirectX convention.
                Vector<float> t = Vector.Create(top[i..]);
                Vector<float> b = Vector.Create(bottom[i..]);

                ((three * ((tl + tr) - (bl + br))) + (ten * (t - b))).CopyTo(dy[i..]);
            }
        }

        for (; i < width; i++)
        {
            dx[i] = ((3f * ((topRight[i] - topLeft[i]) + (bottomRight[i] - bottomLeft[i])))
                + (10f * (middleRight[i] - middleLeft[i]))) * scale;

            dy[i] = ((3f * ((topLeft[i] + topRight[i]) - (bottomLeft[i] + bottomRight[i])))
                + (10f * (top[i] - bottom[i]))) * scale;
        }
    }

    /// <summary>Blends a row's two gradient octaves and normalises the result into a tilt.</summary>
    /// <remarks>
    /// Normalised as a vector rather than clamped per component. Clamping each axis on its own is not a
    /// unit vector and not even a direction: a texel sloping hard along both axes came out with a longer
    /// normal than one sloping along either, so a diagonal ridge lit differently from the two ridges
    /// that compose it.
    /// </remarks>
    private static void BlendNormalRow(
        ReadOnlySpan<float> fineX, ReadOnlySpan<float> fineY,
        ReadOnlySpan<float> coarseX, ReadOnlySpan<float> coarseY,
        float fineWeight, float coarseWeight,
        Span<float> x, Span<float> y)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> fine = new(fineWeight);
            Vector<float> coarse = new(coarseWeight);
            Vector<float> one = Vector<float>.One;

            for (; i <= x.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> slopeX = (Vector.Create(fineX[i..]) * fine) + (Vector.Create(coarseX[i..]) * coarse);
                Vector<float> slopeY = (Vector.Create(fineY[i..]) * fine) + (Vector.Create(coarseY[i..]) * coarse);

                Vector<float> inverse =
                    one / Vector.SquareRoot((slopeX * slopeX) + (slopeY * slopeY) + one);

                (slopeX * inverse).CopyTo(x[i..]);
                (slopeY * inverse).CopyTo(y[i..]);
            }
        }

        for (; i < x.Length; i++)
        {
            float slopeX = (fineX[i] * fineWeight) + (coarseX[i] * coarseWeight);
            float slopeY = (fineY[i] * fineWeight) + (coarseY[i] * coarseWeight);
            float inverse = 1f / MathF.Sqrt((slopeX * slopeX) + (slopeY * slopeY) + 1f);

            x[i] = slopeX * inverse;
            y[i] = slopeY * inverse;
        }
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

        // Swept a row at a time, with every direction and every step of that row's sweep finished
        // before moving on. Three orderings of the same arithmetic were measured here and the ordering
        // is worth more than the arithmetic:
        //
        // Texel-major -- all sixty-four taps for one texel -- reads a texel up to eight rows away per
        // tap, which on a 128-pixel texture is a four-kilobyte stride, so every tap missed the line the
        // last one loaded. Direction-major -- one full pass over the image per step -- fixed that: each
        // step reads the image shifted by a constant, which streams. But it carries the running horizon
        // as a second full image, so each pass touches a working set twice the texture's size and every
        // one of the sixty-four passes reloads it from L2.
        //
        // Row-major keeps the streaming and drops the second image: the running horizon and the
        // accumulated openness are one row each, and the taps for a row reach at most `reach` rows
        // either side of it. That window slides by one row at a time, so the whole sweep works out of a
        // few kilobytes and the texture is read through once instead of sixty-four times.
        int taps = directions * reach;
        int[] offsetX = new int[taps];
        int[] offsetY = new int[taps];
        float[] falloff = new float[taps];

        for (int direction = 0; direction < directions; direction++)
        {
            float angle = MathF.Tau * direction / directions;
            float stepX = MathF.Cos(angle);
            float stepY = MathF.Sin(angle);

            for (int step = 1; step <= reach; step++)
            {
                int tap = (direction * reach) + step - 1;

                offsetX[tap] = (int)MathF.Round(stepX * step);
                offsetY[tap] = (int)MathF.Round(stepY * step);

                // The tangent of the angle a neighbour at this distance subtends, per unit of height
                // difference: the same height twice as far away hides half as much sky. Precomputed
                // because it is invariant over the pixel loop, where it used to be a division per tap.
                falloff[tap] = relief / step;
            }
        }

        // The field wrap-padded by the sweep's reach on all four sides, built once. Without it a tap
        // straddles the edge and has to be walked as two runs, which doubles the call count and leaves
        // each run an awkward length -- a 127-element run vectorises as fifteen whole vectors and a
        // seven-element scalar tail. Padded, every tap is one run of exactly the texture's width, which
        // on the power-of-two sizes this game's art actually uses is a whole number of vectors and no
        // tail at all.
        int paddedWidth = width + (2 * reach);
        int paddedCount = paddedWidth * (height + (2 * reach));

        float[] padded = ArrayPool<float>.Shared.Rent(paddedCount);
        PadWrapped(elevation, width, height, reach, padded.AsSpan(0, paddedCount));

        float[] rows = ArrayPool<float>.Shared.Rent(2 * width);
        Span<float> highest = rows.AsSpan(0, width);
        Span<float> open = rows.AsSpan(width, width);
        float share = 1f / directions;

        ref float field = ref MemoryMarshal.GetArrayDataReference(elevation);
        ref float border = ref MemoryMarshal.GetArrayDataReference(padded);
        ref float horizon = ref MemoryMarshal.GetReference(highest);

        for (int row = 0; row < height; row++)
        {
            int centre = row * width;
            open.Clear();

            for (int direction = 0; direction < directions; direction++)
            {
                highest.Clear();

                for (int step = 0; step < reach; step++)
                {
                    int tap = (direction * reach) + step;
                    int source = ((row + reach + offsetY[tap]) * paddedWidth) + reach + offsetX[tap];

                    RaiseRun(
                        ref Unsafe.Add(ref border, source),
                        ref Unsafe.Add(ref field, centre),
                        ref horizon,
                        width,
                        falloff[tap]);
                }

                // The visible fraction of this direction's quarter-hemisphere. cos(atan(t)) is
                // 1/sqrt(1+t^2), which is the same answer without the trigonometry.
                AccumulateOpenness(highest, open);
            }

            CloseOcclusion(open, share, strength, occlusion.AsSpan(centre, width));
        }

        ArrayPool<float>.Shared.Return(rows);
        ArrayPool<float>.Shared.Return(padded);

        return occlusion;
    }

    /// <summary>Copies a field with a wrapped border, so a bounded neighbourhood never has to wrap.</summary>
    /// <param name="field">The field to pad.</param>
    /// <param name="width">The field's width.</param>
    /// <param name="height">The field's height.</param>
    /// <param name="border">How many texels of border to add on each side.</param>
    /// <param name="padded">Receives the padded field, <c>(width + 2 * border)</c> per row.</param>
    private static void PadWrapped(
        float[] field, int width, int height, int border, Span<float> padded)
    {
        int paddedWidth = width + (2 * border);

        for (int row = 0; row < height + (2 * border); row++)
        {
            ReadOnlySpan<float> source = field.AsSpan(Wrap(row - border, height) * width, width);
            Span<float> destination = padded.Slice(row * paddedWidth, paddedWidth);

            source.CopyTo(destination[border..]);

            // The border repeats the far side of the row, because these textures tile: the texels left
            // of column zero are genuinely the last ones. Copied a piece at a time so a border wider
            // than the texture -- which nothing asks for, but which the arithmetic permits -- still
            // fills rather than throwing.
            for (int i = 0; i < border; i++)
            {
                destination[border - 1 - i] = source[Wrap(width - 1 - i, width)];
                destination[border + width + i] = source[Wrap(i, width)];
            }
        }
    }

    /// <summary>Turns a row's accumulated openness into the occlusion the renderer samples.</summary>
    private static void CloseOcclusion(
        ReadOnlySpan<float> open, float share, float strength, Span<float> occlusion)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> one = Vector<float>.One;
            Vector<float> zero = Vector<float>.Zero;
            Vector<float> weight = new(share);
            Vector<float> depth = new(strength);

            for (; i <= occlusion.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> value = one - ((one - (Vector.Create(open[i..]) * weight)) * depth);
                Vector.Min(Vector.Max(value, zero), one).CopyTo(occlusion[i..]);
            }
        }

        for (; i < occlusion.Length; i++)
        {
            occlusion[i] = Math.Clamp(1f - ((1f - (open[i] * share)) * strength), 0f, 1f);
        }
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

        float[] squares = ArrayPool<float>.Shared.Rent(count);
        SquareInto(elevation, squares.AsSpan(0, count));

        float[] localMean = ArrayPool<float>.Shared.Rent(count);
        float[] localMeanOfSquares = ArrayPool<float>.Shared.Rent(count);

        Blur(elevation, width, height, radius, 3, localMean.AsSpan(0, count));
        Blur(squares.AsSpan(0, count), width, height, radius, 3, localMeanOfSquares.AsSpan(0, count));

        RoughnessFromVariance(
            localMean.AsSpan(0, count), localMeanOfSquares.AsSpan(0, count), range, roughness);

        ArrayPool<float>.Shared.Return(localMeanOfSquares);
        ArrayPool<float>.Shared.Return(localMean);
        ArrayPool<float>.Shared.Return(squares);

        return roughness;
    }

    /// <summary>A separable box blur, run three times to approximate a Gaussian.</summary>
    /// <remarks>
    /// Three box passes is the standard approximation and is indistinguishable from a true Gaussian at
    /// this scale, while costing a constant number of operations per pixel regardless of radius. The
    /// alternative — a real Gaussian kernel — is quadratic in the radius, and the radius here is a
    /// fraction of the texture rather than a small constant.
    /// </remarks>
    private static void Blur(
        ReadOnlySpan<float> field, int width, int height, int radius, int passes, Span<float> destination)
    {
        if (radius <= 0 || passes <= 0)
        {
            field.CopyTo(destination);
            return;
        }

        float[] scratch = ArrayPool<float>.Shared.Rent(field.Length);
        float[] accumulator = ArrayPool<float>.Shared.Rent(width);

        Span<float> scratchSpan = scratch.AsSpan(0, field.Length);
        Span<float> accumulatorSpan = accumulator.AsSpan(0, width);

        field.CopyTo(destination);

        for (int pass = 0; pass < passes; pass++)
        {
            BlurRows(destination, scratchSpan, width, height, radius);
            BlurColumns(scratchSpan, destination, accumulatorSpan, width, height, radius);
        }

        ArrayPool<float>.Shared.Return(accumulator);
        ArrayPool<float>.Shared.Return(scratch);
    }

    /// <summary>The horizontal half of <see cref="Blur"/>, one row at a time.</summary>
    /// <remarks>
    /// A sliding window rather than a fresh sum per pixel: the window at column <c>x + 1</c> is the one
    /// at <c>x</c> with one value added and one removed, so the whole row costs two operations per pixel
    /// regardless of the radius. That is what the doc comment on <see cref="Blur"/> has always claimed
    /// and what the first implementation did not do — it re-summed all <c>2r + 1</c> taps at every pixel,
    /// which at the default relief on a 128-pixel texture is 31 taps each.
    /// <para>
    /// The wrap is carried in the two window indices instead of being recomputed per tap. Each advances
    /// by exactly one per pixel, so a compare-and-subtract keeps it in range — where the per-tap version
    /// needed two integer divisions, and integer division on a runtime-valued width is the single most
    /// expensive instruction in the whole derivation.
    /// </para>
    /// </remarks>
    private static void BlurRows(
        ReadOnlySpan<float> source, Span<float> destination, int width, int height, int radius)
    {
        float inverse = 1f / ((2 * radius) + 1);

        for (int row = 0; row < height; row++)
        {
            int rowStart = row * width;

            // The window centred on column zero. Walked with a wrapping index rather than indexed
            // directly, because the radius may exceed the width -- a texture narrower than the blur is
            // legitimate here, and it must sum the row round more than once, exactly as before.
            int lead = Wrap(-radius, width);
            float sum = 0f;

            for (int tap = 0; tap <= 2 * radius; tap++)
            {
                sum += source[rowStart + lead];

                if (++lead == width)
                {
                    lead = 0;
                }
            }

            // `lead` now points one past the window's right edge, which is exactly the value entering
            // when the window advances. `trail` points at its left edge, which is the value leaving.
            int trail = Wrap(-radius, width);

            for (int column = 0; column < width; column++)
            {
                destination[rowStart + column] = sum * inverse;

                sum += source[rowStart + lead] - source[rowStart + trail];

                if (++lead == width)
                {
                    lead = 0;
                }

                if (++trail == width)
                {
                    trail = 0;
                }
            }
        }
    }

    /// <summary>The vertical half of <see cref="Blur"/>, a whole row of windows at a time.</summary>
    /// <remarks>
    /// The obvious transcription of the horizontal pass walks one column at a time, which strides
    /// through memory by the texture's width and takes a cache miss on every tap. This carries an
    /// accumulator per column instead and slides the whole set down one row at a time, so every read and
    /// every write is sequential — and every operation in the inner loop is element-wise over a
    /// contiguous row, which is the shape the vectorised helpers want.
    /// </remarks>
    private static void BlurColumns(
        ReadOnlySpan<float> source, Span<float> destination, Span<float> accumulator,
        int width, int height, int radius)
    {
        float inverse = 1f / ((2 * radius) + 1);

        accumulator.Clear();

        int lead = Wrap(-radius, height);

        for (int tap = 0; tap <= 2 * radius; tap++)
        {
            AddRow(source.Slice(lead * width, width), accumulator);

            if (++lead == height)
            {
                lead = 0;
            }
        }

        int trail = Wrap(-radius, height);

        for (int row = 0; row < height; row++)
        {
            ScaleInto(accumulator, inverse, destination.Slice(row * width, width));

            AddRow(source.Slice(lead * width, width), accumulator);
            SubtractRow(source.Slice(trail * width, width), accumulator);

            if (++lead == height)
            {
                lead = 0;
            }

            if (++trail == height)
            {
                trail = 0;
            }
        }
    }

    /// <summary>Adds a row into the running accumulator, element-wise.</summary>
    private static void AddRow(ReadOnlySpan<float> row, Span<float> accumulator)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            for (; i <= row.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                (Vector.Create(accumulator.Slice(i)) + Vector.Create(row.Slice(i))).CopyTo(accumulator.Slice(i));
            }
        }

        for (; i < row.Length; i++)
        {
            accumulator[i] += row[i];
        }
    }

    /// <summary>Removes a row from the running accumulator, element-wise.</summary>
    private static void SubtractRow(ReadOnlySpan<float> row, Span<float> accumulator)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            for (; i <= row.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                (Vector.Create(accumulator.Slice(i)) - Vector.Create(row.Slice(i))).CopyTo(accumulator.Slice(i));
            }
        }

        for (; i < row.Length; i++)
        {
            accumulator[i] -= row[i];
        }
    }

    /// <summary>Writes the accumulator out scaled by the kernel's reciprocal weight.</summary>
    private static void ScaleInto(ReadOnlySpan<float> accumulator, float scale, Span<float> destination)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> factor = new(scale);

            for (; i <= accumulator.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                (Vector.Create(accumulator.Slice(i)) * factor).CopyTo(destination.Slice(i));
            }
        }

        for (; i < accumulator.Length; i++)
        {
            destination[i] = accumulator[i] * scale;
        }
    }

    /// <summary>Copies a row rotated left by an offset, wrapping round at both ends.</summary>
    /// <remarks>
    /// Two block copies rather than a loop with a wrapping index, so the shift costs a pair of
    /// <c>memmove</c>s and leaves the caller with a plain aligned row it can walk element-wise against
    /// the unshifted one.
    /// </remarks>
    private static void ShiftRow(ReadOnlySpan<float> row, int offset, Span<float> destination)
    {
        int width = row.Length;
        int start = Wrap(offset, width);
        int head = width - start;

        row[start..].CopyTo(destination);
        row[..start].CopyTo(destination[head..]);
    }

    /// <summary>Raises one contiguous run of horizons, element-wise.</summary>
    /// <remarks>
    /// Walked by managed reference with <c>LoadUnsafe</c>/<c>StoreUnsafe</c> rather than by re-slicing
    /// the spans. This is the innermost loop of the whole file — it runs <c>directions * reach</c> times
    /// over every pixel — and a fresh slice per iteration costs a bounds check the reference form does
    /// not. No pinning is involved: these overloads take a <c>ref</c> and an element offset, so the
    /// garbage collector can still move the arrays underneath them.
    /// </remarks>
    private static void RaiseRun(
        ref float neighbours, ref float centres, ref float highest, int length, float falloff)
    {
        nuint i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated && length >= Vector<float>.Count)
        {
            Vector<float> scale = new(falloff);
            nuint last = (nuint)(length - Vector<float>.Count);

            for (; i <= last; i += (nuint)Vector<float>.Count)
            {
                Vector<float> subtended =
                    (Vector.LoadUnsafe(ref neighbours, i) - Vector.LoadUnsafe(ref centres, i)) * scale;

                Vector.Max(Vector.LoadUnsafe(ref highest, i), subtended).StoreUnsafe(ref highest, i);
            }
        }

        for (; i < (nuint)length; i++)
        {
            ref float horizon = ref Unsafe.Add(ref highest, i);
            horizon = Math.Max(
                horizon,
                (Unsafe.Add(ref neighbours, i) - Unsafe.Add(ref centres, i)) * falloff);
        }
    }

    /// <summary>Adds one direction's visible fraction into the running openness.</summary>
    /// <remarks>
    /// <c>1 / sqrt(1 + h²)</c> is <c>cos(atan(h))</c> without the trigonometry — the cosine of the angle
    /// the highest occluder along this direction subtends, which is the share of the direction's
    /// quarter-hemisphere still reaching the texel.
    /// </remarks>
    private static void AccumulateOpenness(ReadOnlySpan<float> highest, Span<float> open)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> one = Vector<float>.One;

            for (; i <= highest.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> h = Vector.Create(highest.Slice(i));
                Vector<float> visible = one / Vector.SquareRoot(one + (h * h));

                (Vector.Create((ReadOnlySpan<float>)open.Slice(i)) + visible).CopyTo(open.Slice(i));
            }
        }

        for (; i < highest.Length; i++)
        {
            open[i] += 1f / MathF.Sqrt(1f + (highest[i] * highest[i]));
        }
    }

    /// <summary>Writes the element-wise difference of two fields.</summary>
    private static void SubtractInto(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> destination)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            for (; i <= destination.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                (Vector.Create(left[i..]) - Vector.Create(right[i..])).CopyTo(destination[i..]);
            }
        }

        for (; i < destination.Length; i++)
        {
            destination[i] = left[i] - right[i];
        }
    }

    /// <summary>Writes the element-wise square of a field.</summary>
    private static void SquareInto(ReadOnlySpan<float> field, Span<float> destination)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            for (; i <= destination.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> value = Vector.Create(field[i..]);
                (value * value).CopyTo(destination[i..]);
            }
        }

        for (; i < destination.Length; i++)
        {
            destination[i] = field[i] * field[i];
        }
    }

    /// <summary>Totals a field.</summary>
    /// <remarks>
    /// Four accumulators rather than one, so the additions pipeline instead of queueing behind a single
    /// dependency chain. Combining the lanes at the end reorders the summation against the scalar loop
    /// this replaced, by an amount far below what a byte channel can hold.
    /// </remarks>
    private static float Sum(ReadOnlySpan<float> field)
    {
        int i = 0;
        float total = 0f;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            int stride = Vector<float>.Count;

            if (field.Length >= stride * 4)
            {
                Vector<float> a = Vector<float>.Zero;
                Vector<float> b = Vector<float>.Zero;
                Vector<float> c = Vector<float>.Zero;
                Vector<float> d = Vector<float>.Zero;

                for (; i <= field.Length - (stride * 4); i += stride * 4)
                {
                    a += Vector.Create(field[i..]);
                    b += Vector.Create(field[(i + stride)..]);
                    c += Vector.Create(field[(i + (stride * 2))..]);
                    d += Vector.Create(field[(i + (stride * 3))..]);
                }

                total = Vector.Sum((a + b) + (c + d));
            }

            for (; i <= field.Length - stride; i += stride)
            {
                total += Vector.Sum(Vector.Create(field[i..]));
            }
        }

        for (; i < field.Length; i++)
        {
            total += field[i];
        }

        return total;
    }

    /// <summary>Totals the squared deviations of a field from a mean.</summary>
    private static float SumOfSquaredDeviations(ReadOnlySpan<float> field, float mean)
    {
        int i = 0;
        float total = 0f;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> centre = new(mean);
            Vector<float> accumulator = Vector<float>.Zero;

            for (; i <= field.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> difference = Vector.Create(field[i..]) - centre;
                accumulator += difference * difference;
            }

            total = Vector.Sum(accumulator);
        }

        for (; i < field.Length; i++)
        {
            float difference = field[i] - mean;
            total += difference * difference;
        }

        return total;
    }

    /// <summary>Recentres a field on a half at a fixed contrast, clamped to the unit range.</summary>
    private static void Recentre(Span<float> field, float mean, float contrast)
    {
        int i = 0;

        if (Vector<float>.IsSupported && Vector.IsHardwareAccelerated)
        {
            Vector<float> centre = new(mean);
            Vector<float> scale = new(contrast);
            Vector<float> half = new(0.5f);
            Vector<float> zero = Vector<float>.Zero;
            Vector<float> one = Vector<float>.One;

            for (; i <= field.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Vector<float> value = ((Vector.Create((ReadOnlySpan<float>)field[i..]) - centre) * scale) + half;
                Vector.Min(Vector.Max(value, zero), one).CopyTo(field[i..]);
            }
        }

        for (; i < field.Length; i++)
        {
            field[i] = Math.Clamp(((field[i] - mean) * contrast) + 0.5f, 0f, 1f);
        }
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

    /// <summary>Brings a coordinate into <c>[0, extent)</c>, wrapping negatives round.</summary>
    private static int Wrap(int value, int extent)
    {
        int wrapped = value % extent;
        return wrapped < 0 ? wrapped + extent : wrapped;
    }

    private static byte Quantise(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
