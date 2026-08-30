using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Regression coverage for the retail-validated player electric-fence rule.</summary>
public sealed class ElectricFenceContactTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ShockUsesTheNativeDirectZone13DoseWithoutDifficultyScaling(int difficulty)
    {
        HumanHitZone[] zones = ZonesWithDistinctFrontMultiplier();
        HumanHealth health = new(zones, maximumHealth: 3f)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(difficulty),
        };

        ElectricFenceShock shock = ElectricFenceContact.Shock(health);

        Assert.Equal(HumanDamage.FrontZone, ElectricFenceContact.Zone);
        Assert.Equal(0.5f, ElectricFenceContact.DamageFactor);
        Assert.Equal(ElectricFenceContact.DamageFactor, shock.Damage, precision: 6);
        Assert.Equal(ElectricFenceContact.DamageFactor, health.Damage, precision: 6);
        Assert.Equal(1, health.Hits);
        Assert.False(shock.IsLethal);
    }

    [Fact]
    public void ShockReportsLethalityAtTheHumanHealthThreshold()
    {
        HumanHitZone[] zones = ZonesWithDistinctFrontMultiplier();
        float damage = ElectricFenceContact.DamageFactor;
        HumanHealth health = new(zones, maximumHealth: damage);

        ElectricFenceShock shock = ElectricFenceContact.Shock(health);

        Assert.Equal(damage, shock.Damage, precision: 6);
        Assert.True(health.IsDead);
        Assert.True(shock.IsLethal);
    }

    [Fact]
    public void SustainedContactShocksAgainEveryFortyFiveTicks()
    {
        object fence = new();
        int cooldown = 0;

        Assert.Same(fence, ElectricFenceContact.Advance(ref cooldown, fence));
        Assert.Equal(ElectricFenceContact.CooldownTicks, cooldown);

        for (int tick = 1; tick < ElectricFenceContact.CooldownTicks; tick++)
        {
            Assert.Null(ElectricFenceContact.Advance(ref cooldown, fence));
            Assert.Equal(ElectricFenceContact.CooldownTicks - tick, cooldown);
        }

        Assert.Same(fence, ElectricFenceContact.Advance(ref cooldown, fence));
        Assert.Equal(ElectricFenceContact.CooldownTicks, cooldown);
    }

    [Fact]
    public void CooldownPersistsAfterLeavingTheFence()
    {
        object firstFence = new();
        object secondFence = new();
        int cooldown = 0;

        Assert.Same(firstFence, ElectricFenceContact.Advance(ref cooldown, firstFence));
        Assert.Null(ElectricFenceContact.Advance<object>(ref cooldown, null));
        Assert.Equal(ElectricFenceContact.CooldownTicks - 1, cooldown);
        Assert.Null(ElectricFenceContact.TryShock(ref cooldown, secondFence));
        Assert.Equal(ElectricFenceContact.CooldownTicks - 1, cooldown);
    }

    private static HumanHitZone[] ZonesWithDistinctFrontMultiplier()
    {
        HumanHitZone[] zones = new HumanHitZone[HumanDamage.ZoneCount];
        zones[HumanDamage.DefaultZone] = new HumanHitZone(7f, 0, 0, 0);
        zones[HumanDamage.FrontZone] = new HumanHitZone(2f, 0, 0, 0);
        return zones;
    }
}
