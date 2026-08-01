using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the ladder climb geometry, the climb state machine and the interaction dispatcher against the
/// IGI1 handlers.
/// </summary>
public sealed class LadderInteractionTests
{
    // A 3 m ladder: 12288 units tall, which the constructor divides by 1228.8 into exactly ten steps.
    private static readonly Vector3d Foot = new(1000.0, 2000.0, 0.0);
    private static readonly Vector3d Head = new(1000.0, 2000.0, 12288.0);

    [Fact]
    public void StepCountIsHeightOverOneStepTruncated()
    {
        LadderClimbLine line = new(Foot, Head);
        Assert.Equal(10, line.StepCount);
        Assert.Equal(4, line.TopStep); // 10 - TopStepMargin(6)
    }

    [Fact]
    public void StepCountTruncatesRatherThanRounds()
    {
        // 11.9 steps' worth of height is eleven whole steps, not twelve — the constructor uses fistp.
        LadderClimbLine line = new(Foot, new Vector3d(Foot.X, Foot.Y, LadderClimbLine.StepLengthUnits * 11.9));
        Assert.Equal(11, line.StepCount);
    }

    [Fact]
    public void PositionInterpolatesAlongTheSegment()
    {
        LadderClimbLine line = new(Foot, Head);
        Assert.Equal(Foot, line.PositionAtStep(0));
        Assert.Equal(Head, line.PositionAtStep(line.StepCount));

        Vector3d mid = line.PositionAtStep(5);
        Assert.Equal(6144.0, mid.Z, 3); // halfway up
    }

    [Fact]
    public void MountingAtTheFootStartsAtStepZero()
    {
        HumanLadderClimb climb = new();
        climb.Mount(new LadderClimbLine(Foot, Head), facing: 0f, atTop: false);

        Assert.True(climb.IsOnLadder);
        Assert.Equal(0, climb.Step);

        // Stood back from the ladder (facing 0 → back is −Y) and lifted up it — the retail foot mount (probe).
        Assert.Equal(Foot.X, climb.Position.X, 3);
        Assert.Equal(Foot.Y - HumanLadderClimb.HorizontalStandoffUnits, climb.Position.Y, 3);
        Assert.Equal(Foot.Z + HumanLadderClimb.FootMountLiftUnits, climb.Position.Z, 3);
    }

    [Fact]
    public void MountingAtTheTopStartsAtTheTopStep()
    {
        HumanLadderClimb climb = new();
        LadderClimbLine line = new(Foot, Head);
        climb.Mount(line, facing: 0f, atTop: true);

        Assert.True(climb.IsOnLadder);
        Assert.True(climb.AtBoundary);
        Assert.Equal(line.TopStep, climb.Step);
    }

    [Fact]
    public void AtTheTopStepUpHandsOff()
    {
        HumanLadderClimb climb = new();
        LadderClimbLine line = new(Foot, Head);
        climb.Mount(line, facing: 0f, atTop: true);

        // At the top step, pressing up hands off to getting off the head.
        Assert.Equal(LadderStepResult.ReachedTop, climb.Decide(up: true, down: false, slide: false));
        Assert.False(climb.IsOnLadder);
    }

    [Fact]
    public void SteppingOffTheFootDismounts()
    {
        HumanLadderClimb climb = new();
        climb.Mount(new LadderClimbLine(Foot, Head), facing: 0f, atTop: false);

        Assert.Equal(LadderStepResult.Dismounted, climb.Decide(up: false, down: true, slide: false));
        Assert.False(climb.IsOnLadder);
    }

    [Fact]
    public void HoldingUseSlides()
    {
        HumanLadderClimb climb = new();
        climb.Mount(new LadderClimbLine(Foot, Head), facing: 0f, atTop: false);

        Assert.Equal(LadderStepResult.Sliding, climb.Decide(up: false, down: false, slide: true));
    }

    [Fact]
    public void ARungCannotBeCancelledMidStep()
    {
        HumanLadderClimb climb = new();
        climb.Mount(new LadderClimbLine(Foot, Head), facing: 0f, atTop: false);

        // Begin a rung up: now mid-step, no longer at a boundary.
        Assert.Equal(LadderStepResult.SteppingUp, climb.Decide(up: true, down: false, slide: false));
        Assert.False(climb.AtBoundary);
        Assert.Equal(0, climb.Step);

        // Pressing down mid-rung is ignored — the rung plays to completion.
        Assert.Equal(LadderStepResult.Idle, climb.Decide(up: false, down: true, slide: false));

        // The clip's root motion moves the body up (the direction is applied inside Move).
        double startZ = climb.Position.Z;
        climb.Move(new Vector3d(0.0, 0.0, 150.0));
        Assert.Equal(startZ + 150.0, climb.Position.Z, 3);

        // The clip completes: the rung commits and we are back at a boundary on rung 1.
        Assert.True(climb.CompleteStep());
        Assert.True(climb.AtBoundary);
        Assert.Equal(1, climb.Step);
    }

    [Fact]
    public void HoldingUpClimbsOneRungPerCompletedClip()
    {
        HumanLadderClimb climb = new();
        LadderClimbLine line = new(Foot, Head);
        climb.Mount(line, facing: 0f, atTop: false);

        // Holding up: each completed clip advances exactly one rung, until the top step hands off.
        int rungs = 0;
        while (rungs < 100)
        {
            LadderStepResult result = climb.Decide(up: true, down: false, slide: false);
            if (result == LadderStepResult.ReachedTop)
            {
                break;
            }

            Assert.Equal(LadderStepResult.SteppingUp, result);
            climb.CompleteStep();
            rungs++;
        }

        Assert.Equal(line.TopStep, rungs);
    }

    [Fact]
    public void LadderReportsActivateTypeTen()
    {
        // The ladder's GetActivate handler (sub_4406C0) writes 10.
        Assert.Equal(10, (int)InteractionActivateType.Ladder);
        Assert.Equal(InteractionAction.ClimbLadder, HumanInteractionDispatch.Dispatch(InteractionActivateType.Ladder).Action);
    }

    [Fact]
    public void ALockedDoorPicksItsLockAndAnUnlockedOneOpens()
    {
        InteractionDispatchResult locked = HumanInteractionDispatch.Dispatch(InteractionActivateType.Door, doorLocked: true);
        Assert.Equal(InteractionAction.PickLock, locked.Action);
        Assert.Equal(30, locked.LocomotionState); // picklock_loop

        InteractionDispatchResult open = HumanInteractionDispatch.Dispatch(InteractionActivateType.Door, doorLocked: false);
        Assert.Equal(InteractionAction.OpenDoor, open.Action);
    }

    [Fact]
    public void TheComputerEntersTheTypecompState()
    {
        InteractionDispatchResult result = HumanInteractionDispatch.Dispatch(InteractionActivateType.Computer);
        Assert.Equal(InteractionAction.EnterState, result.Action);
        Assert.Equal(32, result.LocomotionState); // typecomp_loop
    }

    [Fact]
    public void FenceAndDrawerTakeTheirOwnActions()
    {
        Assert.Equal(InteractionAction.VaultFence, HumanInteractionDispatch.Dispatch(InteractionActivateType.Fence).Action);
        Assert.Equal(InteractionAction.OpenDrawer, HumanInteractionDispatch.Dispatch(InteractionActivateType.Drawer).Action);
    }
}
