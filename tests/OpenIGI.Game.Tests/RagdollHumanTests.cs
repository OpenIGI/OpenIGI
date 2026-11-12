using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Physics;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The ragdoll behaves like a body: hinges bend one way, nothing rolls past what a joint allows, the
/// muscles follow the death clip and then let go, and the real soldier rig is built the way a person is.
/// </summary>
public sealed class RagdollHumanTests
{
    /// <summary>A knee bends backwards and never forwards or sideways, however hard the shin is shoved.</summary>
    /// <remarks>
    /// <b>The single most impossible-looking thing a corpse did.</b> The old limit was one symmetric cone
    /// about the rest pose, so a shin pushed forwards bent the knee the wrong way as far as it would the
    /// right way.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 4000.0, 0.0)]
    [InlineData(4000.0, 0.0, 0.0)]
    [InlineData(-3000.0, -3000.0, 1000.0)]
    public void AKneeIsAHinge(double x, double y, double z)
    {
        RagdollSkeleton rig = HingedRig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 30_000.0));
        body.AddImpulse(RagdollRig.LeftShin, new Vector3d(x, y, z));

        for (int tick = 0; tick < 60; tick++)
        {
            body.Step(RagdollWorld.Empty, substeps: 8, 0.0);
            AssertHinge(rig, body, RagdollRig.LeftShin);
        }
    }

    /// <summary>An elbow is a hinge too, bending forwards.</summary>
    [Theory]
    [InlineData(0.0, -3000.0, 0.0)]
    [InlineData(0.0, 0.0, 3000.0)]
    public void AnElbowIsAHinge(double x, double y, double z)
    {
        RagdollSkeleton rig = HingedRig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 30_000.0));
        body.AddImpulse(RagdollRig.LeftForearm, new Vector3d(x, y, z));

        for (int tick = 0; tick < 60; tick++)
        {
            body.Step(RagdollWorld.Empty, substeps: 8, 0.0);
            AssertHinge(rig, body, RagdollRig.LeftForearm);
        }
    }

    /// <summary>A limb spun hard about its own length stops rolling where the joint's twist range ends.</summary>
    /// <remarks>The roll the old limit left free, and the source of the sweet-wrapper pinch in the skin.</remarks>
    [Fact]
    public void ALimbCannotRollPastItsTwistRange()
    {
        RagdollSkeleton rig = HingedRig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 30_000.0));
        int arm = rig.BodyOf(RagdollRig.LeftUpperArm);
        RagdollJoint link = JointOf(rig, arm);

        // A couple about the arm's own length: two equal and opposite pushes either side of it.
        Vector3d side = new(0.0, 0.0, 300.0);
        body.AddBodyImpulse(arm, new Vector3d(0.0, 3000.0, 0.0), side);
        body.AddBodyImpulse(arm, new Vector3d(0.0, -3000.0, 0.0), -side);

        for (int tick = 0; tick < 60; tick++)
        {
            body.Step(RagdollWorld.Empty, substeps: 8, 0.0);
            Quaternion32 parent = body.OrientationOf(rig.BoneOf(link.Parent));
            Quaternion32 child = body.OrientationOf(RagdollRig.LeftUpperArm);
            (float twist, _) = RagdollLimits.Decompose((parent.Conjugate() * child).Normalized(), link.TwistAxis);
            Assert.InRange(twist, link.TwistLow - 0.1f, link.TwistHigh + 0.1f);
        }
    }

    /// <summary>The muscles pull a joint towards the death clip while they last, and a limp body ignores it.</summary>
    [Fact]
    public void TheMusclesFollowTheClipAndThenLetGo()
    {
        RagdollSkeleton rig = HingedRig();
        BoneSkeleton skeleton = rig.Skeleton;

        // The clip: the left elbow bent a right angle, the way it bends.
        RagdollJoint elbow = JointOf(rig, rig.BodyOf(RagdollRig.LeftForearm));
        Quaternion32[] locals = new Quaternion32[skeleton.BoneCount];
        Array.Fill(locals, Quaternion32.Identity);
        locals[RagdollRig.LeftForearm] = Quaternion32.FromAxisAngle(elbow.BendAxis, MathF.PI / 2f);
        BonePose clip = BonePose.Evaluate(skeleton, locals);

        float Bend(RagdollBody body)
        {
            Matrix3f parent = BonePose.ToMatrix(body.OrientationOf(RagdollRig.LeftUpperArm));
            Matrix3f child = BonePose.ToMatrix(body.OrientationOf(RagdollRig.LeftForearm));
            Vector3f rest = RagdollSolver.Rotate(parent, elbow.TwistAxis);
            Vector3f along = RagdollSolver.Rotate(child, elbow.TwistAxis);
            return MathF.Acos(Math.Clamp(Vector3f.Dot(rest, along), -1f, 1f));
        }

        RagdollBody driven = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 30_000.0));
        RagdollBody limp = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 30_000.0));
        for (int tick = 0; tick < 10; tick++)
        {
            driven.Step(RagdollWorld.Empty, substeps: 8, 0.0, clip, muscleTicks: 60.0);
            limp.Step(RagdollWorld.Empty, substeps: 8, 0.0, clip, muscleTicks: 0.0);
        }

        Assert.True(Bend(driven) > 1.0f, $"The driven elbow only bent {Bend(driven):F2} rad.");
        Assert.True(Bend(limp) < 0.2f, $"The limp elbow bent {Bend(limp):F2} rad on its own.");
    }

    /// <summary>Carried bones ride their body: a hand moves with its forearm and keeps its own turn.</summary>
    [Fact]
    public void ACarriedBoneRidesItsBody()
    {
        RagdollSkeleton rig = HingedRig();
        Assert.False(rig.IsSimulated(RagdollRig.LeftHand));
        Assert.Equal(rig.BodyOf(RagdollRig.LeftForearm), rig.BodyOf(RagdollRig.LeftHand));

        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 30_000.0));
        body.AddImpulse(RagdollRig.LeftForearm, new Vector3d(0.0, 2000.0, 0.0));
        for (int tick = 0; tick < 20; tick++)
        {
            body.Step(RagdollWorld.Empty, substeps: 8, 0.0);
        }

        Quaternion32 forearm = body.OrientationOf(RagdollRig.LeftForearm);
        Quaternion32 hand = body.OrientationOf(RagdollRig.LeftHand);
        Assert.True(MathF.Abs(Quaternion32.Dot(forearm, hand)) > 0.9999f, "The hand turned against its forearm.");

        Vector3f reach = RagdollSolver.Rotate(BonePose.ToMatrix(forearm), rig.Skeleton.RestTranslations[RagdollRig.LeftHand]);
        Vector3d expected = body.PositionOf(RagdollRig.LeftForearm) + new Vector3d(reach.X, reach.Y, reach.Z);
        Assert.True(Vector3d.Distance(expected, body.PositionOf(RagdollRig.LeftHand)) < 1.0);
    }

    /// <summary>The real soldier rig is recognised and built as a body: sixteen parts weighing what a person's do.</summary>
    [RequiresGameDataFact]
    public void TheSoldierRigIsBuiltAsABody()
    {
        (BoneAnimationSet set, MefModel model) = Soldier();
        Assert.True(HumanRagdollProfile.Matches(set.Skeleton));

        RagdollSkeleton rig = RagdollSkeleton.Build(set.Skeleton, RagdollLimits.Measure(set), model);
        Assert.True(rig.Profile.IsHuman);
        Assert.Equal(16, rig.BodyCount);

        double total = 0.0;
        for (int part = 0; part < rig.BodyCount; part++)
        {
            total += 1.0 / rig.BodyAt(part).InverseMass;
        }

        Assert.Equal(RagdollSkeleton.BodyMassInKilograms, total, 1e-3);

        // A hand a fortieth of the chest, as a person's is -- not the quarter-for-quarter the old
        // conditioning squeezed every neighbour into.
        double chest = 1.0 / rig.BodyAt(rig.BodyOf(HumanRagdollProfile.Chest)).InverseMass;
        double hand = 1.0 / rig.BodyAt(rig.BodyOf(HumanRagdollProfile.LeftHand)).InverseMass;
        Assert.InRange(chest / hand, 30.0, 40.0);

        // The chest's capsule lies across the body and is a ribcage's depth, not a 4 cm stick.
        RagdollBone ribs = rig.BodyAt(rig.BodyOf(HumanRagdollProfile.Chest));
        Assert.Equal(Vector3f.UnitX, ribs.Aim);
        Assert.InRange(ribs.Radius / 4096.0, 0.08, 0.25);
        Assert.True(ribs.HalfLength > 0f);

        // A thigh is thicker than a forearm.
        float thigh = rig.BodyAt(rig.BodyOf(HumanRagdollProfile.LeftThigh)).Radius;
        float forearm = rig.BodyAt(rig.BodyOf(HumanRagdollProfile.LeftForearm)).Radius;
        Assert.True(thigh > forearm, $"thigh {thigh:F0}, forearm {forearm:F0}");

        // Both knees and both elbows are hinges.
        foreach (int bone in (int[])[HumanRagdollProfile.LeftShin, HumanRagdollProfile.RightShin, HumanRagdollProfile.LeftForearm, HumanRagdollProfile.RightForearm])
        {
            Assert.Equal(RagdollJointKind.Hinge, JointOf(rig, rig.BodyOf(bone)).Kind);
        }
    }

    /// <summary>A soldier dropped on his back stays on his back, rather than rolling like a log.</summary>
    [RequiresGameDataFact]
    public void ASoldierOnHisBackDoesNotRoll()
    {
        (BoneAnimationSet set, MefModel model) = Soldier();
        RagdollSkeleton rig = RagdollSkeleton.Build(set.Skeleton, RagdollLimits.Measure(set), model);

        // Face up: the model's forward (+Y) turned to the sky.
        Matrix3f onBack = BonePose.ToMatrix(Quaternion32.FromAxisAngle(Vector3f.UnitX, MathF.PI / 2f));
        RagdollBody body = RagdollBody.Seed(rig, Rest(set.Skeleton), new Vector3d(0.0, 0.0, 1_600.0), onBack);
        body.AddImpulse(HumanRagdollProfile.Chest, new Vector3d(600.0, 0.0, 0.0));

        RagdollWorld plane = RagdollWorld.Plane(0.0);
        for (int tick = 0; tick < 300 && !body.IsAtRest; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        Vector3f facing = RagdollSolver.Rotate(BonePose.ToMatrix(body.OrientationOf(HumanRagdollProfile.Chest)), Vector3f.UnitY);
        Assert.True(facing.Z > 0.8f, $"The chest ended up facing {facing}.");
    }

    /// <summary>A soldier's knees never bend forwards as he falls, whichever way he is pushed.</summary>
    [RequiresGameDataFact]
    public void ASoldiersKneesAreHinges()
    {
        (BoneAnimationSet set, MefModel model) = Soldier();
        RagdollSkeleton rig = RagdollSkeleton.Build(set.Skeleton, RagdollLimits.Measure(set), model);
        foreach (Vector3d push in (Vector3d[])[new(0.0, 900.0, 0.0), new(0.0, -900.0, 0.0), new(900.0, 0.0, 0.0)])
        {
            RagdollBody body = RagdollBody.Seed(rig, Rest(set.Skeleton), new Vector3d(0.0, 0.0, 4_000.0), Matrix3f.Identity);
            body.AddImpulse(HumanRagdollProfile.LeftShin, push);
            body.AddImpulse(HumanRagdollProfile.RightShin, push);

            RagdollWorld plane = RagdollWorld.Plane(0.0);
            for (int tick = 0; tick < 150 && !body.IsAtRest; tick++)
            {
                body.Step(plane, substeps: 8);
                AssertHinge(rig, body, HumanRagdollProfile.LeftShin);
                AssertHinge(rig, body, HumanRagdollProfile.RightShin);
            }
        }
    }

    // The test rig with a person's joints: hinged elbows and knees, everything else a ball.
    private static RagdollSkeleton HingedRig()
    {
        static float Rad(float degrees) => degrees * MathF.PI / 180f;
        static RagdollJointSpec Ball(float swing, float twist) => new(RagdollJointKind.Ball, Rad(swing), -Rad(twist), Rad(twist));
        static RagdollJointSpec Hinge(float high, Vector3f toward) =>
            new(RagdollJointKind.Hinge, 0f, 0f, 0f, Rad(-3f), Rad(high), toward);

        Vector3f forwards = new(0f, 1f, 0f);
        Vector3f backwards = new(0f, -1f, 0f);
        (RagdollBodySpec, RagdollJointSpec)[] table =
        [
            (new(RagdollRig.Pelvis, "pelvis", 0.15, RagdollShapeAxis.AlongBone), Ball(0f, 0f)),
            (new(RagdollRig.Spine, "spine", 0.15, RagdollShapeAxis.AlongBone), Ball(25f, 15f)),
            (new(RagdollRig.Chest, "chest", 0.25, RagdollShapeAxis.AlongBone), Ball(25f, 20f)),
            (new(RagdollRig.LeftUpperArm, "L upper arm", 0.03, RagdollShapeAxis.AlongBone), Ball(85f, 60f)),
            (new(RagdollRig.LeftForearm, "L forearm", 0.025, RagdollShapeAxis.AlongBone), Hinge(145f, forwards)),
            (new(RagdollRig.RightUpperArm, "R upper arm", 0.03, RagdollShapeAxis.AlongBone), Ball(85f, 60f)),
            (new(RagdollRig.RightForearm, "R forearm", 0.025, RagdollShapeAxis.AlongBone), Hinge(145f, forwards)),
            (new(RagdollRig.LeftThigh, "L thigh", 0.1, RagdollShapeAxis.AlongBone), Ball(70f, 25f)),
            (new(RagdollRig.LeftShin, "L shin", 0.07, RagdollShapeAxis.AlongBone), Hinge(140f, backwards)),
            (new(RagdollRig.RightThigh, "R thigh", 0.1, RagdollShapeAxis.AlongBone), Ball(70f, 25f)),
            (new(RagdollRig.RightShin, "R shin", 0.07, RagdollShapeAxis.AlongBone), Hinge(140f, backwards)),
        ];

        BoneSkeleton skeleton = RagdollRig.Skeleton();
        string[] names = [.. Enumerable.Range(0, skeleton.BoneCount).Select(bone => $"{bone}")];
        RagdollProfile profile = new([.. table.Select(row => row.Item1)], [.. table.Select(row => row.Item2)], names, isHuman: false);
        return RagdollSkeleton.Build(skeleton, profile: profile);
    }

    private static RagdollJoint JointOf(RagdollSkeleton rig, int body) =>
        rig.Joints.Single(joint => joint.Child == body);

    // A hinge holds its axis to its parent's and bends only the one way, within a little give.
    private static void AssertHinge(RagdollSkeleton rig, RagdollBody body, int bone)
    {
        RagdollJoint link = JointOf(rig, rig.BodyOf(bone));
        Assert.Equal(RagdollJointKind.Hinge, link.Kind);

        int above = rig.Skeleton.Parents[bone];
        Matrix3f parent = BonePose.ToMatrix(body.OrientationOf(above));
        Matrix3f child = BonePose.ToMatrix(body.OrientationOf(bone));
        Vector3f hinge = RagdollSolver.Rotate(parent, link.BendAxis);
        Vector3f own = RagdollSolver.Rotate(child, link.BendAxis);
        Vector3f rest = RagdollSolver.Rotate(parent, link.TwistAxis);
        Vector3f along = RagdollSolver.Rotate(child, link.TwistAxis);

        float misaligned = MathF.Acos(Math.Clamp(Vector3f.Dot(hinge, own), -1f, 1f));
        float bend = MathF.Atan2(Vector3f.Dot(Vector3f.Cross(rest, along), hinge), Vector3f.Dot(rest, along));
        Assert.True(misaligned < 0.2f, $"Bone {bone}'s hinge came {misaligned:F2} rad out of line.");
        Assert.InRange(bend, link.BendLow - 0.15f, link.BendHigh + 0.15f);
    }

    private static BonePose Rest(BoneSkeleton skeleton)
    {
        Quaternion32[] locals = new Quaternion32[skeleton.BoneCount];
        Array.Fill(locals, Quaternion32.Identity);
        return BonePose.Evaluate(skeleton, locals);
    }

    /// <summary>A rig measured from no points is the rig guessed without a model; a point on a bone the skeleton lacks is skipped.</summary>
    [RequiresGameDataFact]
    public void AMeasuredRigSkipsPointsOnBonesItHasNot()
    {
        (BoneAnimationSet set, _) = Soldier();
        RagdollLimits limits = RagdollLimits.Measure(set);
        RagdollSkeleton guessed = RagdollSkeleton.Build(set.Skeleton, limits);

        RagdollSkeleton unmeasured = RagdollSkeleton.BuildMeasured(set.Skeleton, [], limits);
        RagdollSkeleton strays = RagdollSkeleton.BuildMeasured(
            set.Skeleton, [(-1, new Vector3f(1f, 2f, 3f)), (set.Skeleton.BoneCount, new Vector3f(4f, 5f, 6f))], limits);

        Assert.Equal(guessed.BodyCount, unmeasured.BodyCount);
        for (int body = 0; body < guessed.BodyCount; body++)
        {
            Assert.Equal(guessed.BodyAt(body).Radius, unmeasured.BodyAt(body).Radius);
            Assert.Equal(guessed.BodyAt(body).Radius, strays.BodyAt(body).Radius);
        }
    }

    private static (BoneAnimationSet Set, MefModel Model) Soldier()
    {
        BoneAnimationSet set = BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));
        MefModel model = RetailLevelOneGround.Models().First(m =>
            m.SkinInfluences.Length > 0 && m.HighestSkinBone is >= 20 and <= 32);
        return (set, model);
    }
}
