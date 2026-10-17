using System.Globalization;

namespace OpenIGI.Core.Numerics;

/// <summary>
/// A three-component vector of <see cref="double"/> values — the original engine's <c>Real64x3</c>
/// parameter type.
/// </summary>
/// <remarks>
/// World-space positions are double precision in the original engine. Storing a position in
/// <see cref="Vector3f"/> is a behaviour change (see the determinism contract in
/// <c>docs/architecture.md</c>), not an optimisation.
/// </remarks>
public readonly struct Vector3d : IEquatable<Vector3d>, IFormattable
{
    /// <summary>Initializes a new instance of the <see cref="Vector3d"/> struct.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    public Vector3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Initializes a new instance of the <see cref="Vector3d"/> struct with all components set to the same value.</summary>
    /// <param name="value">The value assigned to every component.</param>
    public Vector3d(double value)
        : this(value, value, value)
    {
    }

    /// <summary>Gets the X component.</summary>
    public double X { get; }

    /// <summary>Gets the Y component.</summary>
    public double Y { get; }

    /// <summary>Gets the Z component.</summary>
    public double Z { get; }

    /// <summary>Gets the vector whose components are all zero.</summary>
    public static Vector3d Zero => default;

    /// <summary>Gets the vector whose components are all one.</summary>
    public static Vector3d One => new(1.0, 1.0, 1.0);

    /// <summary>Gets the unit vector along the X axis.</summary>
    public static Vector3d UnitX => new(1.0, 0.0, 0.0);

    /// <summary>Gets the unit vector along the Y axis.</summary>
    public static Vector3d UnitY => new(0.0, 1.0, 0.0);

    /// <summary>Gets the unit vector along the Z axis.</summary>
    public static Vector3d UnitZ => new(0.0, 0.0, 1.0);

    /// <summary>Gets the euclidean length of this vector.</summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Gets the squared euclidean length of this vector.</summary>
    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    /// <summary>Adds two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector3d operator +(Vector3d left, Vector3d right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Subtracts one vector from another component-wise.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector3d operator -(Vector3d left, Vector3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Negates a vector.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>The negated vector.</returns>
    public static Vector3d operator -(Vector3d value) => new(-value.X, -value.Y, -value.Z);

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The scalar.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3d operator *(Vector3d left, double right) =>
        new(left.X * right, left.Y * right, left.Z * right);

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="left">The scalar.</param>
    /// <param name="right">The vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3d operator *(double left, Vector3d right) => right * left;

    /// <summary>Multiplies two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise product.</returns>
    public static Vector3d operator *(Vector3d left, Vector3d right) =>
        new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3d operator /(Vector3d left, double right) =>
        new(left.X / right, left.Y / right, left.Z / right);

    /// <summary>Determines whether two vectors are bit-for-bit equal.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns><see langword="true"/> if the vectors are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Vector3d left, Vector3d right) => left.Equals(right);

    /// <summary>Determines whether two vectors differ.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns><see langword="true"/> if the vectors differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Vector3d left, Vector3d right) => !left.Equals(right);

    /// <summary>Adds two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector3d Add(Vector3d left, Vector3d right) => left + right;

    /// <summary>Subtracts one vector from another component-wise.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector3d Subtract(Vector3d left, Vector3d right) => left - right;

    /// <summary>Multiplies two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise product.</returns>
    public static Vector3d Multiply(Vector3d left, Vector3d right) => left * right;

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The scalar.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3d Multiply(Vector3d left, double right) => left * right;

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3d Divide(Vector3d left, double right) => left / right;

    /// <summary>Negates a vector.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>The negated vector.</returns>
    public static Vector3d Negate(Vector3d value) => -value;

    /// <summary>Computes the dot product of two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(Vector3d left, Vector3d right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>The point on a segment nearest another point.</summary>
    /// <param name="start">One end of the segment.</param>
    /// <param name="end">The other.</param>
    /// <param name="point">The point.</param>
    /// <returns>The nearest point on the segment; <paramref name="start"/> when the segment has no length.</returns>
    public static Vector3d ClosestOnSegment(Vector3d start, Vector3d end, Vector3d point)
    {
        Vector3d along = end - start;
        double lengthSquared = along.LengthSquared;
        if (lengthSquared <= 0.0)
        {
            return start;
        }

        double t = Math.Clamp(Dot(point - start, along) / lengthSquared, 0.0, 1.0);
        return start + (along * t);
    }

    /// <summary>Computes the cross product of two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The cross product.</returns>
    public static Vector3d Cross(Vector3d left, Vector3d right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    /// <summary>Computes the distance between two points.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>The euclidean distance.</returns>
    public static double Distance(Vector3d left, Vector3d right) => (left - right).Length;

    /// <summary>Computes the squared distance between two points.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>The squared euclidean distance.</returns>
    public static double DistanceSquared(Vector3d left, Vector3d right) => (left - right).LengthSquared;

    /// <summary>Linearly interpolates between two vectors.</summary>
    /// <param name="start">The value returned at <paramref name="amount"/> = 0.</param>
    /// <param name="end">The value returned at <paramref name="amount"/> = 1.</param>
    /// <param name="amount">The interpolation factor; values outside [0, 1] extrapolate.</param>
    /// <returns>The interpolated vector.</returns>
    public static Vector3d Lerp(Vector3d start, Vector3d end, double amount) =>
        start + ((end - start) * amount);

    /// <summary>Returns this vector scaled to unit length.</summary>
    /// <returns>The normalized vector, or <see cref="Zero"/> when this vector has zero length.</returns>
    public Vector3d Normalized()
    {
        double length = Length;
        return length == 0.0 ? Zero : this / length;
    }

    /// <summary>Returns this vector with its components narrowed to <see cref="float"/>.</summary>
    /// <returns>The single-precision vector.</returns>
    public Vector3f ToVector3f() => new((float)X, (float)Y, (float)Z);

    /// <inheritdoc/>
    public bool Equals(Vector3d other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vector3d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.Create(
            formatProvider ?? CultureInfo.InvariantCulture,
            $"({X.ToString(format, formatProvider)}, {Y.ToString(format, formatProvider)}, {Z.ToString(format, formatProvider)})");
}
