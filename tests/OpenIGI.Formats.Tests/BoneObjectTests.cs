using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Checks the bone object's source inheritance and its clock, which is what turns one clip started on
/// bone 0 into a pose for every bone.
/// </summary>
public sealed class BoneObjectTests
{
    [Fact]
    public void AClipStartedOnTheRootReachesEveryBone()
    {
        BoneObject bones = new(Set());

        Assert.Null(bones.EffectiveSourceOf(2));

        BoneClip? clip = bones.Play(0, TurningAnimationId, loops: true);

        Assert.NotNull(clip);
        for (int bone = 0; bone < bones.BoneCount; bone++)
        {
            Assert.Same(clip, bones.EffectiveSourceOf(bone));
        }
    }

    [Fact]
    public void ABoneWithItsOwnClipOverridesWhatItInherits()
    {
        BoneObject bones = new(Set());
        BoneClip? whole = bones.Play(0, TurningAnimationId);
        BoneClip? branch = bones.Play(1, StillAnimationId);

        // Bone 1 and its child take the branch; bone 3, which hangs off the root, keeps the first.
        Assert.Same(whole, bones.EffectiveSourceOf(0));
        Assert.Same(branch, bones.EffectiveSourceOf(1));
        Assert.Same(branch, bones.EffectiveSourceOf(2));
        Assert.Same(whole, bones.EffectiveSourceOf(3));
    }

    [Fact]
    public void StoppingABoneLetsItInheritAgain()
    {
        BoneObject bones = new(Set());
        BoneClip? whole = bones.Play(0, TurningAnimationId);
        bones.Play(1, StillAnimationId);

        bones.Stop(1);

        Assert.Same(whole, bones.EffectiveSourceOf(1));
        Assert.Same(whole, bones.EffectiveSourceOf(2));
    }

    [Fact]
    public void AnUndefinedAnimationIdLeavesTheBoneAlone()
    {
        BoneObject bones = new(Set());

        // The original allocates the id table zeroed and never fills the gaps, so an undefined id is an
        // absent clip rather than an error.
        Assert.Null(bones.Play(0, 900));
        Assert.Null(bones.EffectiveSourceOf(0));
    }

    [Fact]
    public void OneClipIsTickedOnceHoweverManyBonesInheritIt()
    {
        BoneObject bones = new(Set());
        BoneClip clip = bones.Play(0, TurningAnimationId, loops: true)!;

        bones.Tick();

        // Ticking per bone rather than per clip would run a four-bone rig four times too fast, and a
        // thirty-three-bone one thirty-three times.
        Assert.Equal(BoneAnimation.FrameStep, clip.Time, 3);
    }

    [Fact]
    public void AFinishedClipStopsInvalidatingItsPose()
    {
        BoneObject bones = new(Set());
        BoneClip clip = bones.Play(0, TurningAnimationId, loops: false)!;

        while (!clip.Ended)
        {
            bones.Tick();
        }

        Assert.True(bones.ChangedThisTick);

        bones.Tick();

        Assert.False(bones.ChangedThisTick);
    }

    [Fact]
    public void APausedClipDoesNotInvalidateItsPose()
    {
        BoneObject bones = new(Set());
        BoneClip clip = bones.Play(0, TurningAnimationId, loops: true)!;
        clip.Rate = 0f;

        bones.Tick();

        Assert.False(bones.ChangedThisTick);
    }

    [Fact]
    public void ThePoseComposesTheRootRotationDownTheChain()
    {
        BoneObject bones = new(Set());
        bones.Play(0, StillAnimationId);

        // A quarter turn about Z above the root: bone 1 sits one unit along +X in its rest pose, so it
        // lands on +Y.
        Matrix3f quarterTurn = new(0f, -1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f);
        BonePose pose = bones.Pose(quarterTurn);

        Assert.Equal(0f, pose.Positions[1].X, 4);
        Assert.Equal(1f, pose.Positions[1].Y, 4);
    }

    [Fact]
    public void AnUnsourcedBoneHoldsItsRestPose()
    {
        BoneObject bones = new(Set());

        BonePose pose = bones.Pose();

        // Nothing is playing, so every local rotation is the identity and the chain is the rest pose:
        // bone 2 hangs one unit past bone 1, which hangs one unit past the root.
        Assert.Equal(2f, pose.Positions[2].X, 4);
        Assert.Equal(Matrix3f.Identity, pose.Rotations[2]);
    }

    [Fact]
    public void PlayCrossFadesFromTheOutgoingClipOverTheBlendTime()
    {
        BoneObject bones = new(Set());

        // Drive the 90-degree turn to completion so the outgoing branch holds a known, frozen pose.
        BoneClip turning = bones.Play(0, TurningAnimationId, loops: false)!;
        bones.Tick();
        bones.Tick();
        Assert.True(turning.Ended);

        // 0.2 s = 6 ticks. The still clip (identity) fades in from that turned pose.
        bones.Play(0, StillAnimationId, loops: true, backwards: false, blendSeconds: 0.2f);

        // The live source is the incoming clip throughout the fade; only the pose is a blend.
        Assert.Same(bones.SourceOf(0), bones.EffectiveSourceOf(0));

        // Weight 0: fully the outgoing 90-degree turn, so bone 1 sits on +Y.
        BonePose start = bones.Pose();
        Assert.Equal(0f, start.Positions[1].X, 3);
        Assert.Equal(1f, start.Positions[1].Y, 3);

        // Three ticks in, weight 0.5: half-way back, bone 1 on the 45-degree diagonal.
        bones.Tick();
        bones.Tick();
        bones.Tick();
        BonePose mid = bones.Pose();
        Assert.Equal(MathF.Cos(MathF.PI / 4f), mid.Positions[1].X, 3);
        Assert.Equal(MathF.Sin(MathF.PI / 4f), mid.Positions[1].Y, 3);

        // Three more ticks completes the blend; the outgoing branch is dropped and bone 1 is back on +X.
        bones.Tick();
        bones.Tick();
        bones.Tick();
        BonePose done = bones.Pose();
        Assert.Equal(1f, done.Positions[1].X, 3);
        Assert.Equal(0f, done.Positions[1].Y, 3);
    }

    [Fact]
    public void PlayWithABlendUnderOneTickReplacesTheSourceOutright()
    {
        BoneObject bones = new(Set());
        bones.Play(0, TurningAnimationId, loops: false);
        bones.Tick();
        bones.Tick();

        // ~0.02 s = 0.6 ticks, under 0x4D61D0's one-tick threshold: the still pose shows at once.
        bones.Play(0, StillAnimationId, loops: true, backwards: false, blendSeconds: 0.02f);

        BonePose pose = bones.Pose();
        Assert.Equal(1f, pose.Positions[1].X, 3);
        Assert.Equal(0f, pose.Positions[1].Y, 3);
    }

    private const int StillAnimationId = 2;
    private const int TurningAnimationId = 3;

    /// <summary>
    /// A four-bone rig: root, a chain of two along +X, and a third bone hanging straight off the root.
    /// </summary>
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
