using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenIGI.Core.Graphics;

/// <summary>The ambient occlusion <see cref="MaterialMapGenerator"/> derives from a height field.</summary>
/// <remarks>
/// A short horizon search over the wrap-padded field, swept a row at a time. The innermost loops walk
/// managed references rather than slices; see <see cref="RaiseRun"/>.
/// </remarks>
internal static class OcclusionDerivation
{
    /// <summary>Ambient occlusion, from a horizon search over the height field.</summary>
    /// <remarks>
    /// The same question the screen-space pass asks of the depth buffer, asked here of the texture's own
    /// relief: sweep a set of directions, find the highest thing along each, and the fraction of the
    /// hemisphere left over is what reaches this texel. It resolves what a screen-space search never
    /// can, because the occluder is smaller than a pixel — the mortar between bricks, the gap between
    /// planks.
    /// </remarks>
    internal static float[] DeriveOcclusion(float[] elevation, int width, int height, MaterialMapGenerator.Options options)
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
        const float relief = NormalDerivation.Relief * 3f;

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
            ReadOnlySpan<float> source = field.AsSpan(ElevationFilters.Wrap(row - border, height) * width, width);
            Span<float> destination = padded.Slice(row * paddedWidth, paddedWidth);

            source.CopyTo(destination[border..]);

            // The border repeats the far side of the row, because these textures tile: the texels left
            // of column zero are genuinely the last ones. Copied a piece at a time so a border wider
            // than the texture -- which nothing asks for, but which the arithmetic permits -- still
            // fills rather than throwing.
            for (int i = 0; i < border; i++)
            {
                destination[border - 1 - i] = source[ElevationFilters.Wrap(width - 1 - i, width)];
                destination[border + width + i] = source[ElevationFilters.Wrap(i, width)];
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
}
