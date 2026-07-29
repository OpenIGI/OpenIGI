using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// How a hit becomes damage, and how damage becomes death.
/// </summary>
public sealed class HumanDamageTests
{
    [Fact]
    public void DamageCountsUpwardTowardAMaximumRatherThanDownFromAPool()
    {
        // The single most misleading thing about this model: +596 is accumulated damage and +600 is the
        // maximum. Nothing subtracts anywhere, and the HUD turns it back into a bar.
        Assert.False(HumanDamage.IsDead(accumulatedDamage: 0f, maximumHealth: 100f));
        Assert.False(HumanDamage.IsDead(99.9f, 100f));

        // Exactly reaching the maximum kills: the comparison is >=.
        Assert.True(HumanDamage.IsDead(100f, 100f));
        Assert.True(HumanDamage.IsDead(250f, 100f));
    }

    [Fact]
    public void TheHudFractionIsOneMinusTheRatioAndOnlyClampsBelow()
    {
        Assert.Equal(1f, HumanDamage.RemainingFraction(0f, 100f));
        Assert.Equal(0.25f, HumanDamage.RemainingFraction(75f, 100f));
        Assert.Equal(0f, HumanDamage.RemainingFraction(100f, 100f));

        // Over-killed reads as zero, not negative - and there is no clamp on the high side because
        // accumulated damage never goes below zero to begin with.
        Assert.Equal(0f, HumanDamage.RemainingFraction(500f, 100f));
    }

    [Fact]
    public void DamageIsAPlainProductOfZoneMultiplierAndWeaponFactor()
    {
        // Nothing scales it by range, by remaining energy, or by how many surfaces the round has
        // already been through. A round that has punched through two walls hits exactly as hard.
        Assert.Equal(0.5f, HumanDamage.DamageOfHit(zoneMultiplier: 2f, damageFactor: 0.25f));
        Assert.Equal(0f, HumanDamage.DamageOfHit(0f, 0.25f));
    }

    [Fact]
    public void TheGlockNeedsFourTimesAsManyBodyHitsAsTheDragunov()
    {
        // A worked example against the real Damage Factors: 0.25 for the Glock, 0.5 for the Dragunov,
        // both into a zone with a multiplier of one.
        const float maximum = 1f;

        int glock = HitsToKill(damageFactor: 0.25f, zoneMultiplier: 1f, maximum);
        int dragunov = HitsToKill(damageFactor: 0.5f, zoneMultiplier: 1f, maximum);

        Assert.Equal(4, glock);
        Assert.Equal(2, dragunov);
    }

    [Fact]
    public void AZoneMultiplierScalesTheHitCountDirectly()
    {
        // Which is what makes a hit zone worth having: the same round into a zone worth four times as
        // much kills in a quarter of the hits.
        Assert.Equal(8, HitsToKill(damageFactor: 0.25f, zoneMultiplier: 0.5f, maximumHealth: 1f));
        Assert.Equal(1, HitsToKill(damageFactor: 0.25f, zoneMultiplier: 4f, maximumHealth: 1f));
    }

    [Fact]
    public void TheSubObjectTableCoversThirtyEntriesAndSeventeenZones()
    {
        HashSet<int> zones = [];
        for (int i = 0; i < HumanDamage.MappedSubObjectCount; i++)
        {
            zones.Add(HumanDamage.ZoneOfSubObject(i));
        }

        // Fourteen zones are reachable from the table; 13, 14 and 15 only ever come from refining 12.
        Assert.Equal(14, zones.Count);
        Assert.DoesNotContain(HumanDamage.FrontZone, zones);
        Assert.DoesNotContain(HumanDamage.RearZone, zones);
        Assert.DoesNotContain(HumanDamage.ProximityZone, zones);

        // And every zone the table names is inside the array the human actually allocates.
        Assert.All(zones, z => Assert.InRange(z, 0, HumanDamage.ZoneCount - 1));
    }

    [Fact]
    public void TheTableIsFourLimbChainsPlusATorsoAndOneMoreRegion()
    {
        // The structure is what makes the zone numbering readable: four chains of three, each running
        // from the body outward.
        Assert.Equal([12, 12, 12, 12], Zones(0, 4));
        Assert.Equal([16, 16, 16, 16], Zones(4, 4));
        Assert.Equal([11, 10, 9, 9, 9, 9], Zones(8, 6));
        Assert.Equal([5, 4, 3, 3, 3, 3], Zones(14, 6));
        Assert.Equal([8, 7, 6, 6, 6], Zones(20, 5));
        Assert.Equal([2, 1, 0, 0, 0], Zones(25, 5));
    }

    [Fact]
    public void AnythingPastTheTableTakesTheDefaultZone()
    {
        Assert.Equal(HumanDamage.DefaultZone, HumanDamage.ZoneOfSubObject(30));
        Assert.Equal(HumanDamage.DefaultZone, HumanDamage.ZoneOfSubObject(1000));

        // The original's bound is a plain "< 30" with no lower check, so a negative index would read
        // behind the table. This returns the default rather than reproducing that.
        Assert.Equal(HumanDamage.DefaultZone, HumanDamage.ZoneOfSubObject(-1));
    }

    [Fact]
    public void OnlyADefaultZoneHitIsRefinedByDirection()
    {
        // A round that named a limb keeps its zone whatever angle it came in at.
        Assert.Equal(9, HumanDamage.RefineByDirection(9, localForward: 1f, localSide: 0f));
        Assert.Equal(0, HumanDamage.RefineByDirection(0, localForward: -1f, localSide: 0f));
    }

    [Fact]
    public void ADirectionalHitTakesFrontOrRearAndAGlancingOneDoesNot()
    {
        // Front: the forward component dominates and is positive.
        Assert.Equal(
            HumanDamage.FrontZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: 1f, localSide: 0.2f));

        // Rear: it dominates and is negative.
        Assert.Equal(
            HumanDamage.RearZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: -1f, localSide: 0.2f));

        // Glancing: the sideways component dominates, so neither applies and the default stands.
        Assert.Equal(
            HumanDamage.DefaultZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: 0.2f, localSide: 1f));
        Assert.Equal(
            HumanDamage.DefaultZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: -0.2f, localSide: 1f));
    }

    [Fact]
    public void TheDirectionTestsAreStrictSoTiesKeepTheDefaultZone()
    {
        // Both comparisons are ">", not ">=". A hit arriving at exactly 45 degrees has equal forward
        // and sideways magnitudes and takes neither directional zone.
        Assert.Equal(
            HumanDamage.DefaultZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: 1f, localSide: 1f));
        Assert.Equal(
            HumanDamage.DefaultZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: -1f, localSide: 1f));

        // A forward component of exactly zero enters both of the original's branches - the first tests
        // "<= 0" and the second ">= 0" - and satisfies neither, so it is never refined.
        Assert.Equal(
            HumanDamage.DefaultZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: 0f, localSide: -0.5f));
        Assert.Equal(
            HumanDamage.DefaultZone,
            HumanDamage.RefineByDirection(HumanDamage.DefaultZone, localForward: 0f, localSide: 0f));
    }

    [Fact]
    public void TheProximityZoneIsAFifteenCentimetreSphereAndOnlyFromTheFront()
    {
        // 614.4 units is 0.15 m - head-sized - and it is decided by distance to one bone rather than by
        // any geometry.
        Assert.Equal(0.15f, HumanDamage.HeadProximityUnits / 4096f, 1e-6f);

        Assert.True(HumanDamage.TakesProximityZone(distanceFromBoneUnits: 100f, localForward: 1f));
        Assert.True(HumanDamage.TakesProximityZone(614.3f, 0f));
        Assert.False(HumanDamage.TakesProximityZone(HumanDamage.HeadProximityUnits, 1f));

        // A round from behind can never take it, however close it lands.
        Assert.False(HumanDamage.TakesProximityZone(0f, localForward: -0.001f));
    }

    private static int[] Zones(int start, int count)
    {
        int[] result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = HumanDamage.ZoneOfSubObject(start + i);
        }

        return result;
    }

    private static int HitsToKill(float damageFactor, float zoneMultiplier, float maximumHealth)
    {
        float accumulated = 0f;
        int hits = 0;

        while (!HumanDamage.IsDead(accumulated, maximumHealth))
        {
            accumulated += HumanDamage.DamageOfHit(zoneMultiplier, damageFactor);
            hits++;
        }

        return hits;
    }
}
