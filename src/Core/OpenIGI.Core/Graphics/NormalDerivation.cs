using System.Buffers;
using System.Numerics;

namespace OpenIGI.Core.Graphics;

/// <summary>The tangent-space normal <see cref="MaterialMapGenerator"/> derives from a height field.</summary>
/// <remarks>
/// Scharr gradients over two octaves of the height, blended and normalised a row at a time. The relief
/// constant lives here because it is what turns a unit of the normalised field into a slope, and the
/// occlusion search borrows it so the two maps agree about how deep a groove is.
/// </remarks>
internal static class NormalDerivation
{
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
    internal const float Relief = 20f;

    internal static (float[] X, float[] Y) DeriveNormal(
        float[] elevation, int width, int height, MaterialMapGenerator.Options options)
    {
        int count = width * height;
        float[] x = new float[count];
        float[] y = new float[count];

        float[] coarse = ArrayPool<float>.Shared.Rent(count);
        ElevationFilters.Blur(elevation, width, height, 2, 3, coarse.AsSpan(0, count));
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
        ReadOnlySpan<float> top = field.Slice(ElevationFilters.Wrap(row - 1, height) * width, width);
        ReadOnlySpan<float> middle = field.Slice(row * width, width);
        ReadOnlySpan<float> bottom = field.Slice(ElevationFilters.Wrap(row + 1, height) * width, width);

        Span<float> topLeft = scratch[..width];
        Span<float> topRight = scratch.Slice(width, width);
        Span<float> middleLeft = scratch.Slice(2 * width, width);
        Span<float> middleRight = scratch.Slice(3 * width, width);
        Span<float> bottomLeft = scratch.Slice(4 * width, width);
        Span<float> bottomRight = scratch.Slice(5 * width, width);

        ElevationFilters.ShiftRow(top, -1, topLeft);
        ElevationFilters.ShiftRow(top, 1, topRight);
        ElevationFilters.ShiftRow(middle, -1, middleLeft);
        ElevationFilters.ShiftRow(middle, 1, middleRight);
        ElevationFilters.ShiftRow(bottom, -1, bottomLeft);
        ElevationFilters.ShiftRow(bottom, 1, bottomRight);

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
}
