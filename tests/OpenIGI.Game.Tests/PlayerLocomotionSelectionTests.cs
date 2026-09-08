using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks that player command gait and stance select their distinct retail locomotion paths.</summary>
public sealed class PlayerLocomotionSelectionTests
{
    [Fact]
    public void RunningForwardSelectsTheRunningState()
    {
        HumanLocomotionAnimator animator = Tick(new PlayerCommand(1f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false));

        AssertStateAndTorsoAction(animator, state: 1, WeaponAction.Run);
    }

    [Fact]
    public void WalkingForwardSelectsTheWalkingState()
    {
        HumanLocomotionAnimator animator = Tick(new PlayerCommand(1f, 0f, 0f, 0f, Run: false, Crouch: false, Jump: false));

        AssertStateAndTorsoAction(animator, state: 9, WeaponAction.Walk);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrouchingForwardUsesTheSeparateCrouchPathRegardlessOfGait(bool run)
    {
        HumanLocomotionAnimator animator = Tick(new PlayerCommand(1f, 0f, 0f, 0f, Run: run, Crouch: true, Jump: false));

        AssertStateAndTorsoAction(animator, state: 20, WeaponAction.CrouchRun);
    }

    [Fact]
    public void StationaryCommandsSelectTheirMatchingIdleStates()
    {
        AssertStateAndTorsoAction(
            Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false)),
            state: 0,
            WeaponAction.Stand);
        AssertStateAndTorsoAction(
            Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: false, Crouch: true, Jump: false)),
            state: 19,
            WeaponAction.Crouch);
    }

    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(0f, 0f, false)]
    [InlineData(1f, 0f, true)]
    [InlineData(1f, 0f, false)]
    [InlineData(0f, 1f, true)]
    [InlineData(-1f, 0f, true)]
    public void AnyStandingJumpReachesTheJumpState(float forward, float strafe, bool run)
    {
        // State 0 tests its directions before the jump edge, but the movement state it hands to tests
        // the jump edge as its next branch and is called in the same tick -- so a moving jump ends in
        // 28 just as a stationary one does.
        Assert.Equal(
            HumanLocomotionGraph.Jump,
            Tick(new PlayerCommand(forward, strafe, 0f, 0f, run, Crouch: false, Jump: true)).State);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(0f, -1f)]
    public void ACrouchedJumpIsRefusedBecauseNoCrouchStateNamesTwentyEight(float forward, float strafe)
    {
        // Rows 19 and 20-27 of the recovered graph contain no 28. The crouch keeps its own state.
        int state = Tick(new PlayerCommand(forward, strafe, 0f, 0f, Run: true, Crouch: true, Jump: true)).State;

        Assert.NotEqual(HumanLocomotionGraph.Jump, state);
        Assert.True(state == HumanLocomotionStates.CrouchingIdle || (state >= 20 && state <= 27));
    }

    [Fact]
    public void TheConfiguredWalkRunToggleSelectsWalkAtThePureSelectorBoundary()
    {
        Assert.True(PlayerInputBindings.TryGetSlot(PlayerAction.ToggleWalkRun, out PlayerInputSlot slot));
        Assert.Equal(PlayerInputSlot.ToggleWalkRun, slot);
        Assert.Equal((uint)PlayerAction.ToggleWalkRun, (uint)HumanMotionState.Walking);

        int state = HumanLocomotionStates.ForDirection(
            HumanMotionState.OnGround | HumanMotionState.Walking | HumanMotionState.MoveForward);

        Assert.Equal(9, state);
        Assert.True(HumanLocomotionWeaponActions.TryGet(state, out WeaponAction action));
        Assert.Equal(WeaponAction.Walk, action);
    }

    private static void AssertStateAndTorsoAction(HumanLocomotionAnimator animator, int state, WeaponAction expectedAction)
    {
        Assert.Equal(state, animator.State);
        Assert.True(HumanLocomotionWeaponActions.TryGet(animator.State, out WeaponAction action));
        Assert.Equal(expectedAction, action);
    }

    private static HumanLocomotionAnimator Tick(PlayerCommand command)
    {
        PlayerBody body = new()
        {
            Position = new Vector3d(0d, 0d, 1000d),
            Walls = Floor,
        };
        Assert.True(body.PlaceOnGround(terrain: null));

        HumanLocomotionAnimator animator = new(BoneAnimationSet.Create(
            BoneSkeleton.Create([BoneSkeleton.NoParent], [default])));
        body.TickAnimated(command, terrain: null, animator);
        return animator;
    }

    private static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        const double floorZ = 0d;
        hit = default;
        if (end.Z >= origin.Z || origin.Z < floorZ || end.Z > floorZ)
        {
            return false;
        }

        hit = new WorldRayHit(
            new Vector3d(origin.X, origin.Y, floorZ),
            new Vector3f(0f, 0f, 1f),
            origin.Z - floorZ);
        return true;
    }
}
