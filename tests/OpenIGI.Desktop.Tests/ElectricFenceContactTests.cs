using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Regression coverage for the port's current, not-yet-retail-validated fence-contact rule.</summary>
public sealed class ElectricFenceContactTests
{
    [Theory]
    [InlineData(0, 0.75f)]
    [InlineData(1, 1.00f)]
    [InlineData(2, 1.25f)]
    public void ShockUsesThePlayerHealthPathForItsCurrentZoneAndDifficulty(
        int difficulty,
        float expectedDifficultyScale)
    {
        HumanHitZone[] zones = ZonesWithDistinctFrontMultiplier();
        HumanHealth health = new(zones, maximumHealth: 3f)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(difficulty),
        };

        ElectricFenceShock shock = ElectricFenceContact.Shock(health);
        float expectedDamage = zones[HumanDamage.FrontZone].Multiplier
            * ElectricFenceContact.DamageFactor
            * expectedDifficultyScale;

        Assert.Equal(HumanDamage.FrontZone, ElectricFenceContact.Zone);
        Assert.Equal(expectedDamage, shock.Damage, precision: 6);
        Assert.Equal(expectedDamage, health.Damage, precision: 6);
        Assert.Equal(1, health.Hits);
        Assert.False(shock.IsLethal);
    }

    [Fact]
    public void ShockReportsLethalityAtTheHumanHealthThreshold()
    {
        HumanHitZone[] zones = ZonesWithDistinctFrontMultiplier();
        float damage = zones[HumanDamage.FrontZone].Multiplier * ElectricFenceContact.DamageFactor;
        HumanHealth health = new(zones, maximumHealth: damage);

        ElectricFenceShock shock = ElectricFenceContact.Shock(health);

        Assert.Equal(damage, shock.Damage, precision: 6);
        Assert.True(health.IsDead);
        Assert.True(shock.IsLethal);
    }

    [Fact]
    public void ContactTransitionReportsOnlyEntryAndAllowsReentry()
    {
        object firstFence = new();
        object secondFence = new();
        object? active = null;

        Assert.Same(firstFence, ElectricFenceContact.Advance(ref active, firstFence));
        Assert.Null(ElectricFenceContact.Advance(ref active, firstFence));
        Assert.Same(secondFence, ElectricFenceContact.Advance(ref active, secondFence));
        Assert.Null(ElectricFenceContact.Advance<object>(ref active, null));
        Assert.Same(firstFence, ElectricFenceContact.Advance(ref active, firstFence));
    }

    [Fact]
    public void DamagingShockNotifiesAiWithTheFenceAnchorAndGroundImpactRadius()
    {
        Vector3d anchor = new(100, 200, 300);
        Vector3d player = new(-10, -20, -30);
        (Vector3d Impact, Vector3d Player, double Radius)? notification = null;

        ElectricFenceContact.NotifyAiOnShock(
            new ElectricFenceShock(0.1f, IsLethal: false),
            (impact, source, radius) => notification = (impact, source, radius),
            anchor,
            player);

        Assert.Equal((anchor, player, AiSoldier.GroundImpactRadiusInUnits), notification);

        notification = null;
        ElectricFenceContact.NotifyAiOnShock(
            new ElectricFenceShock(0f, IsLethal: false),
            (impact, source, radius) => notification = (impact, source, radius),
            anchor,
            player);
        Assert.Null(notification);
    }

    private static HumanHitZone[] ZonesWithDistinctFrontMultiplier()
    {
        HumanHitZone[] zones = new HumanHitZone[HumanDamage.ZoneCount];
        zones[HumanDamage.DefaultZone] = new HumanHitZone(7f, 0, 0, 0);
        zones[HumanDamage.FrontZone] = new HumanHitZone(2f, 0, 0, 0);
        return zones;
    }
}
