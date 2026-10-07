using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
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

    /// <summary>The running gaits move between each other by the held directions, as each retail per-tick decides.</summary>
    /// <remarks>Run forward tests right and left before forward, so a lone sideways key still enters the diagonal.</remarks>
    [Theory]
    [InlineData(Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveRight, Igi2PlayerState.RunForwardRight)]
    [InlineData(Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveLeft, Igi2PlayerState.RunForwardLeft)]
    [InlineData(Igi2PlayerAction.MoveRight, Igi2PlayerState.RunForwardRight)]
    [InlineData(Igi2PlayerAction.MoveBackwards, Igi2PlayerState.RunBackward)]
    [InlineData(Igi2PlayerAction.None, Igi2PlayerState.Stand)]
    public void Run_forward_follows_the_held_directions(Igi2PlayerAction held, int expected)
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);

        Tick(player, held, grounded: true);

        // Backward from a forward run passes through standing: run forward has no backward exit.
        int state = player.Locomotion.CurrentState;
        Assert.Equal(held == Igi2PlayerAction.MoveBackwards ? Igi2PlayerState.Stand : expected, state);
    }

    /// <summary>A side run takes a diagonal when forward or back joins it.</summary>
    [Fact]
    public void A_side_run_turns_diagonal()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.MoveRight, grounded: true);
        Assert.Equal(Igi2PlayerState.RunRight, player.Locomotion.CurrentState);

        Tick(player, Igi2PlayerAction.MoveRight | Igi2PlayerAction.MoveBackwards, grounded: true);

        Assert.Equal(Igi2PlayerState.RunBackwardRight, player.Locomotion.CurrentState);
    }

    /// <summary>Changing between neighbouring gaits keeps the clip's place in its cycle.</summary>
    [Fact]
    public void Neighbouring_gaits_keep_their_phase()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);
        animation.MainTime = 480;
        animation.MainTotalTime = 960;

        Tick(player, Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveRight, grounded: true);

        Assert.Equal((Igi2PlayerState.RunForwardRight, 480, 0.2f), animation.Played[^1]);
    }

    /// <summary>Each gait steers toward its own retail targets.</summary>
    [Fact]
    public void Running_backward_steers_to_its_own_target()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.MoveBackwards, grounded: true);

        for (int i = 0; i < 20; i++)
        {
            Tick(player, Igi2PlayerAction.MoveBackwards, grounded: true);
        }

        Assert.Equal(-681.98401f, player.GroundMovementSpeed.Y, 0.01f);
    }

    /// <summary>The walk modifier walks; letting go of it runs on in the same phase of the cycle.</summary>
    [Fact]
    public void The_walk_modifier_switches_between_walk_and_run()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.MoveForwards | Igi2PlayerAction.ModifyWalkRun, grounded: true);
        Assert.Equal(Igi2PlayerState.WalkForward, player.Locomotion.CurrentState);
        Assert.Equal(2, player.RequestedUpperBody);

        animation.MainTime = 300;
        animation.MainTotalTime = 600;
        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);

        Assert.Equal(Igi2PlayerState.RunForward, player.Locomotion.CurrentState);
        Assert.Equal((Igi2PlayerState.RunForward, 300, 0.2f), animation.Played[^1]);
    }

    /// <summary>A walk steers to the walking speed and leaves the clip's rate alone.</summary>
    [Fact]
    public void Walking_forward_steers_to_the_walk_speed()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);
        animation.MainTimeStep = 77;

        for (int i = 0; i < 20; i++)
        {
            Tick(player, Igi2PlayerAction.MoveForwards | Igi2PlayerAction.ModifyWalkRun, grounded: true);
        }

        Assert.Equal(442.36801f, player.GroundMovementSpeed.Y, 0.001f);
        Assert.Equal(77, animation.MainTimeStep);
    }

    /// <summary>Holding crouch crouches, a direction walks crouched at its own speed, and letting go stands.</summary>
    [Fact]
    public void A_held_crouch_walks_crouched_and_stands_on_release()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.Crouch, grounded: true);
        Assert.Equal(Igi2PlayerState.Crouch, player.Locomotion.CurrentState);
        Assert.Equal(Igi2HumanPose.Crouching, player.Pose);
        Assert.Equal(4, player.RequestedUpperBody);

        for (int i = 0; i < 10; i++)
        {
            Tick(player, Igi2PlayerAction.Crouch | Igi2PlayerAction.MoveForwards, grounded: true);
        }

        Assert.Equal(Igi2PlayerState.CrouchWalkForward, player.Locomotion.CurrentState);
        Assert.Equal(5, player.RequestedUpperBody);
        Assert.Equal(221.86667f, player.GroundMovementSpeed.Y, 0.001f);

        Tick(player, Igi2PlayerAction.MoveForwards, grounded: true);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
    }

    /// <summary>Crouched walks make no noise.</summary>
    [Fact]
    public void Crouched_walking_is_silent()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.Crouch, grounded: true);

        for (int i = 0; i < 5; i++)
        {
            Tick(player, Igi2PlayerAction.Crouch | Igi2PlayerAction.MoveRight, grounded: true);
        }

        Assert.Equal(Igi2PlayerState.CrouchWalkRight, player.Locomotion.CurrentState);
        Assert.Equal(0, surroundings.WalkEvents);
    }

    /// <summary>
    /// Letting go of both keys of a forward crouched diagonal goes straight, because it tests the side key
    /// first; the standing walk's diagonal tests forward first and goes sideways.
    /// </summary>
    [Fact]
    public void The_forward_crouched_diagonal_tests_its_side_key_first()
    {
        Igi2HumanPlayer crouched = Igi2PlayerFakes.Player().Player;
        Tick(crouched, Igi2PlayerAction.None, grounded: true);
        Tick(crouched, Igi2PlayerAction.Crouch, grounded: true);
        Tick(crouched, Igi2PlayerAction.Crouch | Igi2PlayerAction.MoveForwards, grounded: true);
        Tick(crouched, Igi2PlayerAction.Crouch | Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveRight, grounded: true);
        Assert.Equal(Igi2PlayerState.CrouchWalkForwardRight, crouched.Locomotion.CurrentState);

        Tick(crouched, Igi2PlayerAction.Crouch, grounded: true);
        Assert.Equal(Igi2PlayerState.CrouchWalkForward, crouched.Locomotion.CurrentState);

        Igi2HumanPlayer walking = Igi2PlayerFakes.Player().Player;
        const Igi2PlayerAction walk = Igi2PlayerAction.ModifyWalkRun;
        Tick(walking, Igi2PlayerAction.None, grounded: true);
        Tick(walking, walk | Igi2PlayerAction.MoveForwards, grounded: true);
        Tick(walking, walk | Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveRight, grounded: true);
        Assert.Equal(Igi2PlayerState.WalkForwardRight, walking.Locomotion.CurrentState);

        Tick(walking, walk, grounded: true);
        Assert.Equal(Igi2PlayerState.WalkRight, walking.Locomotion.CurrentState);
    }

    /// <summary>Crouched with nothing to do in deep water, the player stands up.</summary>
    [Theory]
    [InlineData(3276.7f, Igi2PlayerState.Crouch)]
    [InlineData(3276.8f, Igi2PlayerState.Stand)]
    public void Deep_water_stands_a_crouch_up(float depth, int expected)
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.Crouch, grounded: true);
        player.WaterDepth = depth;

        Tick(player, Igi2PlayerAction.Crouch, grounded: true);

        Assert.Equal(expected, player.Locomotion.CurrentState);
    }

    /// <summary>The crouched turn notes its clip's end only after testing it, so it settles a tick later.</summary>
    [Fact]
    public void A_crouched_turn_settles_a_tick_after_its_clip()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Igi2PlayerFakes.Player();
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.Crouch, grounded: true);
        Tick(player, Igi2PlayerAction.Crouch | Igi2PlayerAction.TurnRight, grounded: true);
        Assert.Equal(Igi2PlayerState.CrouchTurn, player.Locomotion.CurrentState);

        animation.MainEnded = true;
        Tick(player, Igi2PlayerAction.Crouch, grounded: true);
        Assert.Equal(Igi2PlayerState.CrouchTurn, player.Locomotion.CurrentState);

        Tick(player, Igi2PlayerAction.Crouch, grounded: true);
        Assert.Equal(Igi2PlayerState.Crouch, player.Locomotion.CurrentState);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held, bool grounded)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Signal = grounded ? player.Signal | Igi2PlayerSignal.Grounded : player.Signal & ~Igi2PlayerSignal.Grounded;
        player.Locomotion.Tick(player);
    }
}
