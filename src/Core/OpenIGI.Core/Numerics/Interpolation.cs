namespace OpenIGI.Core.Numerics;

/// <summary>The blends of drawing between two simulation ticks that the vectors do not already provide.</summary>
/// <remarks>
/// The simulation computes states at 30 Hz and the picture is drawn between the last two of them. Positions blend with
/// <see cref="Vector3d.Lerp"/> and <see cref="Vector3f.Lerp"/>; a heading and an orientation need these.
/// </remarks>
public static class Interpolation
{
    /// <summary>Interpolates a heading along the shorter way round.</summary>
    /// <param name="from">The heading at <paramref name="t"/> = 0, in radians.</param>
    /// <param name="to">The heading at <paramref name="t"/> = 1, in radians.</param>
    /// <param name="t">The interpolation factor.</param>
    /// <returns>The interpolated heading.</returns>
    public static float LerpAngle(float from, float to, float t)
    {
        float difference = MathF.IEEERemainder(to - from, 2f * MathF.PI);
        return from + (difference * t);
    }

    /// <summary>Blends two small rotations by lerping their rows and re-orthonormalising.</summary>
    /// <remarks>
    /// Not a general slerp and does not need to be: the two are one simulation tick apart, a few
    /// degrees at most, where the chord and the arc are indistinguishable. Degenerate only at half a
    /// turn per tick, which nothing animated does.
    /// </remarks>
    /// <param name="from">The orientation at <paramref name="t"/> = 0.</param>
    /// <param name="to">The orientation at <paramref name="t"/> = 1.</param>
    /// <param name="t">The interpolation factor.</param>
    /// <returns>The interpolated orientation.</returns>
    public static Matrix3f InterpolateRotation(in Matrix3f from, in Matrix3f to, float t)
    {
        Vector3f row1 = (from.Row1 + ((to.Row1 - from.Row1) * t)).Normalized();
        Vector3f blended3 = from.Row3 + ((to.Row3 - from.Row3) * t);
        Vector3f row2 = Vector3f.Cross(blended3, row1).Normalized();
        Vector3f row3 = Vector3f.Cross(row1, row2);
        return Matrix3f.FromRows(row1, row2, row3);
    }
}
