using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The landing equation and effect thresholds transcribed from retail <c>0x408460</c>.</summary>
public sealed class HumanFallDamageTests
{
    private const float PlayerMaximumHealth = 3f;

    [Fact]
    public void FourteenMetresPerSecondIsTheLastSafeLanding()
    {
        HumanFallImpact impact = FromSpeed(HumanFallDamage.SafeSpeedMetresPerSecond);

        Assert.Equal(14f, impact.SpeedMetresPerSecond, 4);
        Assert.Equal(0f, impact.Damage);
        Assert.Equal(string.Empty, impact.SoundName);
        Assert.Equal(-HumanFallDamage.MaximumViewKickUnits, impact.ViewKickUnits);
        Assert.Equal(HumanFallDamage.SafeImpactHearingRadiusUnits, impact.HearingRadiusUnits);
    }

    [Fact]
    public void TwentySevenMetresPerSecondConsumesTheWholeRetailHealthBar()
    {
        HumanFallImpact impact = FromSpeed(
            HumanFallDamage.SafeSpeedMetresPerSecond
            + HumanFallDamage.LethalSpeedRangeMetresPerSecond);

        Assert.Equal(27f, impact.SpeedMetresPerSecond, 4);
        Assert.Equal(PlayerMaximumHealth, impact.Damage, 4);
        Assert.Equal("player_fall_3", impact.SoundName);
        Assert.Equal(HumanFallDamage.DamagingImpactHearingRadiusUnits, impact.HearingRadiusUnits);
    }

    [Theory]
    [InlineData(0.099f, "player_fall_1")]
    [InlineData(0.101f, "player_fall_2")]
    [InlineData(0.199f, "player_fall_2")]
    [InlineData(0.201f, "player_fall_3")]
    public void SoundTierFollowsTheRetailTenthsOfMaximumHealth(float damageFraction, string expected)
    {
        float speed = HumanFallDamage.SafeSpeedMetresPerSecond
            + (damageFraction * HumanFallDamage.LethalSpeedRangeMetresPerSecond);

        HumanFallImpact impact = FromSpeed(speed);

        Assert.Equal(expected, impact.SoundName);
        Assert.Equal(PlayerMaximumHealth * damageFraction, impact.Damage, 4);
    }

    [Fact]
    public void DamageAboveTheLethalSpeedIsNotCapped()
    {
        HumanFallImpact impact = FromSpeed(40f);

        Assert.True(impact.Damage > PlayerMaximumHealth);
    }

    [Fact]
    public void FallDamageBypassesThePlayersDifficultyScale()
    {
        HumanHealth health = new(HumanHitZoneTable.Player, PlayerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(difficulty: 0),
        };

        float dealt = health.ApplyDirect(HumanDamage.FrontZone, 1.5f);

        Assert.Equal(1.5f, dealt);
        Assert.Equal(1.5f, health.Damage);
        Assert.Equal(1, health.Hits);
    }

    private static HumanFallImpact FromSpeed(float metresPerSecond) =>
        HumanFallDamage.Calculate(
            -metresPerSecond * 4096f / 30f,
            PlayerMaximumHealth);
}
