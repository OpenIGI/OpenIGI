using System.Globalization;

namespace OpenIGI.Core.Numerics;

/// <summary>
/// A 4×4 single-precision matrix, used for the view and projection transforms of the 3D pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The original has no 4×4 matrix type: it keeps orientation in a <see cref="Matrix3f"/>
/// (<c>Real32x9</c>), the camera position in a <see cref="Vector3d"/> (<c>Real64x3</c>), and applies the
/// projection as scalar arithmetic — <c>sx = x/z · xScale + cx</c> — while transforming each vertex
/// (<c>sub_512C30</c>). This type packs that same arithmetic into one matrix so a GPU can do it, which
/// is a representation change, not a behaviour change.
/// </para>
/// <para>
/// <b>Conventions.</b> Elements are named <c>MrowColumn</c> and stored row-major. Vectors are columns,
/// so a transform is <c>M · v</c> and composition reads right-to-left: <c>projection * view * world</c>.
/// <see cref="CopyColumnMajorTo"/> writes the transpose, which is the layout OpenGL uniforms want.
/// </para>
/// </remarks>
public readonly struct Matrix4f : IEquatable<Matrix4f>, IFormattable
{
    /// <summary>The number of components in a matrix.</summary>
    public const int ComponentCount = 16;

    /// <summary>Initializes a new instance of the <see cref="Matrix4f"/> struct from its elements in row-major order.</summary>
    /// <param name="m11">Row 1, column 1.</param>
    /// <param name="m12">Row 1, column 2.</param>
    /// <param name="m13">Row 1, column 3.</param>
    /// <param name="m14">Row 1, column 4.</param>
    /// <param name="m21">Row 2, column 1.</param>
    /// <param name="m22">Row 2, column 2.</param>
    /// <param name="m23">Row 2, column 3.</param>
    /// <param name="m24">Row 2, column 4.</param>
    /// <param name="m31">Row 3, column 1.</param>
    /// <param name="m32">Row 3, column 2.</param>
    /// <param name="m33">Row 3, column 3.</param>
    /// <param name="m34">Row 3, column 4.</param>
    /// <param name="m41">Row 4, column 1.</param>
    /// <param name="m42">Row 4, column 2.</param>
    /// <param name="m43">Row 4, column 3.</param>
    /// <param name="m44">Row 4, column 4.</param>
    public Matrix4f(
        float m11, float m12, float m13, float m14,
        float m21, float m22, float m23, float m24,
        float m31, float m32, float m33, float m34,
        float m41, float m42, float m43, float m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }

    /// <summary>Gets row 1, column 1.</summary>
    public float M11 { get; }

    /// <summary>Gets row 1, column 2.</summary>
    public float M12 { get; }

    /// <summary>Gets row 1, column 3.</summary>
    public float M13 { get; }

    /// <summary>Gets row 1, column 4.</summary>
    public float M14 { get; }

    /// <summary>Gets row 2, column 1.</summary>
    public float M21 { get; }

    /// <summary>Gets row 2, column 2.</summary>
    public float M22 { get; }

    /// <summary>Gets row 2, column 3.</summary>
    public float M23 { get; }

    /// <summary>Gets row 2, column 4.</summary>
    public float M24 { get; }

    /// <summary>Gets row 3, column 1.</summary>
    public float M31 { get; }

    /// <summary>Gets row 3, column 2.</summary>
    public float M32 { get; }

    /// <summary>Gets row 3, column 3.</summary>
    public float M33 { get; }

    /// <summary>Gets row 3, column 4.</summary>
    public float M34 { get; }

    /// <summary>Gets row 4, column 1.</summary>
    public float M41 { get; }

    /// <summary>Gets row 4, column 2.</summary>
    public float M42 { get; }

    /// <summary>Gets row 4, column 3.</summary>
    public float M43 { get; }

    /// <summary>Gets row 4, column 4.</summary>
    public float M44 { get; }

    /// <summary>Gets the identity matrix.</summary>
    public static Matrix4f Identity => new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f);

    /// <summary>Composes two transforms.</summary>
    /// <param name="left">The transform applied second.</param>
    /// <param name="right">The transform applied first.</param>
    /// <returns>The product <c>left · right</c>.</returns>
    public static Matrix4f operator *(Matrix4f left, Matrix4f right) => Multiply(left, right);

    /// <summary>Determines whether two matrices are equal.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> when every element matches.</returns>
    public static bool operator ==(Matrix4f left, Matrix4f right) => left.Equals(right);

    /// <summary>Determines whether two matrices differ.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> when any element differs.</returns>
    public static bool operator !=(Matrix4f left, Matrix4f right) => !left.Equals(right);

    /// <summary>Composes two transforms.</summary>
    /// <param name="left">The transform applied second.</param>
    /// <param name="right">The transform applied first.</param>
    /// <returns>The product <c>left · right</c>.</returns>
    public static Matrix4f Multiply(Matrix4f left, Matrix4f right) => new(
        (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31) + (left.M14 * right.M41),
        (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32) + (left.M14 * right.M42),
        (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33) + (left.M14 * right.M43),
        (left.M11 * right.M14) + (left.M12 * right.M24) + (left.M13 * right.M34) + (left.M14 * right.M44),
        (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31) + (left.M24 * right.M41),
        (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32) + (left.M24 * right.M42),
        (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33) + (left.M24 * right.M43),
        (left.M21 * right.M14) + (left.M22 * right.M24) + (left.M23 * right.M34) + (left.M24 * right.M44),
        (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31) + (left.M34 * right.M41),
        (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32) + (left.M34 * right.M42),
        (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33) + (left.M34 * right.M43),
        (left.M31 * right.M14) + (left.M32 * right.M24) + (left.M33 * right.M34) + (left.M34 * right.M44),
        (left.M41 * right.M11) + (left.M42 * right.M21) + (left.M43 * right.M31) + (left.M44 * right.M41),
        (left.M41 * right.M12) + (left.M42 * right.M22) + (left.M43 * right.M32) + (left.M44 * right.M42),
        (left.M41 * right.M13) + (left.M42 * right.M23) + (left.M43 * right.M33) + (left.M44 * right.M43),
        (left.M41 * right.M14) + (left.M42 * right.M24) + (left.M43 * right.M34) + (left.M44 * right.M44));

    /// <summary>Builds a translation.</summary>
    /// <param name="x">The X offset.</param>
    /// <param name="y">The Y offset.</param>
    /// <param name="z">The Z offset.</param>
    /// <returns>The translation matrix.</returns>
    public static Matrix4f CreateTranslation(float x, float y, float z) => new(
        1f, 0f, 0f, x,
        0f, 1f, 0f, y,
        0f, 0f, 1f, z,
        0f, 0f, 0f, 1f);

    /// <summary>Builds a rigid transform from an orientation and a translation.</summary>
    /// <param name="orientation">The rotation, applied first.</param>
    /// <param name="translation">The translation, applied second.</param>
    /// <returns>The combined transform.</returns>
    public static Matrix4f CreateRigid(Matrix3f orientation, Vector3f translation) => new(
        orientation.M11, orientation.M12, orientation.M13, translation.X,
        orientation.M21, orientation.M22, orientation.M23, translation.Y,
        orientation.M31, orientation.M32, orientation.M33, translation.Z,
        0f, 0f, 0f, 1f);

    /// <summary>
    /// Builds the projection the original applies as scalar arithmetic, expressed for a clip-space
    /// pipeline.
    /// </summary>
    /// <param name="horizontalScale">The original's <c>xScale</c>: pixels per unit of <c>x/z</c>.</param>
    /// <param name="verticalScale">The original's <c>yScale</c>: pixels per unit of <c>y/z</c>.</param>
    /// <param name="centreX">The projection centre's horizontal pixel position.</param>
    /// <param name="centreY">The projection centre's vertical pixel position, measured from the top.</param>
    /// <param name="viewportWidth">The viewport width in pixels.</param>
    /// <param name="viewportHeight">The viewport height in pixels.</param>
    /// <param name="near">The near plane distance, greater than zero.</param>
    /// <param name="far">The far plane distance, greater than <paramref name="near"/>.</param>
    /// <returns>The projection matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is not positive, or the plane distances are invalid.</exception>
    /// <remarks>
    /// <para>
    /// View space is <b>left-handed with +Z into the screen</b>, which is what makes the original's
    /// divide by <c>z</c> well defined, so the last row is <c>(0, 0, 1, 0)</c> and <c>w</c> comes out as
    /// the view-space depth.
    /// </para>
    /// <para>
    /// The vertical scale is negated here because the original measures screen Y downwards from the top
    /// while clip space has Y up.
    /// </para>
    /// <para>
    /// <b>Divergence:</b> depth maps to the OpenGL range <c>[-1, 1]</c> rather than the Direct3D
    /// <c>[0, 1]</c> the original targets. Only the depth buffer sees the difference, and OpenIGI owns
    /// that buffer end to end.
    /// </para>
    /// </remarks>
    public static Matrix4f CreateProjection(
        float horizontalScale,
        float verticalScale,
        float centreX,
        float centreY,
        int viewportWidth,
        int viewportHeight,
        float near,
        float far)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(near);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(far, near);

        float a = 2f * horizontalScale / viewportWidth;
        float c = (2f * centreX / viewportWidth) - 1f;
        float b = -2f * verticalScale / viewportHeight;
        float d = 1f - (2f * centreY / viewportHeight);

        return new Matrix4f(
            a, 0f, c, 0f,
            0f, b, d, 0f,
            0f, 0f, (far + near) / (far - near), -2f * far * near / (far - near),
            0f, 0f, 1f, 0f);
    }

    /// <summary>Builds a symmetric perspective projection from a vertical field of view.</summary>
    /// <param name="verticalFieldOfView">The full vertical field of view in radians.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="near">The near plane distance, greater than zero.</param>
    /// <param name="far">The far plane distance, greater than <paramref name="near"/>.</param>
    /// <returns>The projection matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its valid range.</exception>
    /// <remarks>
    /// The same left-handed convention as <see cref="CreateProjection"/>. Use this for a free camera,
    /// where the original's per-camera pixel scales are not available.
    /// </remarks>
    public static Matrix4f CreatePerspective(float verticalFieldOfView, float aspectRatio, float near, float far)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verticalFieldOfView);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(verticalFieldOfView, MathF.PI);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectRatio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(near);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(far, near);

        float yScale = 1f / MathF.Tan(verticalFieldOfView * 0.5f);
        return new Matrix4f(
            yScale / aspectRatio, 0f, 0f, 0f,
            0f, yScale, 0f, 0f,
            0f, 0f, (far + near) / (far - near), -2f * far * near / (far - near),
            0f, 0f, 1f, 0f);
    }

    /// <summary>Builds an orthographic projection centred on the view-space origin.</summary>
    /// <param name="width">The box width.</param>
    /// <param name="height">The box height.</param>
    /// <param name="near">The near plane distance. May be negative.</param>
    /// <param name="far">The far plane distance.</param>
    /// <returns>The projection.</returns>
    public static Matrix4f CreateOrthographic(float width, float height, float near, float far)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return CreateOrthographic(-width * 0.5f, width * 0.5f, -height * 0.5f, height * 0.5f, near, far);
    }

    /// <summary>
    /// Builds an orthographic projection, for rendering a scene from a directional light.
    /// </summary>
    /// <param name="left">The left edge of the box, in view space.</param>
    /// <param name="right">The right edge of the box.</param>
    /// <param name="bottom">The bottom edge of the box.</param>
    /// <param name="top">The top edge of the box.</param>
    /// <param name="near">The near plane distance. May be negative: a light's box has no eye in it.</param>
    /// <param name="far">The far plane distance.</param>
    /// <returns>The projection.</returns>
    /// <remarks>
    /// <para>
    /// Same conventions as <see cref="CreatePerspective"/> — left-handed view space with <c>+Z</c> into
    /// the screen, and depth mapped to OpenGL's <c>[-1, 1]</c>.
    /// </para>
    /// <para>
    /// Unlike a perspective projection this one accepts a <paramref name="near"/> that is zero or
    /// negative, and needs to: the box a shadow cascade is fitted into is centred on the geometry it
    /// covers, and half of that geometry is behind the point the light's frame is built at. Refusing a
    /// negative near here would mean pushing every cascade artificially far back and spending its depth
    /// precision on empty space.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The box is empty in some axis.</exception>
    public static Matrix4f CreateOrthographic(
        float left,
        float right,
        float bottom,
        float top,
        float near,
        float far)
    {
        if (right == left || top == bottom || far == near)
        {
            throw new ArgumentException(
                $"An orthographic box must have extent on every axis, got x[{left}, {right}] y[{bottom}, {top}] z[{near}, {far}].",
                nameof(right));
        }

        float rl = right - left;
        float tb = top - bottom;
        float fn = far - near;

        return new Matrix4f(
            2f / rl, 0f, 0f, -(right + left) / rl,
            0f, 2f / tb, 0f, -(top + bottom) / tb,
            0f, 0f, 2f / fn, -(far + near) / fn,
            0f, 0f, 0f, 1f);
    }

    /// <summary>Transforms a point, dividing by the resulting <c>w</c>.</summary>
    /// <param name="value">The point to transform.</param>
    /// <returns>The transformed point in normalized device coordinates.</returns>
    /// <remarks>Returns the un-divided result when <c>w</c> is zero, which happens only for degenerate input.</remarks>
    public Vector3f TransformPoint(Vector3f value)
    {
        float x = (M11 * value.X) + (M12 * value.Y) + (M13 * value.Z) + M14;
        float y = (M21 * value.X) + (M22 * value.Y) + (M23 * value.Z) + M24;
        float z = (M31 * value.X) + (M32 * value.Y) + (M33 * value.Z) + M34;
        float w = (M41 * value.X) + (M42 * value.Y) + (M43 * value.Z) + M44;
        return w == 0f ? new Vector3f(x, y, z) : new Vector3f(x / w, y / w, z / w);
    }

    /// <summary>Writes the sixteen elements transposed, which is the layout OpenGL uniforms expect.</summary>
    /// <param name="destination">Receives the elements in column-major order.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> holds fewer than <see cref="ComponentCount"/> elements.</exception>
    public void CopyColumnMajorTo(Span<float> destination)
    {
        if (destination.Length < ComponentCount)
        {
            throw new ArgumentException($"A matrix needs {ComponentCount} elements.", nameof(destination));
        }

        destination[0] = M11; destination[1] = M21; destination[2] = M31; destination[3] = M41;
        destination[4] = M12; destination[5] = M22; destination[6] = M32; destination[7] = M42;
        destination[8] = M13; destination[9] = M23; destination[10] = M33; destination[11] = M43;
        destination[12] = M14; destination[13] = M24; destination[14] = M34; destination[15] = M44;
    }

    /// <inheritdoc/>
    public bool Equals(Matrix4f other) =>
        M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13) && M14.Equals(other.M14)
        && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23) && M24.Equals(other.M24)
        && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33) && M34.Equals(other.M34)
        && M41.Equals(other.M41) && M42.Equals(other.M42) && M43.Equals(other.M43) && M44.Equals(other.M44);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Matrix4f other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(M11); hash.Add(M12); hash.Add(M13); hash.Add(M14);
        hash.Add(M21); hash.Add(M22); hash.Add(M23); hash.Add(M24);
        hash.Add(M31); hash.Add(M32); hash.Add(M33); hash.Add(M34);
        hash.Add(M41); hash.Add(M42); hash.Add(M43); hash.Add(M44);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        $"[{M11.ToString(format, formatProvider)} {M12.ToString(format, formatProvider)} {M13.ToString(format, formatProvider)} {M14.ToString(format, formatProvider)}; "
        + $"{M21.ToString(format, formatProvider)} {M22.ToString(format, formatProvider)} {M23.ToString(format, formatProvider)} {M24.ToString(format, formatProvider)}; "
        + $"{M31.ToString(format, formatProvider)} {M32.ToString(format, formatProvider)} {M33.ToString(format, formatProvider)} {M34.ToString(format, formatProvider)}; "
        + $"{M41.ToString(format, formatProvider)} {M42.ToString(format, formatProvider)} {M43.ToString(format, formatProvider)} {M44.ToString(format, formatProvider)}]";
}
