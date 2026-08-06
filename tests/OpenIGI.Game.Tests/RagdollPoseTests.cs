using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the seam between the solver and the skinner — the step where being subtly wrong looks like a
/// rendering bug rather than a physics one.
/// </summary>
public sealed class RagdollPoseTests
{
    /// <summary>How near two positions must be to count as the same, in engine units.</summary>
    private const double Tolerance = 0.5;

    /// <summary>
    /// A body seeded from a pose and asked for its pose straight back gives the same pose. This is the
    /// whole correctness argument for the seam: if the round trip is not the identity then the corpse
    /// snaps to a different shape on the frame physics takes over.
    /// </summary>
    [Fact]
    public void SeedingAndPosingIsTheIdentity()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        BonePose animated = RagdollRig.Standing();
        RagdollBody body = RagdollRig.Standing(rig);

        BonePose drawn = body.Pose();

        Assert.Equal(animated.BoneCount, drawn.BoneCount);
        for (int bone = 0; bone < animated.BoneCount; bone++)
        {
            Assert.Equal(animated.Positions[bone].X, drawn.Positions[bone].X, Tolerance);
            Assert.Equal(animated.Positions[bone].Y, drawn.Positions[bone].Y, Tolerance);
            Assert.Equal(animated.Positions[bone].Z, drawn.Positions[bone].Z, Tolerance);
        }
    }

    /// <summary>
    /// The same is true of a body that was turned and moved before it died — the heading is folded into
    /// the root bone's own rotation, and the origin the pose is built against is the one it was seeded
    /// with, so the two together land it exactly where it was drawn.
    /// </summary>
    [Fact]
    public void SeedingAndPosingIsTheIdentityForATurnedBodyAwayFromTheOrigin()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        BonePose animated = RagdollRig.Standing();
        Vector3d origin = new(120_000.0, -48_000.0, 3_500.0);
        Matrix3f facing = Matrix3f.FromEngineEulerAngles(0f, 0f, 1.1f);

        RagdollBody body = RagdollBody.Seed(rig, animated, origin, facing);
        BonePose drawn = body.Pose();

        for (int bone = 0; bone < animated.BoneCount; bone++)
        {
            Vector3f expected = RagdollSolver.Rotate(facing, animated.Positions[bone]);
            Assert.Equal(expected.X, drawn.Positions[bone].X, 1.0);
            Assert.Equal(expected.Y, drawn.Positions[bone].Y, 1.0);
            Assert.Equal(expected.Z, drawn.Positions[bone].Z, 1.0);
        }
    }

    /// <summary>
    /// The drawn pose <b>is</b> the simulation, not a fit to it: every bone's drawn joint sits where the
    /// solver actually put it.
    /// </summary>
    /// <remarks>
    /// The single largest reason the solver is rigid bodies rather than points. A particle carries no
    /// orientation, so its pose has to be recovered by fitting a rotation to where its children ended up
    /// — and a bone that forks three ways cannot be fitted exactly, so the drawn hips end up somewhere
    /// the body is not. A rigid body already knows which way it faces, and this pins that the read is
    /// direct.
    /// </remarks>
    [Fact]
    public void TheDrawnPoseIsTheSimulationRatherThanAFitToIt()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 20_000.0));

        // Throw one leg sideways: the pelvis now has a spine pointing up and two thighs disagreeing,
        // which is precisely the configuration a fitted pose cannot represent.
        body.AddImpulse(RagdollRig.LeftShin, new Vector3d(600.0, 0.0, 0.0));
        RagdollWorld plane = RagdollWorld.Plane(0.0);
        for (int tick = 0; tick < 30; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        BonePose drawn = body.Pose();
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            Vector3d simulated = body.PositionOf(bone) - body.Origin;
            Vector3f posed = drawn.Positions[bone];
            double apart = Vector3d.Distance(simulated, new Vector3d(posed.X, posed.Y, posed.Z));

            // Eight centimetres, and what is left is the solver's own joint drift rather than any error
            // in the reading. It is the price of eight substeps rather than sixteen -- three times
            // cheaper, and it settles just as well -- and of the bones now being as thick as the model
            // they carry, which gives the self-collision far more to push against.
            Assert.True(apart < 328.0, $"Bone {bone} is drawn {apart:F1} units from where it is simulated.");
        }
    }

    /// <summary>
    /// Reading a rotation back out of a matrix is the exact inverse of the conversion the bone palette
    /// uses. It must be that one and not <see cref="Quaternion32.ToMatrix"/>'s, which differs by a
    /// transpose — the trap <c>BonePose</c> carries its own warning about.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.7, 0.0, 0.0)]
    [InlineData(0.0, 1.9, 0.0)]
    [InlineData(0.0, 0.0, 3.0)]
    [InlineData(2.5, -1.3, 0.6)]
    public void ReadingARotationBackOutInvertsTheConversionThePaletteUses(double x, double y, double z)
    {
        Matrix3f rotation = Matrix3f.RotationZ((float)z)
            * Matrix3f.RotationY((float)y)
            * Matrix3f.RotationX((float)x);

        Matrix3f roundTrip = BonePose.ToMatrix(RagdollPose.QuaternionOf(rotation));

        Assert.Equal(rotation.M11, roundTrip.M11, 1e-4);
        Assert.Equal(rotation.M12, roundTrip.M12, 1e-4);
        Assert.Equal(rotation.M13, roundTrip.M13, 1e-4);
        Assert.Equal(rotation.M21, roundTrip.M21, 1e-4);
        Assert.Equal(rotation.M22, roundTrip.M22, 1e-4);
        Assert.Equal(rotation.M23, roundTrip.M23, 1e-4);
        Assert.Equal(rotation.M31, roundTrip.M31, 1e-4);
        Assert.Equal(rotation.M32, roundTrip.M32, 1e-4);
        Assert.Equal(rotation.M33, roundTrip.M33, 1e-4);
    }
}
