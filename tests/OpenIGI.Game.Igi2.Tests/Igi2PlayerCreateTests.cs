using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Making the player, <c>HumanPlayer_CreateHandler</c>, and its delayed tick, <c>HumanPlayer_RunDelayedHandler</c>.</summary>
public sealed class Igi2PlayerCreateTests
{
    private static readonly Igi2HumanPlayerGeneral Tuning = Igi2HumanPlayerGeneral.Unset with
    {
        MaxDamageScale = 1.5f,
        DeltaTranslationScale = 1.75f,
        DamageMultipliers = [.. Enumerable.Range(1, Igi2HumanDamage.PartCount).Select(static i => i * 0.5f)],
    };

    /// <summary>
    /// The player's health is the tuning's, seven tenths of it on normal and four tenths on hard, multiplied on the 24-bit
    /// FPU: 1.5 by 0.4 truncates to 0.59999996.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy, 1.5f)]
    [InlineData(AiDifficulty.Normal, 1.05f)]
    [InlineData(AiDifficulty.Hard, 0.59999996f)]
    public void Health_follows_the_difficulty(AiDifficulty difficulty, float health)
    {
        Igi2HumanPlayer player = Created(difficulty);

        Assert.Equal(health, player.Damage.MaxDamage);
        Assert.Equal(0f, player.Damage.Damage);
        Assert.Equal(Igi2DamagePart.None, player.Damage.LastPart);
        Assert.Equal(8.5f, player.Damage.Multiplier(Igi2DamagePart.Head));
    }

    /// <summary>The player starts standing, with every collision on, and its first tick enters the stand.</summary>
    [Fact]
    public void The_player_starts_standing()
    {
        Igi2HumanPlayer player = Created(AiDifficulty.Easy);

        Assert.True(player.IsGroundCollision && player.IsObjectCollision && player.IsHumanCollision);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
        Assert.Equal(0f, player.MovementSpeedScale);
    }

    /// <summary>
    /// Carried weapons slow the legs, to half at most, and to half again while the jump wait runs. One less 0.2 truncates
    /// to 0.79999995 on the 24-bit FPU.
    /// </summary>
    [Theory]
    [InlineData(0.2f, 0, 0.79999995f)]
    [InlineData(0.9f, 0, 0.5f)]
    [InlineData(0.2f, 3, 0.39999998f)]
    public void Carried_weapons_slow_the_legs(float slowDown, int jumpWait, float scale)
    {
        Igi2HumanPlayer player = Created(AiDifficulty.Easy, new Igi2PlayerFakes.RecordingWeapon { SummedMovementSlowDown = slowDown });
        player.JumpWaitTimer = jumpWait;

        player.RunDelayed();

        Assert.Equal(scale, player.MovementSpeedScale);
    }

    /// <summary>The weapon's precision settles by the cross-hair damping; the mission score shows the health left.</summary>
    [Fact]
    public void Precision_settles_and_health_is_scored()
    {
        Igi2HumanPlayer player = Created(AiDifficulty.Easy);
        player.IdealWeaponPrecision = 2f;
        player.CrossHairDamping = 0.25f;
        player.Damage.Add(Igi2DamagePart.Chest, 0.375f);

        player.RunDelayed();

        Assert.Equal(0.5f, player.ActualWeaponPrecision);
        Assert.Equal(75, player.MissionHealth);
        Assert.Equal(1, player.PerfectShotTimer);
    }

    private static Igi2HumanPlayer Created(AiDifficulty difficulty, IIgi2PlayerWeaponControl? weapon = null)
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player(weapon);
        player.General = Tuning;
        player.Create(difficulty);
        return player;
    }
}
