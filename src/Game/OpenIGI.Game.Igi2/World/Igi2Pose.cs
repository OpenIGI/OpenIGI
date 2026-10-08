using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Where an object stands and how it is turned, exactly as the engine holds it: a position in doubles and a
/// nine-float matrix, both built in the client's 24-bit truncating arithmetic.
/// </summary>
/// <remarks>
/// The matrix is stored row by row, and it takes the object's own frame to the world's: world = M · local. A
/// collision line handler multiplies with these exact floats, so a pose that is a bit off is a wall that is
/// a bit off.
/// </remarks>
public sealed class Igi2Pose
{
    private Igi2Pose(Vector3d position, float[] orientation)
    {
        Position = position;
        Orientation = orientation;
    }

    /// <summary>Gets the position, <c>tPos</c>, in world units.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the orientation, <c>tOrientation</c>: nine floats, row by row.</summary>
    public IReadOnlyList<float> Orientation { get; }

    /// <summary>
    /// The pose a script's position and three angles describe: <c>Real32x9_Create</c> (<c>igi2 0x562A60</c>), which
    /// <c>Level_ParseReal32x9Handler</c> (<c>0x542000</c>) calls for every <c>Real32x9</c> parameter.
    /// </summary>
    /// <param name="position">The position, in world units.</param>
    /// <param name="angles">The angles alpha, beta and gamma, in radians.</param>
    /// <returns>The pose.</returns>
    /// <remarks>
    /// <para>
    /// The rotation is Z(gamma) · Y(beta) · X(alpha), built as gamma's matrix multiplied by beta's and the result by
    /// alpha's, every term in general form. The zero terms drop out exactly, which leaves each element one product
    /// or one two-product sum, each truncated to 24 bits.
    /// </para>
    /// <para>
    /// Gamma's and alpha's sine and cosine are used straight off <c>fsin</c> and <c>fcos</c>; beta's are first stored
    /// as floats. Precision control does not reach the trigonometric instructions, so their own results are wide;
    /// this port has them at double precision, against the FPU's 64-bit significand.
    /// </para>
    /// </remarks>
    public static Igi2Pose FromAngles(Vector3d position, Vector3f angles) =>
        new(position, Rotation(angles.X, angles.Y, angles.Z));

    /// <summary>The rotation three angles describe, <c>Real32x9_Create</c> (<c>igi2 0x562A60</c>): see <see cref="FromAngles"/>.</summary>
    /// <param name="alpha">The turn about X, in radians.</param>
    /// <param name="beta">The turn about Y.</param>
    /// <param name="gamma">The turn about Z.</param>
    /// <returns>The nine floats, row by row.</returns>
    public static float[] Rotation(float alpha, float beta, float gamma)
    {
        Fpu sinGamma = Fpu.Sin(gamma), cosGamma = Fpu.Cos(gamma);
        float sinBeta = Fpu.Sin(beta).F, cosBeta = Fpu.Cos(beta).F;

        // Gamma times beta.
        float p00 = (cosBeta * cosGamma).F, p01 = (-sinGamma).F, p02 = ((Fpu)sinBeta * cosGamma).F;
        float p10 = (cosBeta * sinGamma).F, p11 = cosGamma.F, p12 = ((Fpu)sinBeta * sinGamma).F;
        float p20 = -sinBeta, p22 = cosBeta;

        // Then alpha.
        Fpu sinAlpha = Fpu.Sin(alpha), cosAlpha = Fpu.Cos(alpha);
        return
        [
            p00, ((cosAlpha * p01) + (sinAlpha * p02)).F, ((cosAlpha * p02) + (-sinAlpha * p01)).F,
            p10, ((cosAlpha * p11) + (sinAlpha * p12)).F, ((cosAlpha * p12) + (-sinAlpha * p11)).F,
            p20, (sinAlpha * p22).F, (cosAlpha * p22).F,
        ];
    }

    /// <summary>A pose whose orientation its type's handler builds as a matrix rather than from three angles.</summary>
    /// <param name="position">The position, in world units.</param>
    /// <param name="frame">The orientation, local to world.</param>
    /// <returns>The pose.</returns>
    public static Igi2Pose FromMatrix(Vector3d position, Matrix3f frame) =>
        new(position, [frame.M11, frame.M12, frame.M13, frame.M21, frame.M22, frame.M23, frame.M31, frame.M32, frame.M33]);

    /// <summary>
    /// The pose of an object hanging off this one: <c>ModelObj_CreateAttachObject</c> (<c>igi2 0x56D0B0</c>).
    /// </summary>
    /// <param name="attachment">The attachment record from this object's model.</param>
    /// <returns>The attached object's pose.</returns>
    /// <remarks>
    /// <para>
    /// The record's matrix is used transposed, its first two columns as the first two rows, and the third row is
    /// rebuilt as their cross product. That is then turned by this object's matrix, and the record's position is
    /// turned and added to this object's position. Each row of each product sums its three terms in its own order.
    /// </para>
    /// <para>
    /// The position sum is a register sum too, so an attached object's world position is truncated to 24 bits.
    /// </para>
    /// </remarks>
    public Igi2Pose Attach(MefAttachment attachment)
    {
        Matrix3f relative = RelativeOrientation(attachment.Orientation);
        float xx = relative.M11, xy = relative.M12, xz = relative.M13;
        float yx = relative.M21, yy = relative.M22, yz = relative.M23;
        float zx = relative.M31, zy = relative.M32, zz = relative.M33;

        IReadOnlyList<float> p = Orientation;
        Vector3f at = attachment.Position;
        Fpu x = at.X, y = at.Y, z = at.Z;
        Fpu turnedX = ((Fpu)p[0] * x) + ((Fpu)p[1] * y) + ((Fpu)p[2] * z);
        Fpu turnedY = ((Fpu)p[4] * y) + ((Fpu)p[5] * z) + ((Fpu)p[3] * x);
        Fpu turnedZ = ((Fpu)p[7] * y) + ((Fpu)p[8] * z) + ((Fpu)p[6] * x);
        Vector3d position = new(
            (turnedX + Position.X).Value,
            (turnedY + Position.Y).Value,
            (turnedZ + Position.Z).Value);

        float[] m =
        [
            Top(xx, yx, zx), Top(xy, yy, zy), Top(xz, yz, zz),
            Lower(3, xx, yx, zx), Lower(3, xy, yy, zy), Lower(3, xz, yz, zz),
            Lower(6, xx, yx, zx), Lower(6, xy, yy, zy), Lower(6, xz, yz, zz),
        ];
        return new Igi2Pose(position, m);

        // The first row: x, y, z in order. The others start from y.
        float Top(float rx, float ry, float rz) => (((Fpu)rx * p[0]) + ((Fpu)ry * p[1]) + ((Fpu)rz * p[2])).F;

        float Lower(int row, float rx, float ry, float rz) =>
            (((Fpu)ry * p[row + 1]) + ((Fpu)rz * p[row + 2]) + ((Fpu)rx * p[row])).F;
    }

    /// <summary>
    /// How an attach record's matrix turns its object against what it hangs on: <c>ModelObj_CreateAttachObject</c>
    /// (<c>igi2 0x56D0B0</c>) for a model's attachments and a clip's attach objects alike.
    /// </summary>
    /// <param name="record">The record's matrix, as the file stores it.</param>
    /// <returns>The relative orientation: the record's first two columns as rows, the third row their cross product.</returns>
    /// <remarks>The relative orientation then stands under the parent's (and its bone's) orientation: world = P · B · R.</remarks>
    public static Matrix3f RelativeOrientation(Matrix3f record)
    {
        float xx = record.M11, xy = record.M21, xz = record.M31;
        float yx = record.M12, yy = record.M22, yz = record.M32;
        float zx = (((Fpu)yz * xy) - ((Fpu)yy * xz)).F;
        float zy = (-(((Fpu)yz * xx) - ((Fpu)yx * xz))).F;
        float zz = (((Fpu)yy * xx) - ((Fpu)yx * xy)).F;
        return new Matrix3f(xx, xy, xz, yx, yy, yz, zx, zy, zz);
    }

    /// <summary>
    /// The turn about the vertical an orientation stands at: the gamma of <c>Real32x9_GetAngles</c> (<c>igi2 0x562D60</c>).
    /// </summary>
    /// <param name="m0">The orientation's first element, <c>tX.vX</c>.</param>
    /// <param name="m3">Its fourth, <c>tY.vX</c>.</param>
    /// <returns>The turn, in radians from zero to a whole turn.</returns>
    /// <remarks>
    /// <c>fpatan</c> of the two, which precision control does not reach; zero when the X axis stands within a ten
    /// thousandth of vertical, and a quarter turn either way when <paramref name="m0"/> is zero. A negative turn is
    /// brought into range by a whole turn, at 24 bits.
    /// </remarks>
    public static float Gamma(float m0, float m3)
    {
        Fpu length = Fpu.Sqrt(((Fpu)m3 * m3) + ((Fpu)m0 * m0));
        if (!(length.Value > 0.0001))
        {
            return 0f;
        }

        Fpu gamma = m0 != 0f
            ? Math.Atan2(m3, m0)
            : (Fpu)(m3 < 0f ? -1.0 : 1.0) * 1.570796326794897;
        if (gamma.Value < 0.0)
        {
            gamma += 6.283185307179589;
        }

        return gamma.F;
    }

    /// <summary>
    /// The turn about the vertical a direction points at: the gamma of <c>Real32x3_GetAngles</c> (<c>igi2 0x561AF0</c>).
    /// </summary>
    /// <param name="direction">The direction; need not be normalised.</param>
    /// <returns>The turn, in radians, as stored: <c>fpatan</c> of y over x, or zero when x is zero.</returns>
    public static float DirectionGamma(Vector3f direction) =>
        direction.X == 0f ? 0f : Fpu.Atan2(direction.Y, direction.X).F;

    /// <summary>
    /// The inverse of a nine-float matrix, transposed: <c>Real32x9_InvertSafe</c> (<c>igi2 0x563490</c>), which
    /// divides each cofactor by the determinant and stores it where the element it was formed for stands.
    /// </summary>
    /// <param name="m">The matrix, row by row.</param>
    /// <returns>The cofactors over the determinant, row by row: the inverse of the matrix's transpose.</returns>
    /// <remarks>
    /// Five products are stored as floats first and one is kept on the FPU; the determinant sums them in the
    /// handler's order, and its reciprocal is taken against a double one.
    /// </remarks>
    public static float[] InverseTranspose(IReadOnlyList<float> m)
    {
        ArgumentNullException.ThrowIfNull(m);

        Fpu p37 = (Fpu)m[3] * m[7];
        float p65 = ((Fpu)m[6] * m[5]).F;
        float p84 = ((Fpu)m[8] * m[4]).F;
        float p57 = ((Fpu)m[5] * m[7]).F;
        float p83 = ((Fpu)m[8] * m[3]).F;
        float p64 = ((Fpu)m[6] * m[4]).F;
        Fpu det = ((Fpu)p84 * m[0]) + (p37 * m[2]) + ((Fpu)p65 * m[1]) - ((Fpu)p57 * m[0]) - ((Fpu)p83 * m[1]) - ((Fpu)p64 * m[2]);
        Fpu inverse = (Fpu)1.0 / det;
        return
        [
            (((Fpu)p84 - p57) * inverse).F,
            (-(((Fpu)p83 - p65) * inverse)).F,
            ((p37 - p64) * inverse).F,
            (-((((Fpu)m[8] * m[1]) - ((Fpu)m[2] * m[7])) * inverse)).F,
            ((((Fpu)m[8] * m[0]) - ((Fpu)m[6] * m[2])) * inverse).F,
            (-((((Fpu)m[0] * m[7]) - ((Fpu)m[6] * m[1])) * inverse)).F,
            ((((Fpu)m[5] * m[1]) - ((Fpu)m[2] * m[4])) * inverse).F,
            (-((((Fpu)m[5] * m[0]) - ((Fpu)m[3] * m[2])) * inverse)).F,
            ((((Fpu)m[4] * m[0]) - ((Fpu)m[3] * m[1])) * inverse).F,
        ];
    }

    /// <summary>The orientation as a matrix, row for row.</summary>
    /// <returns>The matrix.</returns>
    public Matrix3f ToMatrix()
    {
        IReadOnlyList<float> m = Orientation;
        return new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
    }

    /// <summary>The orientation as a fresh array, for a consumer that keeps its own copy.</summary>
    /// <returns>The nine floats, row by row.</returns>
    public float[] ToArray() => [.. Orientation];
}
