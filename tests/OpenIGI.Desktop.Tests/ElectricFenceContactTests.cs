using OpenIGI.Game.Player;
using OpenIGI.Presentation.Igi1;
using Xunit;
using OpenIGI.Game.Igi1.Player;

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

        ElectricFenceShock shock = ElectricFenceContact.Shock(
            health,
            ElectricFenceContact.DefaultDamageFactor);

        Assert.Equal(HumanDamage.FrontZone, ElectricFenceContact.Zone);
        Assert.Equal(0.5f, ElectricFenceContact.DefaultDamageFactor);
        Assert.Equal(ElectricFenceContact.DefaultDamageFactor, shock.Damage, precision: 6);
        Assert.Equal(ElectricFenceContact.DefaultDamageFactor, health.Damage, precision: 6);
        Assert.Equal(1, health.Hits);
        Assert.False(shock.IsLethal);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1.5f)]
    public void ShockUsesTheAuthoredHumanPlayerArgumentEightDose(float dose)
    {
        // 0x412F80 multiplies flt_56E208 -- DefineHumanPlayerGeneral argument 8, written by 0x410137 --
        // by the human's damage scale. A level that retunes the player owns its own shock, so the dose
        // has to come from the parsed script rather than from the shipped 0.5.
        HumanHitZone[] zones = ZonesWithDistinctFrontMultiplier();
        HumanHealth health = new(zones, maximumHealth: 3f);

        ElectricFenceShock shock = ElectricFenceContact.Shock(health, dose);

        Assert.Equal(dose, shock.Damage, precision: 6);
        Assert.Equal(dose, health.Damage, precision: 6);
    }

    [Fact]
    public void TheDefaultDoseIsTheRetailArgumentEightValue()
    {
        float[] arguments = new float[HumanPlayerParameters.ArgumentCount];
        arguments[8] = ElectricFenceContact.DefaultDamageFactor;

        HumanPlayerParameters parameters = new(arguments);

        Assert.Equal(ElectricFenceContact.DefaultDamageFactor, parameters.ElectricFenceDose);
    }

    [Fact]
    public void ShockReportsLethalityAtTheHumanHealthThreshold()
    {
        HumanHitZone[] zones = ZonesWithDistinctFrontMultiplier();
        float damage = ElectricFenceContact.DefaultDamageFactor;
        HumanHealth health = new(zones, maximumHealth: damage);

        ElectricFenceShock shock = ElectricFenceContact.Shock(health, damage);

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
