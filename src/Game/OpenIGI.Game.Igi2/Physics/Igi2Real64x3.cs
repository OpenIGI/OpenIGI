using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Physics;

/// <summary>The client's distance between two world points, <c>Real64x3_Distance</c> (<c>igi2 0x5624C0</c>), and its square.</summary>
/// <remarks>
/// The first point less the second, the squares summed z, y, x, on the client's 24-bit FPU. A door's portal test inlines the
/// square (<c>Door_RunHandler</c>), which is why it has a name of its own.
/// </remarks>
public static class Igi2Real64x3
{
    /// <summary>The squared distance, summed z, y, x.</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The squared distance, unstored.</returns>
    public static Fpu DistanceSquared(Vector3d a, Vector3d b)
    {
        Fpu dx = (Fpu)a.X - b.X, dy = (Fpu)a.Y - b.Y, dz = (Fpu)a.Z - b.Z;
        return (dz * dz) + (dy * dy) + (dx * dx);
    }

    /// <summary>The distance, <c>Real64x3_Distance</c>.</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>The distance, unstored.</returns>
    public static Fpu Distance(Vector3d a, Vector3d b) => Fpu.Sqrt(DistanceSquared(a, b));
}
