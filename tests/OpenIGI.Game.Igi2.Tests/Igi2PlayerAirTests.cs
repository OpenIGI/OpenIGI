using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Jumping, falling and landing, against <c>HumanPlayer_InAirControl</c> and the jump and fall states.</summary>
public sealed class Igi2PlayerAirTests
{
    private static readonly Igi2HumanPlayerGeneral Tuning = Igi2HumanPlayerGeneral.Unset with
    {
        UpwardJumpSpeed = 834.3703002929688f,
        InAirControlSpeed = 7.585184574127197f,
    };

    /// <summary>A jump starts with the carried speed plus the upward jump speed, off the ground.</summary>
    [Fact]
    public void A_jump_takes_off_with_the_carried_speed()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Player();
        player.AccumulatedSpeed = new Vector3f(100f, 0f, 5f);
        player.GroundIntersection.IsHit = true;
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.Jump, grounded: true);

        Assert.Equal(Igi2PlayerState.Jump, player.Locomotion.CurrentState);
        Assert.Equal(new Vector3f(100f, 0f, 839.3703002929688f), player.Speed);
        Assert.False(player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.Equal(0f, player.GroundTolerance);
        Assert.False(player.GroundIntersection.IsHit);
        Assert.Null(player.GroundCollider);
        Assert.Equal(["jump_1"], surroundings.Sounds);
    }

    /// <summary>There is no jumping off a lift: the jump's own exit runs and the player stands.</summary>
    [Fact]
    public void A_jump_from_a_lift_ends_before_it_starts()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Player();
        surroundings.IsOnElevator = true;
        player.Speed = new Vector3f(1f, 2f, 3f);
        Tick(player, Igi2PlayerAction.None, grounded: true);

        Tick(player, Igi2PlayerAction.Jump, grounded: true);

        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
        Assert.Equal(default, player.Speed);
        Assert.Equal(Igi2HumanPlayer.LandingGroundTolerance, player.GroundTolerance);
        Assert.Empty(surroundings.Sounds);
    }

    /// <summary>Each tick in the air takes the gravity step off the vertical and adds the held direction along the body.</summary>
    [Fact]
    public void Gravity_and_air_control_act_each_tick()
    {
        Igi2HumanPlayer player = Player().Player;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.None, grounded: false);
        Assert.Equal(Igi2PlayerState.Fall, player.Locomotion.CurrentState);
        player.Speed = new Vector3f(0f, 0f, -100f);

        Tick(player, Igi2PlayerAction.MoveForwards, grounded: false);

        Assert.Equal(-184.74168395996094f, player.Speed.Z);
        Assert.Equal(7.585184574127197f, player.Speed.Y);
        Assert.Equal(0f, player.Speed.X);
    }

    /// <summary>
    /// A landing over 16.5 m/s costs 7% of the maximum damage per m/s over, on the chest, and kicks the view
    /// down by up to 1024.
    /// </summary>
    [Fact]
    public void A_hard_landing_hurts_and_kicks_the_view()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Fall(maxDamage: 100f);
        player.Speed = new Vector3f(0f, 0f, -4096f);

        Tick(player, Igi2PlayerAction.None, grounded: true);

        Assert.Equal(98.8446044921875f, player.Damage.Damage);
        Assert.Equal(98.8446044921875f, player.Damage.PartDamage(Igi2DamagePart.Chest));
        Assert.Equal(-1024f, player.View.ImpactOffset);
        Assert.Contains("player_fall_3", surroundings.Sounds);
        Assert.Equal(1, surroundings.LandingEvents);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
        Assert.Equal(default, player.Speed);
    }

    /// <summary>A landing that reaches the limit kills: the dead bit is raised and the fall ends in dying.</summary>
    [Fact]
    public void A_fatal_landing_dies()
    {
        (Igi2HumanPlayer player, _, _) = Fall(maxDamage: 100f);
        player.Speed = new Vector3f(0f, 0f, -8192f);

        Tick(player, Igi2PlayerAction.None, grounded: true);

        Assert.True(player.IsDead);
        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Dead));
        Assert.Equal(Igi2PlayerState.Die, player.Locomotion.CurrentState);
    }

    /// <summary>Protected from fall damage, only the vertical counts toward the impact; otherwise the whole speed does.</summary>
    [Theory]
    [InlineData(true, 0f)]
    [InlineData(false, 7f)]
    public void Protection_measures_only_the_vertical(bool isProtected, float minimumDamage)
    {
        (Igi2HumanPlayer player, _, _) = Fall(maxDamage: 100f, isProtected);
        player.Speed = new Vector3f(4096f, 0f, -100f);

        Tick(player, Igi2PlayerAction.None, grounded: true);

        Assert.True(player.Damage.Damage >= minimumDamage);
        Assert.Equal(isProtected, player.Damage.Damage == 0f);
    }

    /// <summary>Landing in water splashes rather than playing the ground's sound.</summary>
    [Fact]
    public void A_landing_in_water_splashes()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Fall(maxDamage: 100f);
        surroundings.Water = 300f;
        surroundings.LandingSounds[3] = "land_concrete";
        player.GroundIntersection.EnterMaterial = 3;

        Tick(player, Igi2PlayerAction.None, grounded: true);

        Assert.Equal(["water_land_01"], surroundings.Sounds);
        Assert.Equal(300f, player.WaterDepth);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeAnimation Animation, Igi2PlayerFakes.FakeSurroundings Surroundings) Player()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        player.General = Tuning;
        return (player, animation, surroundings);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeAnimation Animation, Igi2PlayerFakes.FakeSurroundings Surroundings) Fall(
        float maxDamage, bool isProtected = false)
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, Igi2PlayerFakes.FakeSurroundings surroundings) = Player();
        player.Damage.MaxDamage = maxDamage;
        player.IsProtectedFromFallDamage = isProtected;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Tick(player, Igi2PlayerAction.None, grounded: false);
        Tick(player, Igi2PlayerAction.None, grounded: false);
        return (player, animation, surroundings);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held, bool grounded)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Signal = grounded ? player.Signal | Igi2PlayerSignal.Grounded : player.Signal & ~Igi2PlayerSignal.Grounded;
        player.Locomotion.Tick(player);
    }
}
