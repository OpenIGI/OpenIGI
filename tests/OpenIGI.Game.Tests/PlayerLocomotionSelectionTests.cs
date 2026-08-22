using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
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

        Assert.Equal(1, animator.State);
    }

    [Fact]
    public void WalkingForwardSelectsTheWalkingState()
    {
        HumanLocomotionAnimator animator = Tick(new PlayerCommand(1f, 0f, 0f, 0f, Run: false, Crouch: false, Jump: false));

        Assert.Equal(9, animator.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrouchingForwardUsesTheSeparateCrouchPathRegardlessOfGait(bool run)
    {
        HumanLocomotionAnimator animator = Tick(new PlayerCommand(1f, 0f, 0f, 0f, Run: run, Crouch: true, Jump: false));

        Assert.Equal(20, animator.State);
    }

    [Fact]
    public void StationaryCommandsSelectTheirMatchingIdleStates()
    {
        Assert.Equal(0, Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false)).State);
        Assert.Equal(19, Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: false, Crouch: true, Jump: false)).State);
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
