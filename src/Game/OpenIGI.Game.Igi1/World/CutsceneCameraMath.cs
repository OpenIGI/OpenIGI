using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The camera arithmetic a cutscene frame is built with: the Hermite knots, the look-at basis and the shake noise.</summary>
internal static class CutsceneCameraMath
{
    internal static Matrix3f ToCameraOrientation(Matrix3f orientation)
    {
        Matrix3f axes = orientation.Transposed();
        return Matrix3f.FromRows(axes.Row1, -axes.Row2, axes.Row3);
    }

    internal static Matrix3f LookAt(Vector3d from, Vector3d to)
    {
        Vector3d d = to - from;
        double length = d.Length;
        if (length <= double.Epsilon)
        {
            return Matrix3f.Identity;
        }
        Vector3f forward = new((float)(d.X / length), (float)(d.Y / length), (float)(d.Z / length));
        Vector3f right = new(forward.Y, -forward.X, 0f);
        float rl = MathF.Sqrt((right.X * right.X) + (right.Y * right.Y));
        right = rl > 0f ? right * (1f / rl) : Vector3f.UnitX;
        // sub_508620 writes right, -cross(right, forward), forward as COLUMNS. The final camera
        // boundary transposes this retail storage into Camera3D's row-axis representation.
        Vector3f up = -Vector3f.Cross(right, forward);
        return new Matrix3f(
            right.X, up.X, forward.X,
            right.Y, up.Y, forward.Y,
            right.Z, up.Z, forward.Z);
    }

    internal static double Hermite(double time, double t0, double t1, double t2, double t3, double v0, double v1, double v2, double v3)
    {
        double s1 = t2 == t0 ? 0.0 : 2.0 * (t1 - t0) / (t2 - t0) * ((v2 - v0) * 0.5);
        double s2 = t3 == t1 ? 0.0 : 2.0 * (t2 - t1) / (t3 - t1) * ((v3 - v1) * 0.5);
        double u = t2 == t1 ? 0.0 : (time - t1) / (t2 - t1);
        double a = (2.0 * v1) - (2.0 * v2) + s2 + s1;
        double b = (3.0 * v2) - (3.0 * v1) - (2.0 * s1) - s2;
        return v1 + (u * u * u * a) + (u * u * b) + (u * s1);
    }

    internal static float Noise(ref uint value)
    {
        value = unchecked((value * 1664525u) + 1013904223u);
        return ((value >> 8) / 16777215f) - 0.5f;
    }

    internal static bool IsBandFilter(string filter) =>
        filter.Contains("BAND", StringComparison.OrdinalIgnoreCase);
}
