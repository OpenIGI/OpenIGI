using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// How an explosion's damage falls off with distance.
/// </summary>
public sealed class ExplosionDamageTests
{
    [Fact]
    public void TheShippedGrenadeUsesTheAuthoredFourMetreFalloff()
    {
        // These values come from the Hand Grenade child tags in weaponconfig.qvm.
        float radius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);
        float falloff = ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeFalloffMetres);
        const float body = 0f;
        const float factor = ExplosionDamage.DefaultGrenadeDamageFactor;

        Assert.Equal(4f * ExplosionDamage.UnitsPerMetre, falloff);

        Assert.Equal(factor, ExplosionDamage.Dose(0f, body, radius, falloff, factor));
        Assert.Equal(factor, ExplosionDamage.Dose(radius - 1f, body, radius, falloff, factor));

        Assert.Equal(factor, ExplosionDamage.Dose(radius, body, radius, falloff, factor));
        Assert.Equal(factor * 0.5f, ExplosionDamage.Dose(radius + (falloff * 0.5f), body, radius, falloff, factor));
        Assert.Equal(0f, ExplosionDamage.Dose(radius + falloff, body, radius, falloff, factor));
    }

    [Fact]
    public void AZeroFalloffCannotDivideByZero()
    {
        // The order of the two tests is what saves it: everything at or past the falloff returns before
        // the divide, so no input reaches the division with a zero denominator.
        float dose = ExplosionDamage.Dose(
            distanceInUnits: 1e9f, bodyRadiusInUnits: 0f, radiusInUnits: 1f, falloffInUnits: 0f,
            damageFactor: 1f);

        Assert.Equal(0f, dose);
        Assert.False(float.IsNaN(dose));
        Assert.False(float.IsInfinity(dose));
    }

    [Fact]
    public void WithAFalloffTheDamageIsLinearBetweenTheRadiusAndTheEdge()
    {
        const float radius = 1000f;
        const float falloff = 500f;
        const float factor = 1f;

        // Inside the radius: undiminished.
        Assert.Equal(1f, ExplosionDamage.Dose(999f, 0f, radius, falloff, factor));

        // Exactly at the radius: d is 0, which is not "< 0", so it takes the linear branch - where a
        // distance of 0 into the falloff still yields the full value. The curve is continuous across
        // the boundary despite the two branches.
        Assert.Equal(1f, ExplosionDamage.Dose(radius, 0f, radius, falloff, factor));

        // Halfway across the falloff: half.
        Assert.Equal(0.5f, ExplosionDamage.Dose(radius + 250f, 0f, radius, falloff, factor));

        // At the edge and beyond: nothing.
        Assert.Equal(0f, ExplosionDamage.Dose(radius + falloff, 0f, radius, falloff, factor));
        Assert.Equal(0f, ExplosionDamage.Dose(radius + falloff + 1f, 0f, radius, falloff, factor));
    }

    [Fact]
    public void TheDamageFactorScalesTheWholeCurve()
    {
        const float radius = 1000f;
        const float falloff = 500f;

        Assert.Equal(2f, ExplosionDamage.Dose(0f, 0f, radius, falloff, damageFactor: 2f));
        Assert.Equal(1f, ExplosionDamage.Dose(radius + 250f, 0f, radius, falloff, damageFactor: 2f));
    }

    [Fact]
    public void TheTargetsOwnRadiusCountsAsAThird()
    {
        // A body counts as rather narrower than it collides, which pulls the effective distance in.
        Assert.Equal(100f, ExplosionDamage.EffectiveBodyRadius(300f), 1e-4f);

        const float radius = 1000f;
        const float falloff = 500f;

        // Standing 1200 units away with no body radius is a quarter of the way across the falloff.
        Assert.Equal(0.6f, ExplosionDamage.Dose(1200f, 0f, radius, falloff, 1f), 1e-6f);

        // The same position with a body radius closes 100 of that gap.
        Assert.Equal(0.8f, ExplosionDamage.Dose(1200f, 100f, radius, falloff, 1f), 1e-6f);
    }

    [Fact]
    public void AnExplosionAlwaysAppliesToTheFrontZone()
    {
        // The human's explosion handler hardcodes zone 13, so a blast never lands a headshot and never
        // hits a limb - every target takes the same multiplier however it happens to be standing.
        Assert.Equal(HumanDamage.FrontZone, ExplosionDamage.Zone);
        Assert.NotEqual(HumanDamage.ProximityZone, ExplosionDamage.Zone);
    }

    [Fact]
    public void ZoneThirteensSlotsAreWhereTheHumansArrayLayoutSaysTheyAre()
    {
        // An independent confirmation of the {accumulated, multiplier} pair layout: the explosion
        // handler reaches zone 13 through hardcoded offsets +712 and +716, and the array starts at
        // +608 with a stride of 8.
        const int arrayBase = 608;
        const int stride = 8;

        Assert.Equal(712, arrayBase + (stride * ExplosionDamage.Zone));
        Assert.Equal(716, arrayBase + (stride * ExplosionDamage.Zone) + 4);

        // And zone 13 is inside the seventeen the human allocates.
        Assert.InRange(ExplosionDamage.Zone, 0, HumanDamage.ZoneCount - 1);
    }

    [Fact]
    public void TheGrenadeExplosionTypesAreZeroAndOne()
    {
        Assert.Equal(0, (int)GrenadeExplosionType.Normal);
        Assert.Equal(1, (int)GrenadeExplosionType.Flashbang);
    }

    [Fact]
    public void TheHardcodedFallbackMatchesTheTagDefaultsExactly()
    {
        // When a grenade has no parameter block the update passes literals instead. They are the same
        // numbers as the tag defaults, which is worth pinning: a mismatch would mean the two paths
        // disagreed about how big a grenade is.
        Assert.Equal(28672f, ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres));
        Assert.Equal(1.3f, ExplosionDamage.DefaultGrenadeDamageFactor);
        Assert.Equal(4f, ExplosionDamage.DefaultGrenadeFalloffMetres);
    }
}
