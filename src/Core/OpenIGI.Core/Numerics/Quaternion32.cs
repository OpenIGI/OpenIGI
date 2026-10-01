using System.Globalization;

namespace OpenIGI.Core.Numerics;

/// <summary>
/// A single-precision quaternion — the original engine's <c>Quat32</c> parameter type.
/// </summary>
/// <remarks>
/// Script values of this type are four consecutive <see cref="float"/>s in the order
/// <c>X Y Z W</c>, with <c>W</c> the scalar part.
/// </remarks>
public readonly struct Quaternion32 : IEquatable<Quaternion32>, IFormattable
{
    /// <summary>The number of <see cref="float"/> components in a <see cref="Quaternion32"/>.</summary>
    public const int ComponentCount = 4;

    /// <summary>Initializes a new instance of the <see cref="Quaternion32"/> struct.</summary>
    /// <param name="x">The X component of the vector part.</param>
    /// <param name="y">The Y component of the vector part.</param>
    /// <param name="z">The Z component of the vector part.</param>
    /// <param name="w">The scalar part.</param>
    public Quaternion32(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Initializes a new instance of the <see cref="Quaternion32"/> struct from a vector and a scalar part.</summary>
    /// <param name="vector">The vector part.</param>
    /// <param name="scalar">The scalar part.</param>
    public Quaternion32(Vector3f vector, float scalar)
        : this(vector.X, vector.Y, vector.Z, scalar)
    {
    }

    /// <summary>Gets the X component of the vector part.</summary>
    public float X { get; }

    /// <summary>Gets the Y component of the vector part.</summary>
    public float Y { get; }

    /// <summary>Gets the Z component of the vector part.</summary>
    public float Z { get; }

    /// <summary>Gets the scalar part.</summary>
    public float W { get; }

    /// <summary>Gets the identity rotation.</summary>
    public static Quaternion32 Identity => new(0f, 0f, 0f, 1f);

    /// <summary>Gets the vector part.</summary>
    public Vector3f Vector => new(X, Y, Z);

    /// <summary>Gets the length of this quaternion.</summary>
    public float Length => MathF.Sqrt(LengthSquared);

    /// <summary>Gets the squared length of this quaternion.</summary>
    public float LengthSquared => (X * X) + (Y * Y) + (Z * Z) + (W * W);

    /// <summary>Composes two rotations.</summary>
    /// <param name="left">The rotation applied first.</param>
    /// <param name="right">The rotation applied second.</param>
    /// <returns>The Hamilton product <c>left * right</c>.</returns>
    public static Quaternion32 operator *(Quaternion32 left, Quaternion32 right) =>
        new(
            (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
            (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
            (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W),
            (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z));

    /// <summary>Determines whether two quaternions are bit-for-bit equal.</summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns><see langword="true"/> if the quaternions are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Quaternion32 left, Quaternion32 right) => left.Equals(right);

    /// <summary>Determines whether two quaternions differ.</summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns><see langword="true"/> if the quaternions differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Quaternion32 left, Quaternion32 right) => !left.Equals(right);

    /// <summary>Composes two rotations.</summary>
    /// <param name="left">The rotation applied first.</param>
    /// <param name="right">The rotation applied second.</param>
    /// <returns>The Hamilton product <c>left * right</c>.</returns>
    public static Quaternion32 Multiply(Quaternion32 left, Quaternion32 right) => left * right;

    /// <summary>Creates a rotation about an axis.</summary>
    /// <param name="axis">The rotation axis; it is normalized internally.</param>
    /// <param name="radians">The rotation angle in radians.</param>
    /// <returns>The rotation quaternion.</returns>
    public static Quaternion32 FromAxisAngle(Vector3f axis, float radians)
    {
        Vector3f unit = axis.Normalized();
        float half = radians * 0.5f;
        float sin = MathF.Sin(half);
        return new Quaternion32(unit.X * sin, unit.Y * sin, unit.Z * sin, MathF.Cos(half));
    }

    /// <summary>Computes the dot product of two quaternions.</summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns>The dot product.</returns>
    public static float Dot(Quaternion32 left, Quaternion32 right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z) + (left.W * right.W);

    /// <summary>Returns this quaternion scaled to unit length.</summary>
    /// <returns>The normalized quaternion, or <see cref="Identity"/> when this quaternion has zero length.</returns>
    public Quaternion32 Normalized()
    {
        float length = Length;
        if (length == 0f)
        {
            return Identity;
        }

        float inverse = 1f / length;
        return new Quaternion32(X * inverse, Y * inverse, Z * inverse, W * inverse);
    }

    /// <summary>Returns the conjugate of this quaternion.</summary>
    /// <returns>The conjugate, which is the inverse rotation for a unit quaternion.</returns>
    public Quaternion32 Conjugate() => new(-X, -Y, -Z, W);

    /// <summary>Converts this quaternion into an equivalent row-vector orientation matrix.</summary>
    /// <returns>The rotation matrix.</returns>
    /// <remarks>The quaternion is normalized first, so a denormalized input still yields a pure rotation.</remarks>
    public Matrix3f ToMatrix()
    {
        Quaternion32 q = Normalized();
        float xx = q.X * q.X;
        float yy = q.Y * q.Y;
        float zz = q.Z * q.Z;
        float xy = q.X * q.Y;
        float xz = q.X * q.Z;
        float yz = q.Y * q.Z;
        float wx = q.W * q.X;
        float wy = q.W * q.Y;
        float wz = q.W * q.Z;

        return new Matrix3f(
            1f - (2f * (yy + zz)), 2f * (xy + wz), 2f * (xz - wy),
            2f * (xy - wz), 1f - (2f * (xx + zz)), 2f * (yz + wx),
            2f * (xz + wy), 2f * (yz - wx), 1f - (2f * (xx + yy)));
    }

    /// <summary>Rotates a vector by this quaternion.</summary>
    /// <param name="value">The vector to rotate.</param>
    /// <returns>The rotated vector.</returns>
    public Vector3f Rotate(Vector3f value) => ToMatrix().Transform(value);

    /// <inheritdoc/>
    public bool Equals(Quaternion32 other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Quaternion32 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.Create(
            formatProvider ?? CultureInfo.InvariantCulture,
            $"({X.ToString(format, formatProvider)}, {Y.ToString(format, formatProvider)}, {Z.ToString(format, formatProvider)}, {W.ToString(format, formatProvider)})");
}
