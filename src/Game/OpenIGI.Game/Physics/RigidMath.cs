using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>A double-precision 3×3 matrix acting on column vectors, for rigid-body state.</summary>
/// <remarks>
/// Rigid bodies integrate orientation and inertia every substep for seconds at a time; doing that in
/// <see cref="Matrix3f"/> drifts visibly. <c>M[r,c]</c>, and <see cref="Transform"/> is <c>M · v</c>.
/// </remarks>
public readonly record struct Matrix3d(
    double M11,
    double M12,
    double M13,
    double M21,
    double M22,
    double M23,
    double M31,
    double M32,
    double M33)
{
    /// <summary>Gets the identity.</summary>
    public static Matrix3d Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>Gets a diagonal matrix.</summary>
    public static Matrix3d Diagonal(double x, double y, double z) => new(x, 0, 0, 0, y, 0, 0, 0, z);

    /// <summary>Multiplies a column vector.</summary>
    public Vector3d Transform(Vector3d v) => new(
        (M11 * v.X) + (M12 * v.Y) + (M13 * v.Z),
        (M21 * v.X) + (M22 * v.Y) + (M23 * v.Z),
        (M31 * v.X) + (M32 * v.Y) + (M33 * v.Z));

    /// <summary>Multiplies a column vector by the transpose.</summary>
    public Vector3d TransformTransposed(Vector3d v) => new(
        (M11 * v.X) + (M21 * v.Y) + (M31 * v.Z),
        (M12 * v.X) + (M22 * v.Y) + (M32 * v.Z),
        (M13 * v.X) + (M23 * v.Y) + (M33 * v.Z));

    /// <summary>Gets the transpose.</summary>
    public Matrix3d Transposed() => new(M11, M21, M31, M12, M22, M32, M13, M23, M33);

    /// <summary>Scales every element.</summary>
    public static Matrix3d operator *(Matrix3d m, double s) =>
        new(m.M11 * s, m.M12 * s, m.M13 * s, m.M21 * s, m.M22 * s, m.M23 * s, m.M31 * s, m.M32 * s, m.M33 * s);

    /// <summary>Multiplies two matrices.</summary>
    public static Matrix3d operator *(Matrix3d a, Matrix3d b) => new(
        (a.M11 * b.M11) + (a.M12 * b.M21) + (a.M13 * b.M31),
        (a.M11 * b.M12) + (a.M12 * b.M22) + (a.M13 * b.M32),
        (a.M11 * b.M13) + (a.M12 * b.M23) + (a.M13 * b.M33),
        (a.M21 * b.M11) + (a.M22 * b.M21) + (a.M23 * b.M31),
        (a.M21 * b.M12) + (a.M22 * b.M22) + (a.M23 * b.M32),
        (a.M21 * b.M13) + (a.M22 * b.M23) + (a.M23 * b.M33),
        (a.M31 * b.M11) + (a.M32 * b.M21) + (a.M33 * b.M31),
        (a.M31 * b.M12) + (a.M32 * b.M22) + (a.M33 * b.M32),
        (a.M31 * b.M13) + (a.M32 * b.M23) + (a.M33 * b.M33));

    /// <summary>Inverts the matrix; a singular one returns the zero matrix.</summary>
    public Matrix3d Inverted()
    {
        double c11 = (M22 * M33) - (M23 * M32);
        double c12 = (M23 * M31) - (M21 * M33);
        double c13 = (M21 * M32) - (M22 * M31);
        double determinant = (M11 * c11) + (M12 * c12) + (M13 * c13);
        if (Math.Abs(determinant) < 1e-300)
        {
            return default;
        }

        double inverse = 1.0 / determinant;
        return new Matrix3d(
            c11 * inverse,
            ((M13 * M32) - (M12 * M33)) * inverse,
            ((M12 * M23) - (M13 * M22)) * inverse,
            c12 * inverse,
            ((M11 * M33) - (M13 * M31)) * inverse,
            ((M13 * M21) - (M11 * M23)) * inverse,
            c13 * inverse,
            ((M12 * M31) - (M11 * M32)) * inverse,
            ((M11 * M22) - (M12 * M21)) * inverse);
    }

    /// <summary>Reads a <see cref="Matrix3f"/> whose rows, read as a row-vector transform, map local to world.</summary>
    /// <remarks>
    /// The port's orientations are applied as <c>orientation.Transposed().Transform(v)</c>, which is
    /// <c>O · v</c> for column vectors, so the element layout carries over unchanged.
    /// </remarks>
    public static Matrix3d From(Matrix3f m) => new(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33);

    /// <summary>Narrows back to the port's single-precision orientation.</summary>
    public Matrix3f ToMatrix3f() => new(
        (float)M11, (float)M12, (float)M13,
        (float)M21, (float)M22, (float)M23,
        (float)M31, (float)M32, (float)M33);
}

/// <summary>A double-precision unit quaternion for rigid-body orientation.</summary>
public readonly record struct QuaternionD(double W, double X, double Y, double Z)
{
    /// <summary>Gets the identity rotation.</summary>
    public static QuaternionD Identity => new(1, 0, 0, 0);

    /// <summary>Renormalises, which integration needs every step.</summary>
    public QuaternionD Normalized()
    {
        double length = Math.Sqrt((W * W) + (X * X) + (Y * Y) + (Z * Z));
        return length > 0.0 ? new QuaternionD(W / length, X / length, Y / length, Z / length) : Identity;
    }

    /// <summary>Advances by an angular velocity over a time step: <c>q += ½ ω q dt</c>, renormalised.</summary>
    /// <param name="omega">World angular velocity, radians per tick.</param>
    /// <param name="dt">The step, in ticks.</param>
    public QuaternionD Integrate(Vector3d omega, double dt)
    {
        double h = 0.5 * dt;
        double w = W + (h * ((-omega.X * X) - (omega.Y * Y) - (omega.Z * Z)));
        double x = X + (h * ((omega.X * W) + (omega.Y * Z) - (omega.Z * Y)));
        double y = Y + (h * ((omega.Y * W) + (omega.Z * X) - (omega.X * Z)));
        double z = Z + (h * ((omega.Z * W) + (omega.X * Y) - (omega.Y * X)));
        return new QuaternionD(w, x, y, z).Normalized();
    }

    /// <summary>Converts to a rotation matrix acting on column vectors.</summary>
    public Matrix3d ToMatrix()
    {
        double xx = X * X, yy = Y * Y, zz = Z * Z;
        double xy = X * Y, xz = X * Z, yz = Y * Z;
        double wx = W * X, wy = W * Y, wz = W * Z;
        return new Matrix3d(
            1 - (2 * (yy + zz)), 2 * (xy - wz), 2 * (xz + wy),
            2 * (xy + wz), 1 - (2 * (xx + zz)), 2 * (yz - wx),
            2 * (xz - wy), 2 * (yz + wx), 1 - (2 * (xx + yy)));
    }

    /// <summary>Converts a rotation matrix acting on column vectors.</summary>
    public static QuaternionD FromMatrix(Matrix3d m)
    {
        double trace = m.M11 + m.M22 + m.M33;
        QuaternionD q;
        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;
            q = new QuaternionD(0.25 * s, (m.M32 - m.M23) / s, (m.M13 - m.M31) / s, (m.M21 - m.M12) / s);
        }
        else if (m.M11 > m.M22 && m.M11 > m.M33)
        {
            double s = Math.Sqrt(1.0 + m.M11 - m.M22 - m.M33) * 2.0;
            q = new QuaternionD((m.M32 - m.M23) / s, 0.25 * s, (m.M12 + m.M21) / s, (m.M13 + m.M31) / s);
        }
        else if (m.M22 > m.M33)
        {
            double s = Math.Sqrt(1.0 + m.M22 - m.M11 - m.M33) * 2.0;
            q = new QuaternionD((m.M13 - m.M31) / s, (m.M12 + m.M21) / s, 0.25 * s, (m.M23 + m.M32) / s);
        }
        else
        {
            double s = Math.Sqrt(1.0 + m.M33 - m.M11 - m.M22) * 2.0;
            q = new QuaternionD((m.M21 - m.M12) / s, (m.M13 + m.M31) / s, (m.M23 + m.M32) / s, 0.25 * s);
        }

        return q.Normalized();
    }
}
