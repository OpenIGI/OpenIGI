using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Flinching and dying, against <c>HumanPlayer_*StateHit</c> and <c>*StateDie</c>.</summary>
public sealed class Igi2PlayerHarmTests
{
    private const uint SeedA = 0xD1EA0316;
    private const uint SeedB = 0xC672F756;
    private const uint SeedC = 0x5DBF1ACD;

    /// <summary>A standing flinch plays the hit zone's clip, draws once, and plays at two-fifteenths of its length per tick.</summary>
    [Fact]
    public void A_standing_flinch_plays_the_zones_clip()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Player(out IgiRandom twin);
        animation.MainTotalTime = 1500;
        player.Damage.LastPart = Igi2DamagePart.Head;

        Hurt(player, Igi2PlayerSignal.Hit);

        Assert.Equal(Igi2PlayerState.Hit, player.Locomotion.CurrentState);
        Assert.Equal((64, 0, 0.2f), animation.Played[^1]);
        Assert.Equal(200, animation.MainTimeStep);
        twin.NextRaw();
        Assert.Equal(twin.NextRaw(), player.Random.NextRaw());
    }

    /// <summary>A crouched flinch plays the crouched clip and still makes the draw.</summary>
    [Fact]
    public void A_crouched_flinch_still_draws()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Player(out IgiRandom twin);
        Tick(player, Igi2PlayerAction.Crouch);

        player.Signal |= Igi2PlayerSignal.Hit;
        Tick(player, Igi2PlayerAction.Crouch);

        Assert.Equal((Igi2HumanPlayer.CrouchHitAnimation, 0, 0.2f), animation.Played[^1]);
        twin.NextRaw();
        Assert.Equal(twin.NextRaw(), player.Random.NextRaw());
    }

    /// <summary>A flinch ends when its clip does, back in the posture it started in.</summary>
    [Fact]
    public void A_flinch_ends_with_its_clip()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Player(out _);
        Hurt(player, Igi2PlayerSignal.Hit);
        player.Signal &= ~Igi2PlayerSignal.Hit;

        animation.MainEnded = true;
        Tick(player, Igi2PlayerAction.None);

        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
    }

    /// <summary>
    /// A standing death draws one of eight clips, locks the body and the view, lets go of the triggers,
    /// and cries out with a second draw.
    /// </summary>
    [Fact]
    public void A_standing_death_draws_a_clip_and_a_cry()
    {
        Igi2PlayerFakes.RecordingWeapon weapon = new();
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, Igi2PlayerFakes.FakeSurroundings surroundings) = Player(out IgiRandom twin, weapon);
        player.TotalSpeed = new Vector3f(1f, 2f, 3f);

        Hurt(player, Igi2PlayerSignal.Dead);

        Assert.Equal(Igi2PlayerState.Die, player.Locomotion.CurrentState);
        Assert.Equal((92 + (int)twin.NextBounded(8), 0, 0.2f), animation.Played[^1]);
        Assert.Equal($"player_death_{twin.NextBounded(3)}", surroundings.Sounds[^1]);
        Assert.Equal(["ClearOverlay", "ReleaseTriggers"], weapon.Calls);
        Assert.Equal(5, player.CameraMode);
        Assert.True(player.IsLockAction);
        Assert.True(player.View.IsLockOrientation);
        Assert.Equal(new Vector3f(1f, 2f, 3f), player.Speed);
    }

    /// <summary>
    /// A fall that lands fatally dies with the mid-air clip; the body then falls until it rests on the
    /// ground, and only then are the weapons taken away. A fall never looks at the dead bit itself.
    /// </summary>
    [Fact]
    public void A_death_in_the_air_lands_before_the_weapons_go()
    {
        Igi2PlayerFakes.RecordingWeapon weapon = new();
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = Player(out _, weapon);
        Tick(player, Igi2PlayerAction.None, grounded: false);
        Assert.Equal(Igi2PlayerState.Fall, player.Locomotion.CurrentState);

        player.Damage.MaxDamage = 100f;
        player.Damage.Damage = 100f;
        player.Signal |= Igi2PlayerSignal.Dead;
        Tick(player, Igi2PlayerAction.None, grounded: false);
        Assert.Equal(Igi2PlayerState.Fall, player.Locomotion.CurrentState);

        Tick(player, Igi2PlayerAction.None, grounded: true);
        Assert.Equal(Igi2PlayerState.Die, player.Locomotion.CurrentState);
        Assert.Equal((Igi2HumanPlayer.AirDeathAnimation, 0, 0.2f), animation.Played[^1]);
        Assert.True(player.IsDead);

        Tick(player, Igi2PlayerAction.None, grounded: false);
        Assert.DoesNotContain("ClearAllWeapons", weapon.Calls);

        Tick(player, Igi2PlayerAction.None, grounded: true);
        Assert.Contains("ClearAllWeapons", weapon.Calls);
        Assert.Equal(default, player.Speed);

        animation.MainEnded = true;
        Tick(player, Igi2PlayerAction.None, grounded: true);
        Assert.True(player.IsDeathAnimationDone);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeAnimation Animation, Igi2PlayerFakes.FakeSurroundings Surroundings) Player(
        out IgiRandom twin, IIgi2PlayerWeaponControl? weapon = null)
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player(weapon);
        player.Random = new IgiRandom(SeedA, SeedB, SeedC);
        twin = new IgiRandom(SeedA, SeedB, SeedC);
        Tick(player, Igi2PlayerAction.None);
        return (player, animation, surroundings);
    }

    private static void Hurt(Igi2HumanPlayer player, Igi2PlayerSignal harm)
    {
        player.Signal |= harm;
        Tick(player, Igi2PlayerAction.None);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held, bool grounded = true)
    {
        Igi2PlayerFakes.Feed(player, held);
        player.Signal = grounded ? player.Signal | Igi2PlayerSignal.Grounded : player.Signal & ~Igi2PlayerSignal.Grounded;
        player.Locomotion.Tick(player);
    }
}
