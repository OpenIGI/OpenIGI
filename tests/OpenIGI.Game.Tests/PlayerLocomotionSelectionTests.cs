using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.Player;

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
        // State 0 tests the directions before the crouch key, and an entry does not run its state's per-tick: the first tick
        // steps into the gait, the second crouches it.
        PlayerCommand command = new(1f, 0f, 0f, 0f, Run: run, Crouch: true, Jump: false);
        HumanLocomotionAnimator animator = Tick(command);
        Assert.Equal(run ? 1 : 9, animator.State);

        AssertStateAndTorsoAction(Tick(command, command), state: 20, WeaponAction.CrouchRun);
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
        // Jump pressed in the gait the keys already hold: every standing state tests the edge. (From a standstill the
        // directions win: state 0 tests them first, and the gait it enters does not run until the next tick.)
        PlayerCommand moving = new(forward, strafe, 0f, 0f, run, Crouch: false, Jump: false);
        Assert.Equal(
            HumanLocomotionGraph.Jump,
            Tick(moving, moving with { Jump = true }).State);
    }

    [Fact]
    public void AJumpPressedAsTheFirstStepIsTakenAsTheStep()
    {
        Assert.Equal(1, Tick(new PlayerCommand(1f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: true)).State);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(0f, -1f)]
    public void ACrouchedJumpIsRefusedBecauseNoCrouchStateNamesTwentyEight(float forward, float strafe)
    {
        // Rows 19 and 20-27 of the recovered graph contain no 28. The crouch keeps its own state.
        PlayerCommand crouched = new(forward, strafe, 0f, 0f, Run: true, Crouch: true, Jump: false);
        int state = Tick(crouched, crouched, crouched, crouched with { Jump = true }).State;

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

    private static HumanLocomotionAnimator Tick(params PlayerCommand[] commands)
    {
        PlayerBody body = new()
        {
            Position = new Vector3d(0d, 0d, 1000d),
            Walls = Floor,
        };
        Assert.True(body.PlaceOnGround(terrain: null));

        HumanLocomotionAnimator animator = new(BoneAnimationSet.Create(
            BoneSkeleton.Create([BoneSkeleton.NoParent], [default])));
        foreach (PlayerCommand command in commands)
        {
            body.TickAnimated(command, terrain: null, animator);
        }

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
