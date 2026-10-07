using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The player's locomotion states, against the retail state functions they port.</summary>
public sealed class Igi2PlayerLocomotionTests
{
    /// <summary>The ground speed moves toward its target by at most the step, in the body's frame.</summary>
    [Fact]
    public void The_legs_accelerate_by_the_step_toward_the_target()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;

        player.MoveOnGround(890.74341f, 118.76579f, 0f, 63.624531f);
        Assert.Equal(new Vector3f(0f, 118.76579f, 0f), player.GroundMovementSpeed);

        for (int i = 0; i < 10; i++)
        {
            player.MoveOnGround(890.74341f, 118.76579f, 0f, 63.624531f);
        }

        Assert.Equal(890.74341f, player.GroundMovementSpeed.Y, 0.001f);
    }

    /// <summary>The speed is steered in the body's frame, so a turned body accelerates along its own forward.</summary>
    [Fact]
    public void A_turned_body_accelerates_along_its_own_forward()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        player.Orientation = Matrix3f.FromRows(new Vector3f(0f, 1f, 0f), new Vector3f(-1f, 0f, 0f), new Vector3f(0f, 0f, 1f));

        player.MoveOnGround(100f, 1000f, 0f, 0f);

        // Local Y = (0, 1, 0) in the body maps through the rows to world (1, 0, 0).
        Assert.Equal(new Vector3f(100f, 0f, 0f), player.GroundMovementSpeed);
    }

    /// <summary>The gait slows uphill: (1 − clamp(factor, −0.2, 1)) × 160 × scale, truncated.</summary>
    [Theory]
    [InlineData(0f, 1f, 160)]
    [InlineData(0.5f, 1.2f, 96)]
    [InlineData(-1f, 1f, 192)]
    [InlineData(2f, 1f, 0)]
    public void The_gait_rate_follows_the_slope(float factor, float scale, int expected)
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        player.GroundAngleFactor = factor;

        player.ControlGait(scale);

        Assert.Equal(expected, animation.MainTimeStep);
    }

    /// <summary>Holding forward on the ground leaves standing for running forward, with the run clip blended in.</summary>
    [Fact]
    public void Forward_from_standing_runs()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);

        Assert.Equal(Igi2PlayerState.RunForward, player.Locomotion.CurrentState);
        Assert.Equal((Igi2PlayerState.RunForward, 0, 0.2f), animation.Played[^1]);
        Assert.Equal(3, player.RequestedUpperBody);
    }

    /// <summary>Off the ground, standing falls before it looks at anything else.</summary>
    [Fact]
    public void Standing_without_ground_falls()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.MoveForwards, grounded: false);

        Assert.Equal(Igi2PlayerState.Fall, player.Locomotion.CurrentState);
    }

    /// <summary>Running with nothing held stops; still holding forward makes a noise the AI can hear.</summary>
    [Fact]
    public void Running_makes_noise_until_released()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);

        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);
        Assert.Equal(1, surroundings.WalkEvents);

        Tick(player, Igi2PlayerAction.None, grounded: true);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
    }

    /// <summary>Lie-down is refused when the body would not fit, and the posture is left as it was.</summary>
    [Fact]
    public void Lying_down_needs_room()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        surroundings.Fits = false;
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.ToggleStandLie, grounded: true);

        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
        Assert.Equal(Igi2HumanPose.Standing, player.Pose);
    }

    /// <summary>The turn on the spot holds until its clip has played out and the turning has stopped.</summary>
    [Fact]
    public void A_turn_settles_after_its_clip()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.TurnRight, grounded: true);
        Assert.Equal(Igi2PlayerState.StandTurn, player.Locomotion.CurrentState);
        Assert.True(animation.Looping);

        Tick(player, Igi2PlayerAction.None, grounded: true);
        Assert.Equal(Igi2PlayerState.StandTurn, player.Locomotion.CurrentState);

        animation.MainEnded = true;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held, bool grounded)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Signal = grounded ? player.Signal | Igi2PlayerSignal.Grounded : player.Signal & ~Igi2PlayerSignal.Grounded;
        player.Locomotion.Tick(player);
    }
}
