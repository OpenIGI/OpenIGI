using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Orientations built the way the original builds them, bit for bit.</summary>
public static class RetailOrientation
{
    /// <summary>
    /// <c>0x4B38E0</c>: <c>Z(gamma) · Y(beta) · X(alpha)</c> with x87 arithmetic, each product's entries stored as floats,
    /// and <c>fsin</c>/<c>fcos</c> rounded toward zero.
    /// </summary>
    /// <param name="alpha">The angle about X.</param>
    /// <param name="beta">The angle about Y.</param>
    /// <param name="gamma">The angle about Z.</param>
    /// <returns>The nine entries, row-major.</returns>
    public static float[] Euler(float alpha, float beta, float gamma)
    {
        float[] m = new float[9];
        RetailPlaneBody.Euler(m, alpha, beta, gamma);
        return m;
    }

    /// <summary>The same rotation as a <see cref="Matrix3f"/>.</summary>
    /// <param name="alpha">The angle about X.</param>
    /// <param name="beta">The angle about Y.</param>
    /// <param name="gamma">The angle about Z.</param>
    /// <returns>The rotation.</returns>
    public static Matrix3f EulerMatrix(float alpha, float beta, float gamma) => ToMatrix(Euler(alpha, beta, gamma));

    /// <summary>A row-major array as a <see cref="Matrix3f"/>.</summary>
    /// <param name="m">The nine entries.</param>
    /// <returns>The matrix.</returns>
    public static Matrix3f ToMatrix(float[] m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
    }
}
