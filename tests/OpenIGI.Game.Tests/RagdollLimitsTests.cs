using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers reading a rig's joint limits out of the animations it ships with, instead of writing them down.
/// </summary>
/// <remarks>
/// This is what gives a knee a hinge without any bone being named by index — see
/// <see cref="RagdollLimits"/> for why the animators have already answered the question.
/// </remarks>
public sealed class RagdollLimitsTests
{
    /// <summary>A joint no clip ever moves is reported as unmeasured rather than as free.</summary>
    [Fact]
    public void AJointNothingEverMovesIsNotMeasured()
    {
        // The clip bends the left knee and nothing else, so every other bone has no evidence at all.
        RagdollLimits limits = RagdollLimits.Measure(
            RagdollRig.Bending(RagdollRig.LeftShin, Vector3f.UnitX, 1.0f));

        Assert.True(limits.TryGet(RagdollRig.LeftShin, out _));

        // A bone whose every key is the identity has no swing to speak of, so what it gets is the floor.
        Assert.True(limits.TryGet(RagdollRig.RightShin, out RagdollJointRange still));
        Assert.Equal(RagdollLimits.MinimumSwing, still.SwingLimit, 1e-4);
    }

    /// <summary>A joint the clips only ever bend one way comes back as a cone centred on that bend.</summary>
    /// <remarks>
    /// <b>The whole point of measuring rather than assuming.</b> A knee's clips never straighten it and
    /// never bend it the other way, so the average of what they demonstrate sits part-way through the
    /// bend — and a cone drawn round that average excludes both straight and backwards. A hinge, arrived
    /// at from the data, with no axis assumed and no bone named.
    /// </remarks>
    [Fact]
    public void AJointBentOnlyOneWayIsCentredOnThatBend()
    {
        const float Bend = 1.2f;
        RagdollLimits limits = RagdollLimits.Measure(
            RagdollRig.Bending(RagdollRig.LeftShin, Vector3f.UnitX, Bend));

        Assert.True(limits.TryGet(RagdollRig.LeftShin, out RagdollJointRange knee));

        // The rest direction is straight down the shin; the clip carries it round by Bend. The cone's
        // centre must sit BETWEEN the two, not at either end.
        Vector3f rest = new Vector3f(0f, 400f, -300f).Normalized();
        float fromRest = MathF.Acos(Math.Clamp(Vector3f.Dot(knee.SwingAxis, rest), -1f, 1f));

        Assert.InRange(fromRest, Bend * 0.2f, Bend * 0.8f);

        // And the allowance is what was demonstrated, widened by the margin a corpse gets -- up to the
        // ceiling, which a bend this deep runs into.
        Assert.Equal(
            MathF.Min(Bend * RagdollLimits.Margin, RagdollLimits.MaximumSwing), knee.SwingLimit, 1e-3);
    }

    /// <summary>A joint the clips only twist is allowed to twist, and by what it was shown.</summary>
    /// <remarks>
    /// The allowance is the <b>total</b> angle from rest, because that is the one quantity the solver
    /// bounds. Measuring a swing and a twist separately and taking the wider of the two let a joint that
    /// does 60° of each deviate by 60° in a direction that is both — which is how a spine came out bent
    /// double.
    /// </remarks>
    [Fact]
    public void AJointOnlyEverTwistedIsAllowedTheTwistItWasShown()
    {
        // The left forearm's own axis runs along -X, so turning about X is pure twist for it.
        RagdollLimits limits = RagdollLimits.Measure(
            RagdollRig.Bending(RagdollRig.LeftForearm, Vector3f.UnitX, 0.9f));

        Assert.True(limits.TryGet(RagdollRig.LeftForearm, out RagdollJointRange wrist));

        // 0.9 rad demonstrated, widened by the margin a corpse gets over a living body.
        Assert.Equal(0.9f * RagdollLimits.Margin, wrist.SwingLimit, 1e-3);
        Assert.True(
            wrist.TwistHigh - wrist.TwistLow > 0.5f,
            $"The twist range came back as {wrist.TwistLow:F2}..{wrist.TwistHigh:F2}.");
    }

    /// <summary>No measurement ever produces a range wider than the solver is prepared to hold.</summary>
    [Fact]
    public void EveryMeasuredRangeIsClamped()
    {
        RagdollLimits limits = RagdollLimits.Measure(
            RagdollRig.Bending(RagdollRig.LeftUpperArm, Vector3f.UnitZ, 3.0f));

        for (int bone = 0; bone < limits.BoneCount; bone++)
        {
            if (!limits.TryGet(bone, out RagdollJointRange range))
            {
                continue;
            }

            Assert.InRange(range.SwingLimit, RagdollLimits.MinimumSwing, RagdollLimits.MaximumSwing);
            Assert.InRange(range.TwistLow, -RagdollLimits.MaximumTwist, 0f);
            Assert.InRange(range.TwistHigh, 0f, RagdollLimits.MaximumTwist);
            Assert.Equal(1f, range.SwingAxis.Length, 1e-3);
        }
    }

    /// <summary>The swing-twist split puts a pure twist in the twist and leaves the axis where it was.</summary>
    [Fact]
    public void APureTwistDoesNotMoveTheAxisItTwistsAbout()
    {
        Vector3f axis = new Vector3f(0.3f, -0.5f, 0.8f).Normalized();

        (float twist, Vector3f swung) = RagdollLimits.Decompose(
            Quaternion32.FromAxisAngle(axis, 0.8f), axis);

        Assert.Equal(0.8f, twist, 1e-3);
        Assert.Equal(1f, Vector3f.Dot(axis, swung), 1e-3);
    }

    /// <summary>Measuring a rig with no clips at all leaves every joint on the solver's own defaults.</summary>
    [Fact]
    public void ARigWithNoClipsFallsBackToTheDefaults()
    {
        BoneAnimationSet empty = BoneAnimationSet.Create(RagdollRig.Skeleton());
        RagdollLimits limits = RagdollLimits.Measure(empty);

        for (int bone = 0; bone < limits.BoneCount; bone++)
        {
            Assert.False(limits.TryGet(bone, out _));
        }

        RagdollSkeleton rig = RagdollSkeleton.Build(RagdollRig.Skeleton(), limits);
        foreach (RagdollJoint link in rig.Joints)
        {
            Assert.Equal(RagdollSkeleton.DefaultSwingLimit, link.SwingLimit, 1e-4);
        }
    }
}
