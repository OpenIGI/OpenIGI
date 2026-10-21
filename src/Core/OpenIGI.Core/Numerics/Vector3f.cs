using System.Globalization;

namespace OpenIGI.Core.Numerics;

/// <summary>
/// A three-component vector of <see cref="float"/> values — the original engine's <c>Real32x3</c>
/// parameter type.
/// </summary>
/// <remarks>
/// Used for directions, colours, scales and other quantities the original kept in single precision.
/// World-space positions use <see cref="Vector3d"/> instead.
/// </remarks>
public readonly struct Vector3f : IEquatable<Vector3f>, IFormattable
{
    /// <summary>Initializes a new instance of the <see cref="Vector3f"/> struct.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    public Vector3f(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Initializes a new instance of the <see cref="Vector3f"/> struct with all components set to the same value.</summary>
    /// <param name="value">The value assigned to every component.</param>
    public Vector3f(float value)
        : this(value, value, value)
    {
    }

    /// <summary>Gets the X component.</summary>
    public float X { get; }

    /// <summary>Gets the Y component.</summary>
    public float Y { get; }

    /// <summary>Gets the Z component.</summary>
    public float Z { get; }

    /// <summary>Gets the vector whose components are all zero.</summary>
    public static Vector3f Zero => default;

    /// <summary>Gets the vector whose components are all one.</summary>
    public static Vector3f One => new(1f, 1f, 1f);

    /// <summary>Gets the unit vector along the X axis.</summary>
    public static Vector3f UnitX => new(1f, 0f, 0f);

    /// <summary>Gets the unit vector along the Y axis.</summary>
    public static Vector3f UnitY => new(0f, 1f, 0f);

    /// <summary>Gets the unit vector along the Z axis.</summary>
    public static Vector3f UnitZ => new(0f, 0f, 1f);

    /// <summary>Gets the euclidean length of this vector.</summary>
    public float Length => MathF.Sqrt(LengthSquared);

    /// <summary>Gets the squared euclidean length of this vector.</summary>
    public float LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    /// <summary>Adds two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector3f operator +(Vector3f left, Vector3f right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Subtracts one vector from another component-wise.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector3f operator -(Vector3f left, Vector3f right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Negates a vector.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>The negated vector.</returns>
    public static Vector3f operator -(Vector3f value) => new(-value.X, -value.Y, -value.Z);

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The scalar.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3f operator *(Vector3f left, float right) =>
        new(left.X * right, left.Y * right, left.Z * right);

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="left">The scalar.</param>
    /// <param name="right">The vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3f operator *(float left, Vector3f right) => right * left;

    /// <summary>Multiplies two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise product.</returns>
    public static Vector3f operator *(Vector3f left, Vector3f right) =>
        new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3f operator /(Vector3f left, float right) =>
        new(left.X / right, left.Y / right, left.Z / right);

    /// <summary>Determines whether two vectors are bit-for-bit equal.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns><see langword="true"/> if the vectors are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Vector3f left, Vector3f right) => left.Equals(right);

    /// <summary>Determines whether two vectors differ.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns><see langword="true"/> if the vectors differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Vector3f left, Vector3f right) => !left.Equals(right);

    /// <summary>Adds two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector3f Add(Vector3f left, Vector3f right) => left + right;

    /// <summary>Subtracts one vector from another component-wise.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector3f Subtract(Vector3f left, Vector3f right) => left - right;

    /// <summary>Multiplies two vectors component-wise.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise product.</returns>
    public static Vector3f Multiply(Vector3f left, Vector3f right) => left * right;

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The scalar.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3f Multiply(Vector3f left, float right) => left * right;

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3f Divide(Vector3f left, float right) => left / right;

    /// <summary>Negates a vector.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>The negated vector.</returns>
    public static Vector3f Negate(Vector3f value) => -value;

    /// <summary>Computes the dot product of two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The dot product.</returns>
    public static float Dot(Vector3f left, Vector3f right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>Computes the cross product of two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The cross product.</returns>
    public static Vector3f Cross(Vector3f left, Vector3f right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    /// <summary>Computes the distance between two points.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>The euclidean distance.</returns>
    public static float Distance(Vector3f left, Vector3f right) => (left - right).Length;

    /// <summary>Computes the squared distance between two points.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>The squared euclidean distance.</returns>
    public static float DistanceSquared(Vector3f left, Vector3f right) => (left - right).LengthSquared;

    /// <summary>Linearly interpolates between two vectors.</summary>
    /// <param name="start">The value returned at <paramref name="amount"/> = 0.</param>
    /// <param name="end">The value returned at <paramref name="amount"/> = 1.</param>
    /// <param name="amount">The interpolation factor; values outside [0, 1] extrapolate.</param>
    /// <returns>The interpolated vector.</returns>
    public static Vector3f Lerp(Vector3f start, Vector3f end, float amount) =>
        start + ((end - start) * amount);

    /// <summary>Returns this vector scaled to unit length.</summary>
    /// <returns>The normalized vector, or <see cref="Zero"/> when this vector has zero length.</returns>
    public Vector3f Normalized()
    {
        float length = Length;
        return length == 0f ? Zero : this / length;
    }

    /// <summary>Returns this vector divided by its length, or a fallback when it is no longer than a minimum.</summary>
    /// <param name="fallback">What a vector too short to normalise, or not a number, becomes.</param>
    /// <param name="minimumLength">The length the vector must exceed to be divided.</param>
    /// <returns>The unit vector, or <paramref name="fallback"/>.</returns>
    /// <remarks>
    /// Each component is divided by the length, which rounds differently from multiplying by its reciprocal; see
    /// <see cref="ReciprocalNormalizedOr"/> for the code that does that instead. The test is <c>length &gt; minimumLength</c>,
    /// so a length that is not a number takes the fallback.
    /// </remarks>
    public Vector3f NormalizedOr(Vector3f fallback, float minimumLength = 0f)
    {
        float length = Length;
        return length > minimumLength ? this / length : fallback;
    }

    /// <summary>Returns this vector divided by its length, or itself unchanged when it is no longer than a minimum.</summary>
    /// <param name="minimumLength">The length the vector must exceed to be divided.</param>
    /// <returns>The unit vector, or this vector.</returns>
    public Vector3f NormalizedOrSelf(float minimumLength = 0f) => NormalizedOr(this, minimumLength);

    /// <summary>Returns this vector scaled by the reciprocal of its length, or a fallback when it is no longer than a minimum.</summary>
    /// <param name="fallback">What a vector too short to normalise, or not a number, becomes.</param>
    /// <param name="minimumLength">The length the vector must exceed to be scaled.</param>
    /// <returns>The unit vector, or <paramref name="fallback"/>.</returns>
    /// <remarks>
    /// <c>1 / length</c> once, then a multiply per component: the form a fixed-function engine writes, and not bit-identical
    /// to <see cref="NormalizedOr"/>.
    /// </remarks>
    public Vector3f ReciprocalNormalizedOr(Vector3f fallback, float minimumLength = 0f)
    {
        float length = Length;
        return length > minimumLength ? this * (1f / length) : fallback;
    }

    /// <summary>Returns this vector scaled by the reciprocal of its length, or itself unchanged when it is no longer than a minimum.</summary>
    /// <param name="minimumLength">The length the vector must exceed to be scaled.</param>
    /// <returns>The unit vector, or this vector.</returns>
    public Vector3f ReciprocalNormalizedOrSelf(float minimumLength = 0f) => ReciprocalNormalizedOr(this, minimumLength);

    /// <summary>Returns this vector with its components widened to <see cref="double"/>.</summary>
    /// <returns>The double-precision vector.</returns>
    public Vector3d ToVector3d() => new(X, Y, Z);

    /// <inheritdoc/>
    public bool Equals(Vector3f other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vector3f other && Equals(other);

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
