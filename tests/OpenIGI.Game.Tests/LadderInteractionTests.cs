using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>Pins ladder geometry and all four IGI1 player ladder states.</summary>
public sealed class LadderInteractionTests
{
    private static readonly Vector3d Origin = new(1000.0, 2000.0, -100.0);
    private static readonly Vector3d Foot = new(1000.0, 2000.0, 0.0);
    private static readonly Vector3d Head = new(1000.0, 2000.0, 12288.0);

    [Fact]
    public void StepCountIsHeightOverOneStepTruncated()
    {
        LadderClimbLine line = new(Foot, Head);
        Assert.Equal(10, line.StepCount);
        Assert.Equal(4, line.TopStep);
    }

    [Fact]
    public void StepCountTruncatesRatherThanRounds()
    {
        LadderClimbLine line = new(
            Foot,
            new Vector3d(Foot.X, Foot.Y, LadderClimbLine.StepLengthUnits * 11.9));
        Assert.Equal(11, line.StepCount);
    }

    [Fact]
    public void RungSoundsFollowTheRetailTwelveStepCycleAcrossLadders()
    {
        // 0x538C18: 0,1,2,3,1,2,1,2,3,2,1,0, each played as walk_ladder_(n + 1). Nothing resets the cursor
        // on a mount, so the second ladder carries on where the first one stopped.
        string[] expected =
        [
            "walk_ladder_1", "walk_ladder_2", "walk_ladder_3", "walk_ladder_4",
            "walk_ladder_2", "walk_ladder_3", "walk_ladder_2", "walk_ladder_3",
            "walk_ladder_4", "walk_ladder_3", "walk_ladder_2", "walk_ladder_1",
            "walk_ladder_1",
        ];

        HumanLadderClimb climb = new();
        climb.Mount(Ladder(), atTop: false);
        string[] heard = new string[expected.Length];
        for (int rung = 0; rung < heard.Length; rung++)
        {
            if (rung == 6)
            {
                climb.Mount(Ladder(), atTop: true);
            }

            heard[rung] = climb.NextStepSound();
        }

        Assert.Equal(expected, heard);
    }

    [Fact]
    public void PositionInterpolatesAlongTheSegment()
    {
        LadderClimbLine line = new(Foot, Head);
        Assert.Equal(Foot, line.PositionAtStep(0));
        Assert.Equal(Head, line.PositionAtStep(line.StepCount));
        Assert.Equal(6144.0, line.PositionAtStep(5).Z, 3);
    }

    [Fact]
    public void MountPositionsUseTheTwoExactOrientationColumnExpressions()
    {
        LadderPlacement ladder = Ladder();
        HumanLadderClimb climb = new();

        climb.Mount(ladder, atTop: false);
        Assert.Equal(Foot.X, climb.Position.X, 3);
        Assert.Equal(Foot.Y + LadderPlacement.BottomStandoffUnits, climb.Position.Y, 3);
        Assert.Equal(Foot.Z + LadderPlacement.BottomVerticalOffsetUnits, climb.Position.Z, 3);
        Assert.Equal(MathF.PI, climb.Facing, 4);
        Assert.Equal(-MathF.PI, ladder.PlayerYaw, 4);
        Assert.Equal(LadderTraversalPhase.Climbing, climb.Phase);

        climb.Mount(ladder, atTop: true);
        Assert.Equal(Head.X, climb.Position.X, 3);
        Assert.Equal(Head.Y - LadderPlacement.TopStandoffUnits, climb.Position.Y, 3);
        Assert.Equal(Head.Z + LadderPlacement.TopVerticalOffsetUnits, climb.Position.Z, 3);
        Assert.Equal(LadderTraversalPhase.GettingOnTop, climb.Phase);
    }

    [Fact]
    public void TopMountPlaysItsTransitionBeforeStateThirtySeven()
    {
        HumanLadderClimb climb = new();
        LadderPlacement ladder = Ladder();
        climb.Mount(ladder, atTop: true);

        Assert.True(climb.IsOnLadder);
        Assert.False(climb.AtBoundary);
        Assert.Equal(ladder.ClimbLine.TopStep, climb.Step);

        Assert.True(climb.CompleteTopTransition());
        Assert.Equal(LadderTraversalPhase.Climbing, climb.Phase);
        Assert.True(climb.AtBoundary);
    }

    [Fact]
    public void GettingOffTheTopStaysAttachedUntilAnimationOneSeventyEnds()
    {
        HumanLadderClimb climb = new();
        climb.Mount(Ladder(), atTop: true);
        climb.CompleteTopTransition();

        Assert.Equal(LadderStepResult.ReachedTop, climb.Decide(up: true, down: false, slide: false));
        Assert.Equal(LadderTraversalPhase.GettingOffTop, climb.Phase);
        Assert.True(climb.IsOnLadder);

        Assert.True(climb.CompleteTopTransition());
        Assert.False(climb.IsOnLadder);
    }

    [Fact]
    public void SteppingOffTheFootDismountsImmediately()
    {
        HumanLadderClimb climb = MountedAtFoot();

        Assert.Equal(LadderStepResult.Dismounted, climb.Decide(up: false, down: true, slide: false));
        Assert.False(climb.IsOnLadder);
    }

    [Fact]
    public void UseCanEnterTheSlideHalfwayThroughARung()
    {
        HumanLadderClimb climb = MountedAtFoot();
        Assert.Equal(LadderStepResult.SteppingUp, climb.Decide(up: true, down: false, slide: false));

        Assert.Equal(LadderStepResult.Sliding, climb.Decide(up: false, down: false, slide: true));
        Assert.Equal(LadderTraversalPhase.SlidingDown, climb.Phase);
        Assert.True(climb.IsOnLadder);

        Assert.True(climb.CompleteSlide());
        Assert.False(climb.IsOnLadder);
    }

    [Fact]
    public void AStepDirectionCannotBeCancelledBeforeItsAuthoredBoundary()
    {
        HumanLadderClimb climb = MountedAtFoot();
        Assert.Equal(LadderStepResult.SteppingUp, climb.Decide(up: true, down: false, slide: false));
        Assert.False(climb.AtBoundary);
        Assert.Equal(LadderStepResult.Idle, climb.Decide(up: false, down: true, slide: false));

        double startZ = climb.Position.Z;
        climb.Move(new Vector3d(0.0, 0.0, 150.0));
        Assert.Equal(startZ + 150.0, climb.Position.Z, 3);

        Assert.True(climb.CompleteStep());
        Assert.True(climb.AtBoundary);
        Assert.Equal(1, climb.Step);
    }

    [Fact]
    public void HoldingUpCommitsOneStepPerAuthoredBoundary()
    {
        HumanLadderClimb climb = MountedAtFoot();
        int rungs = 0;
        while (rungs < 100)
        {
            LadderStepResult result = climb.Decide(up: true, down: false, slide: false);
            if (result == LadderStepResult.ReachedTop)
            {
                break;
            }

            Assert.Equal(LadderStepResult.SteppingUp, result);
            Assert.True(climb.CompleteStep());
            rungs++;
        }

        Assert.Equal(Ladder().ClimbLine.TopStep, rungs);
    }

    [Fact]
    public void BottomActivationUsesTheLadderPlaneWidthFacingAndHeight()
    {
        LadderPlacement ladder = Ladder();
        Vector3d player = new(Foot.X, Foot.Y + 2048.0, Foot.Z + 100.0);

        Assert.True(ladder.CanActivate(player, MathF.PI, out bool atTop));
        Assert.False(atTop);

        Vector3d tooWide = new(Foot.X + 3000.0, player.Y, player.Z);
        Assert.False(ladder.CanActivate(tooWide, MathF.PI, out _));
        Assert.False(ladder.CanActivate(player, 0f, out _));
    }

    [Fact]
    public void BottomActivationUsesTheViewPositionRatherThanGroundedFeet()
    {
        LadderPlacement ladder = new(
            Foot,
            Head,
            Foot,
            Head,
            Matrix3f.Identity);
        Vector3d groundedFeet = new(Foot.X, Foot.Y + 2048.0, Foot.Z);
        Vector3d standingView = new(
            groundedFeet.X,
            groundedFeet.Y,
            groundedFeet.Z + PlayerBody.StandingEyeHeight);

        Assert.False(ladder.CanActivate(groundedFeet, MathF.PI, out _));
        Assert.True(ladder.CanActivate(standingView, MathF.PI, out bool atTop));
        Assert.False(atTop);
    }

    [Fact]
    public void TopActivationUsesMagicVertexOneInFrontOfThePlayer()
    {
        LadderPlacement ladder = Ladder();
        Vector3d player = new(Head.X, Head.Y - 2048.0, Head.Z);

        Assert.True(ladder.CanActivate(player, 0f, out bool atTop));
        Assert.True(atTop);
    }

    [Fact]
    public void LadderReportsActivateTypeTen()
    {
        Assert.Equal(10, (int)InteractionActivateType.Ladder);
        Assert.Equal(
            InteractionAction.ClimbLadder,
            HumanInteractionDispatch.Dispatch(InteractionActivateType.Ladder).Action);
    }

    [Fact]
    public void ALockedDoorPicksItsLockAndAnUnlockedOneOpens()
    {
        InteractionDispatchResult locked = HumanInteractionDispatch.Dispatch(
            InteractionActivateType.Door, doorLocked: true);
        Assert.Equal(InteractionAction.PickLock, locked.Action);
        Assert.Equal(30, locked.LocomotionState);

        InteractionDispatchResult open = HumanInteractionDispatch.Dispatch(
            InteractionActivateType.Door, doorLocked: false);
        Assert.Equal(InteractionAction.OpenDoor, open.Action);
    }

    [Fact]
    public void TheComputerEntersTheTypecompState()
    {
        InteractionDispatchResult result = HumanInteractionDispatch.Dispatch(InteractionActivateType.Terminal);
        Assert.Equal(InteractionAction.EnterState, result.Action);
        Assert.Equal(32, result.LocomotionState);
    }

    [Fact]
    public void FenceWireAndSwitchTakeTheirOwnActions()
    {
        Assert.Equal(
            InteractionAction.ClimbFence,
            HumanInteractionDispatch.Dispatch(InteractionActivateType.Fence).Action);
        Assert.Equal(
            InteractionAction.MountWire,
            HumanInteractionDispatch.Dispatch(InteractionActivateType.Wire).Action);
        Assert.Equal(
            InteractionAction.PressSwitch,
            HumanInteractionDispatch.Dispatch(InteractionActivateType.Switch).Action);
        Assert.Equal(11, (int)InteractionActivateType.Fence);
        Assert.Equal(9, (int)InteractionActivateType.Wire);
    }

    [Fact]
    public void SteppingOffLeavesTheBodyInTheAirOnItsFeetForTheFallToLand()
    {
        // 0x4113E0 only turns collision back on when a climb ends: the origin stays where the climb left it and the body
        // falls the last few centimetres into state 29, landing with its step. The climb carries the origin, the body
        // its feet; snapping the origin to the first surface below stood a player who had climbed to a roof on the
        // ladder's own top.
        LadderPlacement ladder = Ladder();
        PlayerBody body = new() { Position = new Vector3d(ladder.BottomMount.X, ladder.BottomMount.Y, Foot.Z) };
        PlayerLadderController controller = new(body);
        controller.Use([ladder]);
        body.Yaw = new[] { 0f, MathF.PI / 2f, MathF.PI, -MathF.PI / 2f }.First(yaw => ladder.CanActivate(body.Eye, yaw, out _));
        controller.UpdateNear();
        controller.Mount();

        Assert.Equal(LadderStepResult.Dismounted, controller.DecideStep(up: false, down: true, slide: false));
        Assert.Equal(ladder.BottomMount.Z - PlayerBody.StandingOriginHeight, body.Position.Z, 6);
        Assert.False(body.IsGrounded);
    }

    private static LadderPlacement Ladder() =>
        new(Origin, Head, Foot, Head, Matrix3f.Identity);

    private static HumanLadderClimb MountedAtFoot()
    {
        HumanLadderClimb climb = new();
        climb.Mount(Ladder(), atTop: false);
        return climb;
    }
}
