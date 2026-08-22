using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the transitions out of locomotion state 0 against <c>0x4087B0</c>.
/// </summary>
public sealed class HumanIdleTransitionTests
{
    private const HumanMotionState Grounded = HumanMotionState.OnGround;

    [Fact]
    public void AnIdleGroundedHumanStaysPut() =>
        Assert.Equal(HumanIdleTransitions.Stay, HumanIdleTransitions.Next(Grounded, jumpEdge: false));

    [Fact]
    public void LosingTheGroundBeatsEverythingElse()
    {
        // First test in the chain: even a dying human that is also moving and jumping falls.
        HumanMotionState everything =
            HumanMotionState.Death46 | HumanMotionState.Death47 |
            HumanMotionState.MoveForward | HumanMotionState.Crouch;

        Assert.Equal(
            HumanIdleTransitions.Falling,
            HumanIdleTransitions.Next(everything, jumpEdge: true));
    }

    [Fact]
    public void DeathBeatsMovement()
    {
        Assert.Equal(46, HumanIdleTransitions.Next(Grounded | HumanMotionState.Death46 | HumanMotionState.MoveForward, false));
        Assert.Equal(47, HumanIdleTransitions.Next(Grounded | HumanMotionState.Death47 | HumanMotionState.MoveForward, false));

        // 46 is tested before 47.
        Assert.Equal(
            46,
            HumanIdleTransitions.Next(Grounded | HumanMotionState.Death46 | HumanMotionState.Death47, false));
    }

    [Fact]
    public void TheFourDirectionsMapToTheirRunningStates()
    {
        Assert.Equal(1, HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveForward, false));
        Assert.Equal(4, HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveBackward, false));
        Assert.Equal(7, HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveRight, false));
        Assert.Equal(8, HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveLeft, false));
    }

    [Fact]
    public void WalkingRetargetsAllFourAtOnce()
    {
        HumanMotionState walking = Grounded | HumanMotionState.Walking;

        Assert.Equal(9, HumanIdleTransitions.Next(walking | HumanMotionState.MoveForward, false));
        Assert.Equal(12, HumanIdleTransitions.Next(walking | HumanMotionState.MoveBackward, false));
        Assert.Equal(15, HumanIdleTransitions.Next(walking | HumanMotionState.MoveRight, false));
        Assert.Equal(16, HumanIdleTransitions.Next(walking | HumanMotionState.MoveLeft, false));
    }

    [Fact]
    public void TheDirectionsAreTestedInAFixedOrder()
    {
        // Holding two directions at once takes the earlier test, not a diagonal.
        Assert.Equal(
            1,
            HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveForward | HumanMotionState.MoveLeft, false));
        Assert.Equal(
            4,
            HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveBackward | HumanMotionState.MoveRight, false));
    }

    [Fact]
    public void MovementBeatsTheJumpEdge()
    {
        // The jump test comes after all four directions, so holding a direction the tick the jump
        // button goes down takes the run state and the jump edge is spent.
        Assert.Equal(
            1,
            HumanIdleTransitions.Next(Grounded | HumanMotionState.MoveForward, jumpEdge: true));

        Assert.Equal(
            HumanIdleTransitions.Jump,
            HumanIdleTransitions.Next(Grounded, jumpEdge: true));
    }

    [Fact]
    public void TheJumpEdgeBeatsCrouching()
    {
        Assert.Equal(
            HumanIdleTransitions.Jump,
            HumanIdleTransitions.Next(Grounded | HumanMotionState.Crouch, jumpEdge: true));

        Assert.Equal(
            HumanIdleTransitions.CrouchingIdle,
            HumanIdleTransitions.Next(Grounded | HumanMotionState.Crouch, jumpEdge: false));
    }

    [Fact]
    public void TheJumpStateIsTheOneThatPlaysTheJumpAnimation()
    {
        // State 28 is where the entry table binds animation 25 and the entry function spawns "jump_1".
        Assert.Equal(28, HumanIdleTransitions.Jump);
        Assert.Equal(25, HumanLocomotionStates.For(HumanIdleTransitions.Jump).AnimationId);
    }

    [Fact]
    public void TheDeathStatesAreTheSelfDrivingOnes()
    {
        // Both targets of the two death flags carry animation id 0 — they name their clip by string.
        Assert.True(HumanLocomotionStates.DrivesItself(46));
        Assert.True(HumanLocomotionStates.DrivesItself(47));
    }

    [Fact]
    public void EveryTargetIsARealState()
    {
        int[] targets = [29, 46, 47, 1, 4, 7, 8, 9, 12, 15, 16, 28, 19];
        Assert.All(targets, t => Assert.InRange(t, 0, HumanLocomotionStates.Count - 1));
    }

    [Fact]
    public void ANonMovementBitIsRejectedByTheDirectionLookup() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HumanIdleTransitions.ForDirection(HumanMotionState.Crouch, walking: false));
}
