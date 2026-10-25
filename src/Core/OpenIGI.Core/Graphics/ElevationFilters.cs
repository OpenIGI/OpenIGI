using System.Buffers;
using System.Numerics;

namespace OpenIGI.Core.Graphics;

/// <summary>
/// The field arithmetic <see cref="MaterialMapGenerator"/> is built from: the wrapping separable blur,
/// the row shifts and the element-wise and reducing helpers, vectorised where the hardware allows.
/// </summary>
/// <remarks>
/// Every texture these run over tiles, so every neighbourhood wraps. The vector and scalar paths of each
/// helper compute the same expression in the same order per element; only <see cref="Sum"/> reorders its
/// additions, and says so.
/// </remarks>
internal static class ElevationFilters
{
    /// <summary>A separable box blur, run three times to approximate a Gaussian.</summary>
    /// <remarks>
    /// Three box passes is the standard approximation and is indistinguishable from a true Gaussian at
    /// this scale, while costing a constant number of operations per pixel regardless of radius. The
    /// alternative — a real Gaussian kernel — is quadratic in the radius, and the radius here is a
    /// fraction of the texture rather than a small constant.
    /// </remarks>
    internal static void Blur(
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
    internal static void ShiftRow(ReadOnlySpan<float> row, int offset, Span<float> destination)
    {
        int width = row.Length;
        int start = Wrap(offset, width);
        int head = width - start;

        row[start..].CopyTo(destination);
        row[..start].CopyTo(destination[head..]);
    }

    /// <summary>Writes the element-wise difference of two fields.</summary>
    internal static void SubtractInto(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> destination)
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
    internal static void SquareInto(ReadOnlySpan<float> field, Span<float> destination)
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
    internal static float Sum(ReadOnlySpan<float> field)
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
    internal static float SumOfSquaredDeviations(ReadOnlySpan<float> field, float mean)
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
    internal static void Recentre(Span<float> field, float mean, float contrast)
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

    /// <summary>Brings a coordinate into <c>[0, extent)</c>, wrapping negatives round.</summary>
    internal static int Wrap(int value, int extent)
    {
        int wrapped = value % extent;
        return wrapped < 0 ? wrapped + extent : wrapped;
    }
}
