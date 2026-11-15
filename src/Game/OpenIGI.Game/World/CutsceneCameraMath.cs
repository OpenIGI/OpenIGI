using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One authored camera key of a cutscene: an <c>EditCamera</c> as the camera path reads it.</summary>
/// <param name="Position">Where the camera stands, in world units.</param>
/// <param name="Alpha">Its turn about X, in radians.</param>
/// <param name="Beta">Its turn about Y, in radians.</param>
/// <param name="Gamma">Its turn about Z, in radians.</param>
/// <param name="FieldOfView">Its field of view factor.</param>
/// <param name="Duration">How long until the next key, in seconds before the scene's time scale.</param>
/// <param name="SmoothToNext">Whether the path curves on into the next key rather than holding.</param>
/// <param name="TimeOfDay">The hour it sets, or below zero for the scene's own.</param>
/// <param name="Noise">The filter's noise.</param>
/// <param name="Shake">How hard the camera shakes.</param>
public readonly record struct CutsceneKey(
    Vector3d Position,
    float Alpha,
    float Beta,
    float Gamma,
    float FieldOfView,
    float Duration,
    bool SmoothToNext,
    float TimeOfDay,
    float Noise,
    float Shake);

/// <summary>Where a cutscene's camera is at a moment, before any link, target or shake: the path through its keys.</summary>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Orientation">How it is turned, in the engine's storage.</param>
/// <param name="FieldOfView">Its field of view factor.</param>
/// <param name="TimeOfDay">The hour it sets, or below zero for the scene's own.</param>
/// <param name="Noise">The filter's noise.</param>
/// <param name="Shake">How hard it shakes.</param>
/// <param name="Current">The key whose span the moment falls in.</param>
/// <param name="SpanStart">When that key's span starts, in scene seconds.</param>
/// <param name="SpanEnd">When the next key's starts.</param>
/// <param name="Next">The key after it, clamped to the last.</param>
public readonly record struct CutscenePathPoint(
    Vector3d Position,
    Matrix3f Orientation,
    float FieldOfView,
    float TimeOfDay,
    float Noise,
    float Shake,
    int Current,
    double SpanStart,
    double SpanEnd,
    int Next);

/// <summary>The camera arithmetic a cutscene frame is built with: the path through its keys, the look-at basis and the shake noise.</summary>
/// <remarks>
/// One implementation for the game, which plays a scene with it, and the editor, which previews the scene being edited:
/// the keys are the scene's <c>EditCamera</c> knots flattened depth first (<c>0x4F4880</c>), every one kept, duration zero
/// included, and each channel runs through them on the non-uniform cubic Hermite of <c>0x4F55D0</c>.
/// </remarks>
public static class CutsceneCameraMath
{
    /// <summary>Where a scene's camera path is at a moment.</summary>
    /// <param name="keys">The scene's keys, in order; at least one.</param>
    /// <param name="timeScale">The scene's time scale, which every duration is multiplied by.</param>
    /// <param name="time">The moment, in scene seconds.</param>
    /// <param name="sceneTimeOfDay">The scene's own hour, for a key that sets none.</param>
    /// <returns>The camera.</returns>
    /// <exception cref="ArgumentException">There are no keys.</exception>
    public static CutscenePathPoint Evaluate(IReadOnlyList<CutsceneKey> keys, float timeScale, double time, float sceneTimeOfDay)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("A camera path needs a key.", nameof(keys));
        }

        // 0x4F5400 selects the last start <= the time, with no duration filter.
        Span<double> starts = keys.Count <= 256 ? stackalloc double[keys.Count] : new double[keys.Count];
        double at = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            starts[i] = at;
            at += Math.Max(0f, keys[i].Duration * timeScale);
        }

        int current = 0;
        for (int i = 1; i < keys.Count; i++)
        {
            if (starts[i] > time)
            {
                break;
            }

            current = i;
        }

        int i0 = Math.Max(0, current - 1), i1 = current, i2 = Math.Min(keys.Count - 1, current + 1), i3 = Math.Min(keys.Count - 1, current + 2);
        (CutsceneKey k0, double t0) = (keys[i0], starts[i0]);
        (CutsceneKey k1, double t1) = (keys[i1], starts[i1]);
        (CutsceneKey k2, double t2) = (keys[i2], starts[i2]);
        (CutsceneKey k3, double t3) = (keys[i3], starts[i3]);
        if (!k0.SmoothToNext)
        {
            (k0, t0) = (k1, t1);
        }

        if (!k1.SmoothToNext)
        {
            (k2, t2) = (k1, t1);
        }

        if (!k2.SmoothToNext)
        {
            (k3, t3) = (k2, t2);
        }

        double H(double v0, double v1, double v2, double v3) => Hermite(time, t0, t1, t2, t3, v0, v1, v2, v3);
        float Hour(CutsceneKey key) => key.TimeOfDay < 0f ? sceneTimeOfDay : key.TimeOfDay;
        Vector3d position = new(
            H(k0.Position.X, k1.Position.X, k2.Position.X, k3.Position.X),
            H(k0.Position.Y, k1.Position.Y, k2.Position.Y, k3.Position.Y),
            H(k0.Position.Z, k1.Position.Z, k2.Position.Z, k3.Position.Z));
        float alpha = (float)H(k0.Alpha, k1.Alpha, k2.Alpha, k3.Alpha);
        float beta = (float)H(k0.Beta, k1.Beta, k2.Beta, k3.Beta);
        float gamma = (float)H(k0.Gamma, k1.Gamma, k2.Gamma, k3.Gamma);
        return new CutscenePathPoint(
            position,
            Matrix3f.FromEngineEulerAngles(alpha, beta, gamma),
            (float)H(k0.FieldOfView, k1.FieldOfView, k2.FieldOfView, k3.FieldOfView),
            (float)H(Hour(k0), Hour(k1), Hour(k2), Hour(k3)),
            (float)H(k0.Noise, k1.Noise, k2.Noise, k3.Noise),
            (float)H(k0.Shake, k1.Shake, k2.Shake, k3.Shake),
            current,
            t1,
            t2,
            i2);
    }

    /// <summary>The camera's orientation as a view takes it, from the engine's storage of an <c>EditCamera</c>'s turn.</summary>
    /// <param name="orientation">The engine's orientation.</param>
    /// <returns>The view's.</returns>
    /// <remarks>
    /// <c>EditCamera</c> keeps a proper rotation (determinant +1), where a view basis is right, up, forward with determinant
    /// -1: its columns become the view's rows and the up row changes sign.
    /// </remarks>
    public static Matrix3f ToCameraOrientation(Matrix3f orientation)
    {
        Matrix3f axes = orientation.Transposed();
        return Matrix3f.FromRows(axes.Row1, -axes.Row2, axes.Row3);
    }

    /// <summary>The engine's storage of a camera's turn, from a view's orientation: <see cref="ToCameraOrientation"/> undone.</summary>
    /// <param name="camera">The view's orientation.</param>
    /// <returns>The engine's.</returns>
    public static Matrix3f FromCameraOrientation(Matrix3f camera) =>
        Matrix3f.FromRows(camera.Row1, -camera.Row2, camera.Row3).Transposed();

    /// <summary>The engine's turn of a camera looking from one point at another (<c>sub_508620</c>).</summary>
    /// <param name="from">Where it stands.</param>
    /// <param name="to">What it looks at.</param>
    /// <returns>The turn, or identity when the points meet.</returns>
    public static Matrix3f LookAt(Vector3d from, Vector3d to)
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

    /// <summary>The non-uniform cubic Hermite of <c>0x4F55D0</c> through four knots, at a time between the middle two.</summary>
    /// <param name="time">The time.</param>
    /// <param name="t0">The first knot's time.</param>
    /// <param name="t1">The second's, where the span starts.</param>
    /// <param name="t2">The third's, where it ends.</param>
    /// <param name="t3">The fourth's.</param>
    /// <param name="v0">The first knot's value.</param>
    /// <param name="v1">The second's.</param>
    /// <param name="v2">The third's.</param>
    /// <param name="v3">The fourth's.</param>
    /// <returns>The value.</returns>
    public static double Hermite(double time, double t0, double t1, double t2, double t3, double v0, double v1, double v2, double v3)
    {
        double s1 = t2 == t0 ? 0.0 : 2.0 * (t1 - t0) / (t2 - t0) * ((v2 - v0) * 0.5);
        double s2 = t3 == t1 ? 0.0 : 2.0 * (t2 - t1) / (t3 - t1) * ((v3 - v1) * 0.5);
        double u = t2 == t1 ? 0.0 : (time - t1) / (t2 - t1);
        double a = (2.0 * v1) - (2.0 * v2) + s2 + s1;
        double b = (3.0 * v2) - (3.0 * v1) - (2.0 * s1) - s2;
        return v1 + (u * u * u * a) + (u * u * b) + (u * s1);
    }

    /// <summary>The next of the shake's deterministic noise, from nought centred.</summary>
    /// <param name="value">The generator's state.</param>
    /// <returns>A value in [-0.5, 0.5].</returns>
    public static float Noise(ref uint value)
    {
        value = unchecked((value * 1664525u) + 1013904223u);
        return ((value >> 8) / 16777215f) - 0.5f;
    }

    /// <summary>Whether a filter is one of the band filters, whose colour settles half a second before a long span ends.</summary>
    /// <param name="filter">The filter's name.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsBandFilter(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.Contains("BAND", StringComparison.OrdinalIgnoreCase);
    }
}
