using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The little arithmetic of drawing between two simulation ticks, shared by everything that does it.
/// </summary>
/// <remarks>
/// The simulation computes states at 30 Hz and the presentation draws between the last two of them
/// (see <c>RenderCVars.SmoothPresentation</c>); these are the blends that drawing needs and the game
/// layer deliberately does not provide, because interpolation is a property of the presentation.
/// </remarks>
internal static class PresentationBlend
{
    /// <summary>Interpolates a position.</summary>
    public static Vector3d Lerp(Vector3d from, Vector3d to, float t) => new(
        from.X + ((to.X - from.X) * t),
        from.Y + ((to.Y - from.Y) * t),
        from.Z + ((to.Z - from.Z) * t));

    /// <summary>Interpolates a single-precision vector.</summary>
    public static Vector3f Lerp(Vector3f from, Vector3f to, float t) => from + ((to - from) * t);

    /// <summary>Interpolates a heading along the shorter way round.</summary>
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
    public static Matrix3f InterpolateRotation(in Matrix3f from, in Matrix3f to, float t)
    {
        Vector3f row1 = (from.Row1 + ((to.Row1 - from.Row1) * t)).Normalized();
        Vector3f blended3 = from.Row3 + ((to.Row3 - from.Row3) * t);
        Vector3f row2 = Vector3f.Cross(blended3, row1).Normalized();
        Vector3f row3 = Vector3f.Cross(row1, row2);
        return Matrix3f.FromRows(row1, row2, row3);
    }
}
