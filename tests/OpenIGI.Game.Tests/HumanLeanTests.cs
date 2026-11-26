using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail lean: the weapon machine's peek forcing body state 44 (<c>0x40F370</c>, <c>0x40F3F0</c>), and the view's commit
/// moving and sweeping the eye by it (<c>0x464BD0</c>).
/// </summary>
public sealed class HumanLeanTests
{
    private const HumanMotionState Ground = HumanMotionState.OnGround;
    private static readonly Vector3d Eye = new(1000.0, 2000.0, 7000.0);

    [Fact]
    public void TheLeanHoldsTheBodyInStateFortyFourWhateverTheKeysAndTheGround()
    {
        Assert.Equal(HumanBodyStateMachine.Lean, HumanBodyStateMachine.Next(1, Ground | HumanMotionState.MoveForward, false, leaning: true));
        Assert.Equal(HumanBodyStateMachine.Lean, HumanBodyStateMachine.Next(HumanBodyStateMachine.Lean, HumanMotionState.None, true, leaning: true));
    }

    [Fact]
    public void LettingGoStandsTheBodyAndRunsTheStandsOwnTestsTheSameTick()
    {
        Assert.Equal(HumanBodyStateMachine.Stand, HumanBodyStateMachine.Next(HumanBodyStateMachine.Lean, Ground, false, leaning: false));
        Assert.Equal(
            HumanBodyStateMachine.Crouch,
            HumanBodyStateMachine.Next(HumanBodyStateMachine.Lean, Ground | HumanMotionState.Crouch, false, leaning: false));
        Assert.Equal(1, HumanBodyStateMachine.Next(HumanBodyStateMachine.Lean, Ground | HumanMotionState.MoveForward, false, leaning: false));
    }

    [Fact]
    public void TheLeansEntryPlaysTheCrouchWhileCrouchedAndKeepsTheCrouchByte()
    {
        Assert.Equal(HumanBodyStateMachine.Crouch, HumanBodyStateMachine.SlotFor(HumanBodyStateMachine.Lean, crouched: true));
        Assert.Equal(HumanBodyStateMachine.Lean, HumanBodyStateMachine.SlotFor(HumanBodyStateMachine.Lean, crouched: false));
        Assert.Equal(HumanBodyStateMachine.Crouch, HumanBodyStateMachine.SlotFor(HumanBodyStateMachine.Crouch, crouched: true));
        Assert.True(HumanBodyStateMachine.KeepsCrouch(HumanBodyStateMachine.Lean));
        Assert.False(HumanBodyStateMachine.KeepsCrouch(HumanBodyStateMachine.Stand));
    }

    [Fact]
    public void AnUnleanedEyeIsTheBodysExactlyAndNothingIsSwept()
    {
        WorldRayCast mustNotSweep = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            throw new InvalidOperationException("An unleaned eye must not be swept.");
        Assert.Equal(Eye, HumanLeanEye.Place(Eye, Matrix3f.Identity, 0f, 0f, 0f, mustNotSweep));
    }

    [Fact]
    public void TheSideLeanRunsAlongTheEyesRightAndTheRaiseOnWorldZ()
    {
        // Turned a quarter: the first column -- the right -- points along world -Y.
        Matrix3f turned = Matrix3f.FromRows(new Vector3f(0f, 1f, 0f), new Vector3f(-1f, 0f, 0f), new Vector3f(0f, 0f, 1f));
        Vector3d placed = HumanLeanEye.Place(Eye, turned, 3000f, 0f, 500f, walls: null);
        Assert.Equal(Eye.X, placed.X, 6);
        Assert.Equal(Eye.Y - 3000.0, placed.Y, 6);
        Assert.Equal(Eye.Z + 500.0, placed.Z, 6);
    }

    [Fact]
    public void AWallStopsTheEyeTwoHundredAndFourPointEightShortOfTheHit()
    {
        WorldRayCast wall = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = new WorldRayHit(origin + new Vector3d(1000.0, 0.0, 0.0), new Vector3f(-1f, 0f, 0f), 1000.0);
            return true;
        };

        Vector3d placed = HumanLeanEye.Place(Eye, Matrix3f.Identity, 3481.6f, 0f, 0f, wall);
        Assert.Equal(Eye.X + 1000.0 - HumanLeanEye.WallGapUnits, placed.X, 6);
        Assert.Equal(Eye.Y, placed.Y, 6);
    }

    [Fact]
    public void AWallCloserThanTheGapLeavesTheEyeWhereTheBodysIs()
    {
        WorldRayCast wall = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = new WorldRayHit(origin, new Vector3f(-1f, 0f, 0f), 100.0);
            return true;
        };

        Assert.Equal(Eye, HumanLeanEye.Place(Eye, Matrix3f.Identity, 3481.6f, 0f, 0f, wall));
    }
}
