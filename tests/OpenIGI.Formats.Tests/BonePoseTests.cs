using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Pins the bone matrix palette fill against <c>0x4D3A90</c>.
/// </summary>
public sealed class BonePoseTests
{
    private static readonly Quaternion32 Identity = new(0f, 0f, 0f, 1f);

    [Fact]
    public void TheIdentityQuaternionIsTheIdentityMatrix() =>
        Assert.Equal(Matrix3f.Identity, BonePose.ToMatrix(Identity));

    [Fact]
    public void ToMatrixTurnsAVectorTheSameWayTheQuaternionDoes()
    {
        // A quarter turn about Z takes +X to +Y. The palette applies its matrix as M * v, so the rows
        // are what get dotted with the vector.
        Matrix3f m = BonePose.ToMatrix(AboutZ(MathF.PI / 2f));

        Vector3f turned = new(
            (m.M11 * 1f) + (m.M12 * 0f) + (m.M13 * 0f),
            (m.M21 * 1f) + (m.M22 * 0f) + (m.M23 * 0f),
            (m.M31 * 1f) + (m.M32 * 0f) + (m.M33 * 0f));

        Assert.Equal(0f, turned.X, 5);
        Assert.Equal(1f, turned.Y, 5);
        Assert.Equal(0f, turned.Z, 5);
    }

    [Fact]
    public void ToMatrixIsOrthonormal()
    {
        // Exactly unit: w = sqrt(1 - 0.09 - 0.04 - 0.25). The conversion assumes a unit quaternion, so
        // feeding it a nearly-unit one would test the tolerance rather than the arithmetic.
        Matrix3f m = BonePose.ToMatrix(new Quaternion32(0.3f, -0.2f, 0.5f, MathF.Sqrt(0.62f)));

        // Rows are unit length and mutually perpendicular.
        Assert.Equal(1f, (m.M11 * m.M11) + (m.M12 * m.M12) + (m.M13 * m.M13), 4);
        Assert.Equal(1f, (m.M21 * m.M21) + (m.M22 * m.M22) + (m.M23 * m.M23), 4);
        Assert.Equal(1f, (m.M31 * m.M31) + (m.M32 * m.M32) + (m.M33 * m.M33), 4);
        Assert.Equal(0f, (m.M11 * m.M21) + (m.M12 * m.M22) + (m.M13 * m.M23), 4);
        Assert.Equal(0f, (m.M11 * m.M31) + (m.M12 * m.M32) + (m.M13 * m.M33), 4);
    }

    [Fact]
    public void ARestPoseLaysTheChainOutAlongItsRestTranslations()
    {
        // With every local rotation the identity, a bone lands at the running sum of the rest
        // translations from the root down.
        BoneSkeleton skeleton = Chain(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(10f, 0f, 0f),
            new Vector3f(0f, 5f, 0f));

        BonePose pose = BonePose.Evaluate(skeleton, [Identity, Identity, Identity]);

        AssertClose(new Vector3f(0f, 0f, 0f), pose.Positions[0]);
        AssertClose(new Vector3f(10f, 0f, 0f), pose.Positions[1]);
        AssertClose(new Vector3f(10f, 5f, 0f), pose.Positions[2]);
    }

    [Fact]
    public void AParentsRotationCarriesItsChildren()
    {
        // Turning the root a quarter turn about Z swings the whole chain with it.
        BoneSkeleton skeleton = Chain(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(10f, 0f, 0f),
            new Vector3f(10f, 0f, 0f));

        BonePose pose = BonePose.Evaluate(
            skeleton, [AboutZ(MathF.PI / 2f), Identity, Identity]);

        AssertClose(new Vector3f(0f, 0f, 0f), pose.Positions[0]);
        AssertClose(new Vector3f(0f, 10f, 0f), pose.Positions[1]);
        AssertClose(new Vector3f(0f, 20f, 0f), pose.Positions[2]);
    }

    [Fact]
    public void AChildsRotationDoesNotMoveItself()
    {
        // A bone's own rotation orients what hangs off it; its own position comes from the parent.
        BoneSkeleton skeleton = Chain(new Vector3f(0f, 0f, 0f), new Vector3f(10f, 0f, 0f));

        BonePose pose = BonePose.Evaluate(skeleton, [Identity, AboutZ(MathF.PI / 2f)]);

        AssertClose(new Vector3f(10f, 0f, 0f), pose.Positions[1]);
    }

    [Fact]
    public void AChainComposesParentFirst()
    {
        // Two quarter turns about *different* axes. Composing them the other way round gives a
        // different rotation, so this is what catches the parent/child order being reversed — the
        // same class of mistake as M6's camera transpose.
        BoneSkeleton skeleton = Chain(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(0f, 0f, 0f),
            new Vector3f(10f, 0f, 0f));

        BonePose pose = BonePose.Evaluate(
            skeleton, [AboutZ(MathF.PI / 2f), AboutX(MathF.PI / 2f), Identity]);

        // parent(Z) then child(X): the child's +X rest offset is turned by Z only (X-rotation leaves
        // +X alone), landing on +Y. The reversed order would send it to +Z's neighbourhood instead.
        AssertClose(new Vector3f(0f, 10f, 0f), pose.Positions[2]);

        Matrix3f expected = BonePose.ToMatrix(AboutZ(MathF.PI / 2f)) * BonePose.ToMatrix(AboutX(MathF.PI / 2f));
        AssertClose(expected, pose.Rotations[1]);
        Assert.NotEqual(
            BonePose.ToMatrix(AboutX(MathF.PI / 2f)) * BonePose.ToMatrix(AboutZ(MathF.PI / 2f)),
            pose.Rotations[1]);
    }

    [Fact]
    public void TheRootTransformIsAppliedAboveTheSkeleton()
    {
        BoneSkeleton skeleton = Chain(new Vector3f(0f, 0f, 0f), new Vector3f(10f, 0f, 0f));

        BonePose pose = BonePose.Evaluate(
            skeleton,
            [Identity, Identity],
            new Vector3f(100f, 200f, 300f),
            BonePose.ToMatrix(AboutZ(MathF.PI / 2f)));

        AssertClose(new Vector3f(100f, 200f, 300f), pose.Positions[0]);
        AssertClose(new Vector3f(100f, 210f, 300f), pose.Positions[1]);
    }

    [Fact]
    public void AWrongLengthRotationListIsRejected()
    {
        BoneSkeleton skeleton = Chain(new Vector3f(0f, 0f, 0f), new Vector3f(1f, 0f, 0f));

        Assert.Throws<ArgumentException>(() => BonePose.Evaluate(skeleton, [Identity]));
    }

    [RequiresGameDataFact]
    public void ARetailSkeletonPosesAtItsRestWithoutDrift()
    {
        // End to end on real data: pose 000.iff's skeleton at identity and check that every bone is
        // exactly the running sum of its ancestors' rest translations, and every rotation orthonormal.
        string path = Path.Combine(
            OpenIGI.TestSupport.GameData.RequireRoot(), "common", "anims", "000.iff");
        BoneSkeleton skeleton = BoneAnimationSet.Read(File.ReadAllBytes(path)).Skeleton;

        Quaternion32[] rest = [.. Enumerable.Repeat(Identity, skeleton.BoneCount)];
        BonePose pose = BonePose.Evaluate(skeleton, rest);

        for (int bone = 0; bone < skeleton.BoneCount; bone++)
        {
            int parent = skeleton.Parents[bone];
            Vector3f expected = skeleton.RestTranslations[bone];
            if (parent != BoneSkeleton.NoParent)
            {
                Vector3f p = pose.Positions[parent];
                expected = new Vector3f(expected.X + p.X, expected.Y + p.Y, expected.Z + p.Z);
            }

            AssertClose(expected, pose.Positions[bone], 2);
            Assert.Equal(Matrix3f.Identity, pose.Rotations[bone]);
        }
    }

    private static Quaternion32 AboutZ(float radians) =>
        new(0f, 0f, MathF.Sin(radians * 0.5f), MathF.Cos(radians * 0.5f));

    private static Quaternion32 AboutX(float radians) =>
        new(MathF.Sin(radians * 0.5f), 0f, 0f, MathF.Cos(radians * 0.5f));

    private static BoneSkeleton Chain(params Vector3f[] restTranslations)
    {
        // A straight chain: bone 0 is the root, every other bone's parent is the one before it.
        int[] parents = new int[restTranslations.Length];
        parents[0] = BoneSkeleton.NoParent;
        for (int i = 1; i < parents.Length; i++)
        {
            parents[i] = i - 1;
        }

        return BoneSkeleton.Create(parents, restTranslations);
    }

    private static void AssertClose(Vector3f expected, Vector3f actual, int precision = 4)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }

    private static void AssertClose(Matrix3f expected, Matrix3f actual, int precision = 4)
    {
        Assert.Equal(expected.M11, actual.M11, precision);
        Assert.Equal(expected.M12, actual.M12, precision);
        Assert.Equal(expected.M13, actual.M13, precision);
        Assert.Equal(expected.M21, actual.M21, precision);
        Assert.Equal(expected.M22, actual.M22, precision);
        Assert.Equal(expected.M23, actual.M23, precision);
        Assert.Equal(expected.M31, actual.M31, precision);
        Assert.Equal(expected.M32, actual.M32, precision);
        Assert.Equal(expected.M33, actual.M33, precision);
    }
}
