using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>Covers the vector, matrix and quaternion value types.</summary>
public sealed class NumericsTests
{
    [Fact]
    public void Vector3d_ArithmeticIsComponentWise()
    {
        var a = new Vector3d(1.0, 2.0, 3.0);
        var b = new Vector3d(0.5, -1.0, 2.0);

        Assert.Equal(new Vector3d(1.5, 1.0, 5.0), a + b);
        Assert.Equal(new Vector3d(0.5, 3.0, 1.0), a - b);
        Assert.Equal(new Vector3d(2.0, 4.0, 6.0), a * 2.0);
        Assert.Equal(new Vector3d(0.5, 1.0, 1.5), a / 2.0);
        Assert.Equal(new Vector3d(-1.0, -2.0, -3.0), -a);
    }

    [Fact]
    public void Vector3d_DotAndCrossFollowTheRightHandRule()
    {
        Assert.Equal(0.0, Vector3d.Dot(Vector3d.UnitX, Vector3d.UnitY));
        Assert.Equal(Vector3d.UnitZ, Vector3d.Cross(Vector3d.UnitX, Vector3d.UnitY));
        Assert.Equal(-Vector3d.UnitZ, Vector3d.Cross(Vector3d.UnitY, Vector3d.UnitX));
    }

    [Fact]
    public void Vector3d_LengthAndNormalization()
    {
        var v = new Vector3d(3.0, 4.0, 0.0);
        Assert.Equal(25.0, v.LengthSquared);
        Assert.Equal(5.0, v.Length);
        Assert.Equal(new Vector3d(0.6, 0.8, 0.0), v.Normalized());
        Assert.Equal(Vector3d.Zero, Vector3d.Zero.Normalized());
    }

    [Fact]
    public void Vector3d_ConvertsToAndFromSinglePrecision()
    {
        var v = new Vector3d(1.0, 2.0, 3.0);
        Assert.Equal(new Vector3f(1f, 2f, 3f), v.ToVector3f());
        Assert.Equal(v, new Vector3f(1f, 2f, 3f).ToVector3d());
    }

    [Fact]
    public void Vector3f_LengthAndCross()
    {
        Assert.Equal(5f, new Vector3f(3f, 4f, 0f).Length);
        Assert.Equal(Vector3f.UnitZ, Vector3f.Cross(Vector3f.UnitX, Vector3f.UnitY));
        Assert.Equal(Vector3f.Zero, Vector3f.Zero.Normalized());
    }

    [Fact]
    public void Matrix3f_IdentityLeavesVectorsUnchanged()
    {
        var v = new Vector3f(1f, 2f, 3f);
        Assert.Equal(v, Matrix3f.Identity.Transform(v));
        Assert.Equal(new Vector3d(1.0, 2.0, 3.0), Matrix3f.Identity.Transform(new Vector3d(1.0, 2.0, 3.0)));
        Assert.Equal(1f, Matrix3f.Identity.Determinant);
    }

    [Fact]
    public void Matrix3f_StoresNineFloatsRowMajor()
    {
        float[] values = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f];
        Matrix3f m = Matrix3f.FromRowMajor(values);

        Assert.Equal(new Vector3f(1f, 2f, 3f), m.Row1);
        Assert.Equal(new Vector3f(4f, 5f, 6f), m.Row2);
        Assert.Equal(new Vector3f(7f, 8f, 9f), m.Row3);
        Assert.Equal(new Vector3f(1f, 4f, 7f), m.Column1);
        Assert.Equal(5f, m[1, 1]);

        float[] roundTrip = new float[Matrix3f.ComponentCount];
        m.CopyTo(roundTrip);
        Assert.Equal(values, roundTrip);
    }

    [Fact]
    public void Matrix3f_TransformUsesTheRowVectorConvention()
    {
        // Rows are the basis vectors, so transforming UnitX must return the first row.
        Matrix3f m = Matrix3f.FromRows(
            new Vector3f(0f, 1f, 0f),
            new Vector3f(-1f, 0f, 0f),
            new Vector3f(0f, 0f, 1f));

        Assert.Equal(new Vector3f(0f, 1f, 0f), m.Transform(Vector3f.UnitX));
        Assert.Equal(new Vector3f(-1f, 0f, 0f), m.Transform(Vector3f.UnitY));
    }

    [Fact]
    public void Matrix3f_TransposeOfARotationIsItsInverse()
    {
        Matrix3f rotation = Quaternion32.FromAxisAngle(Vector3f.UnitZ, MathF.PI / 3f).ToMatrix();
        Matrix3f product = rotation * rotation.Transposed();

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Assert.Equal(row == column ? 1f : 0f, product[row, column], 5);
            }
        }
    }

    [Fact]
    public void Matrix3f_IndexerRejectsOutOfRange()
    {
        Matrix3f m = Matrix3f.Identity;
        Assert.Throws<ArgumentOutOfRangeException>(() => m[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => m[0, 3]);
    }

    [Fact]
    public void Quaternion32_IdentityRotatesNothing()
    {
        var v = new Vector3f(1f, 2f, 3f);
        Assert.Equal(v, Quaternion32.Identity.Rotate(v));
        Assert.Equal(Matrix3f.Identity, Quaternion32.Identity.ToMatrix());
    }

    [Fact]
    public void Quaternion32_RotatesAboutAnAxis()
    {
        Quaternion32 q = Quaternion32.FromAxisAngle(Vector3f.UnitZ, MathF.PI / 2f);
        Vector3f rotated = q.Rotate(Vector3f.UnitX);

        Assert.Equal(0f, rotated.X, 5);
        Assert.Equal(1f, rotated.Y, 5);
        Assert.Equal(0f, rotated.Z, 5);
    }

    [Fact]
    public void Quaternion32_ComposesRotations()
    {
        Quaternion32 quarter = Quaternion32.FromAxisAngle(Vector3f.UnitZ, MathF.PI / 2f);
        Vector3f rotated = (quarter * quarter).Rotate(Vector3f.UnitX);

        Assert.Equal(-1f, rotated.X, 5);
        Assert.Equal(0f, rotated.Y, 5);
    }

    [Fact]
    public void Quaternion32_NormalizesAndConjugates()
    {
        var q = new Quaternion32(0f, 0f, 2f, 0f);
        Assert.Equal(1f, q.Normalized().Length, 5);
        Assert.Equal(new Quaternion32(0f, 0f, -2f, 0f), q.Conjugate());
        Assert.Equal(Quaternion32.Identity, default(Quaternion32).Normalized());
    }
}
