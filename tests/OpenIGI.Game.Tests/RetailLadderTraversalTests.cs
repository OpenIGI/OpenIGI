using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Runs the retail ladder clips through the gameplay state machine end to end.</summary>
public sealed class RetailLadderTraversalTests
{
    private static readonly Vector3d Origin = new(0.0, 0.0, -100.0);
    private static readonly Vector3d Foot = new(0.0, 0.0, 0.0);
    private static readonly Vector3d Head = new(0.0, 0.0, 20.0 * LadderClimbLine.StepLengthUnits);

    [RequiresGameDataFact]
    public void AnimationOneSixtyEightCommitsAtItsAuthoredHalfCycleEvent()
    {
        BoneAnimationSet set = PlayerAnimations();
        Assert.True(set.TryGet(168, out BoneAnimation animation));

        HumanLadderClimb climb = new();
        climb.Mount(Ladder(), atTop: false);
        Assert.Equal(LadderStepResult.SteppingUp, climb.Decide(up: true, down: false, slide: false));

        BoneClip clip = new(animation, bone: 0, loops: true);
        double startZ = climb.Position.Z;
        int ticks = 0;
        do
        {
            clip.Tick();
            Vector3f root = clip.RootDelta;
            climb.Move(SoldierDeathFall.RotateAboutZ(
                new Vector3d(root.X, root.Y, root.Z), climb.Facing));
            ticks++;
        }
        while (!clip.CrossedEvent(8) && ticks < 100);

        Assert.Equal(11, ticks); // 11 * 160 crosses the authored event at 1737
        Assert.InRange(climb.Position.Z - startZ, 1200.0, 1300.0);
        Assert.True(climb.CompleteStep());
        Assert.Equal(1, climb.Step);
        Assert.True(climb.AtBoundary);
    }

    [RequiresGameDataFact]
    public void BottomMountPlacesTheAuthoredStartingFootAtGroundHeight()
    {
        BoneObject bones = new(PlayerAnimations());
        Assert.NotNull(bones.Play(0, 168, loops: true));

        float lowestBone = bones.Pose().Positions.ToArray().Min(static position => position.Z);

        // IGI1 writes the task root at bottom + 3915.776. Animation 168's starting foot is
        // -4128.768 below that root, leaving only the small authored sole penetration at the ground.
        Assert.InRange(LadderPlacement.BottomVerticalOffsetUnits + lowestBone, -300.0, 0.0);
    }

    [RequiresGameDataFact]
    public void AnimationOneSeventyReturnsTheTopMountAlongTheSameAuthoredPath()
    {
        BoneAnimationSet set = PlayerAnimations();
        Assert.True(set.TryGet(170, out BoneAnimation animation));

        HumanLadderClimb climb = new();
        climb.Mount(Ladder(), atTop: true);
        Vector3d roofMount = climb.Position;

        PlayToEnd(climb, new BoneClip(animation, bone: 0, backwards: true));
        Assert.True(climb.Position.Z < roofMount.Z - 7000.0);
        Assert.True(climb.CompleteTopTransition());

        Assert.Equal(LadderStepResult.ReachedTop, climb.Decide(up: true, down: false, slide: false));
        PlayToEnd(climb, new BoneClip(animation, bone: 0));

        Assert.Equal(roofMount.X, climb.Position.X, 0.01);
        Assert.Equal(roofMount.Y, climb.Position.Y, 0.01);
        Assert.Equal(roofMount.Z, climb.Position.Z, 0.01);
        Assert.True(climb.IsOnLadder);
        Assert.True(climb.CompleteTopTransition());
        Assert.False(climb.IsOnLadder);
    }

    [RequiresGameDataFact]
    public void TheMountSoundsOnceAndAReversedRungRunsBackToZeroBeforeItCommits()
    {
        BoneAnimationSet set = PlayerAnimations();
        Assert.True(set.TryGet(168, out BoneAnimation animation));

        HumanLadderClimb climb = new();
        climb.Mount(Ladder(), atTop: false);
        double mountZ = climb.Position.Z;

        // State 37's entry: a fresh clip runs its first frame, and that tick reports event 8 at zero
        // against the latch the entry cleared. That is the mount sound; it pauses the clip one frame in
        // and commits nothing.
        BoneClip clip = new(animation, bone: 0, loops: true);
        clip.ArmStartEvents();
        clip.Tick();
        Vector3f entry = clip.RootDelta;
        climb.MoveEntryFrame(SoldierDeathFall.RotateAboutZ(
            new Vector3d(entry.X, entry.Y, entry.Z), climb.Facing));
        Assert.True(clip.TryGetCrossedEvent(8, out float mount));
        Assert.Equal(0f, mount);
        Assert.True(climb.AcceptRungEvent(mount));
        Assert.False(climb.CompleteStep());
        Assert.Equal(0, climb.Step);
        clip.Rate = 0f;

        // So the first rung runs from 160 rather than zero, and crosses 1737 a tick sooner.
        Assert.Equal(LadderStepResult.SteppingUp, climb.Decide(up: true, down: false, slide: false));
        clip.Rate = BoneAnimation.FrameStep;
        Assert.Equal(10, RunToAcceptedEvent(climb, clip, out float up));
        Assert.Equal(1737f, up);
        Assert.True(climb.CompleteStep());
        Assert.Equal(1, climb.Step);

        // Back down from 1760: the first tick re-crosses 1737, the latched time, and is ignored; the
        // rung only commits at the event at zero, a whole half-cycle later, back where it started.
        Assert.Equal(LadderStepResult.SteppingDown, climb.Decide(up: false, down: true, slide: false));
        clip.Rate = -BoneAnimation.FrameStep;
        Assert.Equal(11, RunToAcceptedEvent(climb, clip, out float down));
        Assert.Equal(0f, down);
        Assert.True(climb.CompleteStep());
        Assert.Equal(0, climb.Step);
        Assert.Equal(mountZ, climb.Position.Z, 1.0);
    }

    private static int RunToAcceptedEvent(HumanLadderClimb climb, BoneClip clip, out float time)
    {
        time = 0f;
        int ticks = 0;
        bool accepted;
        do
        {
            clip.Tick();
            Vector3f root = clip.RootDelta;
            climb.Move(SoldierDeathFall.RotateAboutZ(
                new Vector3d(root.X, root.Y, root.Z), climb.Facing));
            ticks++;
            accepted = clip.TryGetCrossedEvent(8, out time) && climb.AcceptRungEvent(time);
        }
        while (!accepted && ticks < 100);

        Assert.True(accepted);
        return ticks;
    }

    private static void PlayToEnd(HumanLadderClimb climb, BoneClip clip)
    {
        int ticks = 0;
        do
        {
            clip.Tick();
            Vector3f root = clip.RootDelta;
            climb.Move(SoldierDeathFall.RotateAboutZ(
                new Vector3d(root.X, root.Y, root.Z), climb.Facing));
            ticks++;
        }
        while (!clip.EndedThisTick && ticks < 1000);

        Assert.True(clip.EndedThisTick);
    }

    private static LadderPlacement Ladder() =>
        new(Origin, Head, Foot, Head, Matrix3f.Identity);

    private static BoneAnimationSet PlayerAnimations() =>
        BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));
}
