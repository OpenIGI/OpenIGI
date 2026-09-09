using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The orientation composed above a bone's own animation — the engine's one orientation source, and what
/// bends a body to look where its owner is looking.
/// </summary>
public sealed class BoneBendTests
{
    /// <summary>A bend turns a bone's children and leaves the bone itself where its parent put it.</summary>
    /// <remarks>
    /// Which is what a joint does. A bone is placed by its <em>parent's</em> rotation
    /// (<see cref="BonePose.Evaluate"/>), so rotating one moves everything below it and nothing above.
    /// </remarks>
    [Fact]
    public void ABendTurnsTheBonesBelowItAndNotTheBoneItself()
    {
        BoneObject bones = new(Set());
        bones.Play(0, StillAnimationId, loops: true);

        BonePose level = bones.Pose();
        Assert.Equal(1f, level.Positions[1].X, 4);
        Assert.Equal(2f, level.Positions[2].X, 4);

        // A quarter turn about Z at bone 1: bone 2 swings from +X round to +Y, bone 1 does not move.
        bones.Bend(1, Quaternion32.FromAxisAngle(new Vector3f(0f, 0f, 1f), MathF.PI / 2f));

        BonePose bent = bones.Pose();
        Assert.Equal(1f, bent.Positions[1].X, 4);
        Assert.Equal(0f, bent.Positions[1].Y, 4);
        Assert.Equal(1f, bent.Positions[2].X, 4);
        Assert.Equal(1f, bent.Positions[2].Y, 4);
    }

    /// <summary>
    /// The bend is in the PARENT's frame, which is what makes it a joint angle rather than a twist.
    /// </summary>
    /// <remarks>
    /// <c>sub_4D5820</c>'s type 5 resolves the node as <c>stored ⊗ sampled</c> — the stored rotation on
    /// the left. Composing on the right instead would express the bend in the bone's own already-animated
    /// frame, so how far a spine leaned back would depend on which way the walk cycle had it turned.
    /// </remarks>
    [Fact]
    public void ABendComposesOnTheParentSideOfTheAnimation()
    {
        BoneObject spun = new(Set());
        spun.Play(0, TurningAnimationId, loops: true);
        spun.Tick();
        spun.Tick();
        spun.Bend(1, Quaternion32.FromAxisAngle(new Vector3f(0f, 0f, 1f), MathF.PI / 2f));

        BoneObject reference = new(Set());
        reference.Play(0, TurningAnimationId, loops: true);
        reference.Tick();
        reference.Tick();

        Matrix3f parent = reference.Pose().Rotations[0];
        Matrix3f own = reference.Pose().Rotations[1];
        Matrix3f bend = BonePose.ToMatrix(Quaternion32.FromAxisAngle(new Vector3f(0f, 0f, 1f), MathF.PI / 2f));

        // parent * bend * local, which is the parent's own rotation times the bend times what the bone
        // had been turned to: `parent * bend * (parent^-1 * own)`.
        Matrix3f expected = parent * bend * parent.Transposed() * own;
        Matrix3f actual = spun.Pose().Rotations[1];

        AssertClose(expected, actual);
    }

    /// <summary>A bend of null is no bend, and removing one restores the pose exactly.</summary>
    [Fact]
    public void RemovingABendRestoresThePose()
    {
        BoneObject bones = new(Set());
        bones.Play(0, StillAnimationId, loops: true);
        Vector3f before = bones.Pose().Positions[2];

        bones.Bend(1, Quaternion32.FromAxisAngle(new Vector3f(0f, 0f, 1f), 0.4f));
        Assert.NotNull(bones.BendOf(1));

        bones.Bend(1, null);
        Assert.Null(bones.BendOf(1));

        Vector3f after = bones.Pose().Positions[2];
        Assert.Equal(before.X, after.X, 5);
        Assert.Equal(before.Y, after.Y, 5);
        Assert.Equal(before.Z, after.Z, 5);
    }

    /// <summary>
    /// A rotation about X built as a quaternion is the same rotation <see cref="Matrix3f.RotationX"/> is.
    /// </summary>
    /// <remarks>
    /// The pin under the aim bend. Two conversions live in this codebase —
    /// <see cref="BonePose.ToMatrix"/> is the palette's <c>M · v</c> and <c>Quaternion32.ToMatrix</c> is
    /// the row-vector <c>v · M</c>, and they are transposes — so which one a bend is measured against
    /// decides whether a character leans back or forward. The palette's is the one bones are posed with.
    /// </remarks>
    [Fact]
    public void APitchQuaternionMatchesTheRotationMatrixTheRigIsPosedWith()
    {
        const float angle = 0.4f;
        Matrix3f fromQuaternion = BonePose.ToMatrix(
            Quaternion32.FromAxisAngle(new Vector3f(1f, 0f, 0f), angle));

        AssertClose(Matrix3f.RotationX(angle), fromQuaternion);

        // And it is the one that leans a forward axis UP: +Y goes toward +Z for a positive angle, which
        // is what makes a positive camera pitch a body that looks up rather than down.
        Vector3f forward = Rotate(fromQuaternion, new Vector3f(0f, 1f, 0f));
        Assert.True(forward.Z > 0f, $"a positive pitch should raise the forward axis, not lower it ({forward.Z}).");
    }

    private const int StillAnimationId = 2;
    private const int TurningAnimationId = 3;

    // The palette applies its 3x3 as M * v, dotting the rows.
    private static Vector3f Rotate(Matrix3f m, Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    private static void AssertClose(in Matrix3f expected, in Matrix3f actual)
    {
        Assert.Equal(expected.M11, actual.M11, 4);
        Assert.Equal(expected.M12, actual.M12, 4);
        Assert.Equal(expected.M13, actual.M13, 4);
        Assert.Equal(expected.M21, actual.M21, 4);
        Assert.Equal(expected.M22, actual.M22, 4);
        Assert.Equal(expected.M23, actual.M23, 4);
        Assert.Equal(expected.M31, actual.M31, 4);
        Assert.Equal(expected.M32, actual.M32, 4);
        Assert.Equal(expected.M33, actual.M33, 4);
    }

    /// <summary>The same four-bone rig <see cref="BoneObjectTests"/> uses: root, a chain of two, a spur.</summary>
    private static BoneAnimationSet Set()
    {
        BoneSkeleton skeleton = BoneSkeleton.Create(
            [BoneSkeleton.NoParent, 0, 1, 0],
            [default, new Vector3f(1f, 0f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f)]);

        return TestAnimationSet.Create(
            skeleton,
            TestAnimationSet.Clip(StillAnimationId, duration: 320f, spin: 0f),
            TestAnimationSet.Clip(TurningAnimationId, duration: 320f, spin: MathF.PI / 2f));
    }
}
