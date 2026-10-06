namespace OpenIGI.Rendering;

/// <summary>
/// The sub-pixel offsets the camera is nudged by, one per frame.
/// </summary>
/// <remarks>
/// <para>
/// A rasteriser takes one sample per pixel, at its centre, and everything temporal anti-aliasing does
/// follows from moving that sample somewhere else each frame. Over a few frames the pixel has been
/// sampled all over its own area, and averaging those samples is supersampling paid for in time rather
/// than in bandwidth — which is why this is also what makes the noisy passes affordable: fog, ambient
/// occlusion and screen-space GI can each take a fraction of the samples they would need to be clean in
/// one frame, and let the accumulation finish the job.
/// </para>
/// <para>
/// The offsets come from a Halton sequence rather than from random numbers or a regular grid. Random
/// offsets clump, leaving parts of the pixel unsampled for several frames and the rest oversampled; a
/// grid is uniform but revisits the same few positions, so its pattern beats against the geometry. A
/// low-discrepancy sequence is the thing that is neither: every prefix of it covers the pixel evenly,
/// so the average is unbiased however few frames the history has survived.
/// </para>
/// <para>
/// Base 2 and base 3, the conventional pair, and sixteen of them. A longer sequence samples the pixel
/// more finely but takes proportionally longer to converge after the history is thrown away — which
/// happens on every camera cut and every fast turn, so it is not a rare case worth pessimising.
/// </para>
/// </remarks>
public static class TemporalJitter
{
    /// <summary>The number of offsets before the pattern repeats.</summary>
    public const int Length = 16;

    /// <summary>Gets the offset for a frame, in pixels, each component within ±0.5.</summary>
    /// <param name="frame">The frame counter. Any value, including a negative one, is accepted.</param>
    /// <returns>The offset from the pixel centre.</returns>
    /// <remarks>
    /// Centred on zero rather than running 0 to 1, so that a jitter of index zero is the unjittered
    /// camera and a sequence cut short at any point has averaged to somewhere near the pixel centre
    /// rather than to a corner of it.
    /// </remarks>
    public static (float X, float Y) At(int frame) => At(frame, Length);

    /// <summary>Gets the offset for a frame from a sequence of a given length.</summary>
    /// <param name="frame">The frame counter. Any value, including a negative one, is accepted.</param>
    /// <param name="phases">How many offsets the sequence cycles through; see <see cref="RenderResolutionPolicy"/>.</param>
    /// <returns>The offset from the pixel centre, each component within ±0.5.</returns>
    /// <remarks>
    /// The same Halton pair, only longer. Rendering further below the output spreads each render pixel over
    /// more output pixels, and the sequence has to visit more points of it before each of those has been
    /// sampled as often as an unscaled one would be.
    /// </remarks>
    public static (float X, float Y) At(int frame, int phases)
    {
        int length = Math.Max(1, phases);
        int index = (int)(((uint)frame % (uint)length) + 1);
        return (Halton(index, 2) - 0.5f, Halton(index, 3) - 0.5f);
    }

    /// <summary>Computes the radical inverse of an index in a base.</summary>
    /// <param name="index">The one-based index into the sequence.</param>
    /// <param name="radix">The base, which should be prime.</param>
    /// <returns>A value in [0, 1).</returns>
    /// <remarks>
    /// The index's digits in the given base, mirrored about the point: 5 in base 2 is 101, and 0.101
    /// in base 2 is 0.625. Mirroring is what makes the sequence low-discrepancy, because incrementing
    /// the index changes the <em>most</em> significant digit of the result and therefore lands the next
    /// sample in the largest remaining gap.
    /// </remarks>
    public static float Halton(int index, int radix)
    {
        float result = 0f;
        float fraction = 1f / radix;

        for (int remaining = index; remaining > 0; remaining /= radix)
        {
            result += (remaining % radix) * fraction;
            fraction /= radix;
        }

        return result;
    }
}
