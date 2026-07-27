using System.Globalization;

namespace OpenIGI.Core.Numerics;

/// <summary>
/// A 3x3 matrix of <see cref="float"/> values — the original engine's <c>Real32x9</c> parameter type,
/// used for object orientation.
/// </summary>
/// <remarks>
/// <para>
/// Storage is row-major: the nine values appear in the order
/// <c>M11 M12 M13 M21 M22 M23 M31 M32 M33</c>, matching the layout of a <c>Real32x9</c> script value
/// and of the nine consecutive floats found in the original's data files. Rows are treated as the
/// basis vectors of the oriented frame, so <see cref="Transform(Vector3f)"/> computes
/// <c>row-vector x matrix</c>.
/// </para>
/// <para>
/// The row-vector convention is the one used by the original's D3D7 renderer. It is re-verified when
/// real model and animation data is loaded (milestones M6/M8); a mismatch there is a bug here, not in
/// the caller.
/// </para>
/// </remarks>
public readonly struct Matrix3f : IEquatable<Matrix3f>, IFormattable
{
    /// <summary>The number of <see cref="float"/> components in a <see cref="Matrix3f"/>.</summary>
    public const int ComponentCount = 9;

    /// <summary>Initializes a new instance of the <see cref="Matrix3f"/> struct from its nine components.</summary>
    /// <param name="m11">Row 1, column 1.</param>
    /// <param name="m12">Row 1, column 2.</param>
    /// <param name="m13">Row 1, column 3.</param>
    /// <param name="m21">Row 2, column 1.</param>
    /// <param name="m22">Row 2, column 2.</param>
    /// <param name="m23">Row 2, column 3.</param>
    /// <param name="m31">Row 3, column 1.</param>
    /// <param name="m32">Row 3, column 2.</param>
    /// <param name="m33">Row 3, column 3.</param>
    public Matrix3f(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
    {
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M31 = m31;
        M32 = m32;
        M33 = m33;
    }

    /// <summary>Gets the component at row 1, column 1.</summary>
    public float M11 { get; }

    /// <summary>Gets the component at row 1, column 2.</summary>
    public float M12 { get; }

    /// <summary>Gets the component at row 1, column 3.</summary>
    public float M13 { get; }

    /// <summary>Gets the component at row 2, column 1.</summary>
    public float M21 { get; }

    /// <summary>Gets the component at row 2, column 2.</summary>
    public float M22 { get; }

    /// <summary>Gets the component at row 2, column 3.</summary>
    public float M23 { get; }

    /// <summary>Gets the component at row 3, column 1.</summary>
    public float M31 { get; }

    /// <summary>Gets the component at row 3, column 2.</summary>
    public float M32 { get; }

    /// <summary>Gets the component at row 3, column 3.</summary>
    public float M33 { get; }

    /// <summary>Gets the identity matrix.</summary>
    public static Matrix3f Identity => new(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);

    /// <summary>Gets the first row.</summary>
    public Vector3f Row1 => new(M11, M12, M13);

    /// <summary>Gets the second row.</summary>
    public Vector3f Row2 => new(M21, M22, M23);

    /// <summary>Gets the third row.</summary>
    public Vector3f Row3 => new(M31, M32, M33);

    /// <summary>Gets the first column.</summary>
    public Vector3f Column1 => new(M11, M21, M31);

    /// <summary>Gets the second column.</summary>
    public Vector3f Column2 => new(M12, M22, M32);

    /// <summary>Gets the third column.</summary>
    public Vector3f Column3 => new(M13, M23, M33);

    /// <summary>Gets the determinant of this matrix.</summary>
    public float Determinant =>
        (M11 * ((M22 * M33) - (M23 * M32)))
        - (M12 * ((M21 * M33) - (M23 * M31)))
        + (M13 * ((M21 * M32) - (M22 * M31)));

    /// <summary>Gets the component at the given row and column.</summary>
    /// <param name="row">The zero-based row index.</param>
    /// <param name="column">The zero-based column index.</param>
    /// <returns>The component value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> or <paramref name="column"/> is outside [0, 2].</exception>
    public float this[int row, int column] => (row, column) switch
    {
        (0, 0) => M11,
        (0, 1) => M12,
        (0, 2) => M13,
        (1, 0) => M21,
        (1, 1) => M22,
        (1, 2) => M23,
        (2, 0) => M31,
        (2, 1) => M32,
        (2, 2) => M33,
        (< 0 or > 2, _) => throw new ArgumentOutOfRangeException(nameof(row), row, "Row must be in [0, 2]."),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Column must be in [0, 2]."),
    };

    /// <summary>Builds a matrix from three row vectors.</summary>
    /// <param name="row1">The first row.</param>
    /// <param name="row2">The second row.</param>
    /// <param name="row3">The third row.</param>
    /// <returns>The assembled matrix.</returns>
    public static Matrix3f FromRows(Vector3f row1, Vector3f row2, Vector3f row3) =>
        new(row1.X, row1.Y, row1.Z, row2.X, row2.Y, row2.Z, row3.X, row3.Y, row3.Z);

    /// <summary>Reads a matrix from nine consecutive floats in row-major order.</summary>
    /// <param name="values">The source values; must contain at least <see cref="ComponentCount"/> elements.</param>
    /// <returns>The assembled matrix.</returns>
    /// <exception cref="ArgumentException"><paramref name="values"/> contains fewer than <see cref="ComponentCount"/> elements.</exception>
    public static Matrix3f FromRowMajor(ReadOnlySpan<float> values)
    {
        if (values.Length < ComponentCount)
        {
            throw new ArgumentException($"A Matrix3f needs {ComponentCount} components.", nameof(values));
        }

        return new Matrix3f(
            values[0], values[1], values[2],
            values[3], values[4], values[5],
            values[6], values[7], values[8]);
    }

    /// <summary>Multiplies two matrices.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product <c>left * right</c>.</returns>
    public static Matrix3f operator *(Matrix3f left, Matrix3f right) =>
        new(
            (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31),
            (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32),
            (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
            (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31),
            (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32),
            (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
            (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31),
            (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32),
            (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));

    /// <summary>Determines whether two matrices are bit-for-bit equal.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> if the matrices are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Matrix3f left, Matrix3f right) => left.Equals(right);

    /// <summary>Determines whether two matrices differ.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> if the matrices differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Matrix3f left, Matrix3f right) => !left.Equals(right);

    /// <summary>Multiplies two matrices.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product <c>left * right</c>.</returns>
    public static Matrix3f Multiply(Matrix3f left, Matrix3f right) => left * right;

    /// <summary>Transforms a vector by this matrix, treating it as a row vector.</summary>
    /// <param name="value">The vector to transform.</param>
    /// <returns>The transformed vector.</returns>
    public Vector3f Transform(Vector3f value) =>
        new(
            (value.X * M11) + (value.Y * M21) + (value.Z * M31),
            (value.X * M12) + (value.Y * M22) + (value.Z * M32),
            (value.X * M13) + (value.Y * M23) + (value.Z * M33));

    /// <summary>Transforms a double-precision vector by this matrix, treating it as a row vector.</summary>
    /// <param name="value">The vector to transform.</param>
    /// <returns>The transformed vector.</returns>
    /// <remarks>
    /// World positions stay in double precision through the multiply; only the matrix components are
    /// single precision, matching the original's mixed-precision transform of <c>Real64x3</c> positions
    /// by a <c>Real32x9</c> orientation.
    /// </remarks>
    public Vector3d Transform(Vector3d value) =>
        new(
            (value.X * M11) + (value.Y * M21) + (value.Z * M31),
            (value.X * M12) + (value.Y * M22) + (value.Z * M32),
            (value.X * M13) + (value.Y * M23) + (value.Z * M33));

    /// <summary>Returns the transpose of this matrix.</summary>
    /// <returns>The transposed matrix.</returns>
    /// <remarks>For an orthonormal orientation matrix this is also its inverse.</remarks>
    public Matrix3f Transposed() => new(M11, M21, M31, M12, M22, M32, M13, M23, M33);

    /// <summary>Writes the nine components into <paramref name="destination"/> in row-major order.</summary>
    /// <param name="destination">The span to write to; must hold at least <see cref="ComponentCount"/> elements.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public void CopyTo(Span<float> destination)
    {
        if (destination.Length < ComponentCount)
        {
            throw new ArgumentException($"A Matrix3f needs {ComponentCount} components.", nameof(destination));
        }

        destination[0] = M11;
        destination[1] = M12;
        destination[2] = M13;
        destination[3] = M21;
        destination[4] = M22;
        destination[5] = M23;
        destination[6] = M31;
        destination[7] = M32;
        destination[8] = M33;
    }

    /// <inheritdoc/>
    public bool Equals(Matrix3f other) =>
        M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13)
        && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23)
        && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Matrix3f other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(
            HashCode.Combine(M11, M12, M13),
            HashCode.Combine(M21, M22, M23),
            HashCode.Combine(M31, M32, M33));

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.Create(
            formatProvider ?? CultureInfo.InvariantCulture,
            $"[{Row1.ToString(format, formatProvider)} {Row2.ToString(format, formatProvider)} {Row3.ToString(format, formatProvider)}]");
}
