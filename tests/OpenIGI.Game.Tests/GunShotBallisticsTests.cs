using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// How a round travels and what stops it.
/// </summary>
public sealed class GunShotBallisticsTests
{
    [Fact]
    public void TheEffectiveRangeIsInMetres()
    {
        // 0x47B190 stores EffectiveRange * 4096 as the round's starting range, which is what says the
        // parameter is metres rather than units.
        WeaponType glock = new() { EffectiveRange = 80f };

        Assert.Equal(80f * 4096f, GunShotBallistics.RangeInUnits(glock));
        Assert.Equal(327680f, GunShotBallistics.RangeInUnits(glock));
    }

    [Fact]
    public void AMarginOfOneOrMoreAlwaysGetsThrough()
    {
        // The sample is not even drawn in this branch, so any value must give the same answer.
        Assert.True(GunShotBallistics.Penetrates(5f, 4f, 0f));
        Assert.True(GunShotBallistics.Penetrates(5f, 4f, 0.999f));
        Assert.True(GunShotBallistics.Penetrates(100f, 1f, 0.5f));
    }

    [Fact]
    public void AnExactMatchGetsThroughSixTimesInTen()
    {
        Assert.True(GunShotBallistics.Penetrates(3f, 3f, 0f));
        Assert.True(GunShotBallistics.Penetrates(3f, 3f, 0.59f));
        Assert.False(GunShotBallistics.Penetrates(3f, 3f, GunShotBallistics.EqualPenetrationChance));
        Assert.False(GunShotBallistics.Penetrates(3f, 3f, 0.99f));
    }

    [Fact]
    public void TheRuleIsNotMonotonicAndAWiderMarginCanFailWhereAnEqualOneSucceeds()
    {
        // The original tests "== 0" and ">= 1" and stops on everything else, so a margin strictly
        // between the two is worse off than no margin at all. It looks like an oversight, but it is
        // what the game does - a round that out-penetrates its target by half never gets through, while
        // one that exactly matches gets through six times in ten.
        Assert.True(GunShotBallistics.Penetrates(3f, 3f, 0.5f));
        Assert.False(GunShotBallistics.Penetrates(3.5f, 3f, 0.5f));
        Assert.False(GunShotBallistics.Penetrates(3.99f, 3f, 0f));

        // And the moment the margin reaches one it succeeds again, unconditionally.
        Assert.True(GunShotBallistics.Penetrates(4f, 3f, 0.99f));
    }

    [Fact]
    public void AWeakerRoundNeverGetsThrough()
    {
        Assert.False(GunShotBallistics.Penetrates(1f, 3f, 0f));
        Assert.False(GunShotBallistics.Penetrates(1f, 3f, 0.99f));
    }

    [Fact]
    public void ASquareOnHitNeverDeflects()
    {
        // Incidence zero means the round arrived along the normal. The expression gives a large
        // negative number there, which no uniform sample can be below.
        float chance = GunShotBallistics.DeflectionChance(grazeLimitDegrees: 30f, incidenceRadians: 0f);

        // Impossible rather than merely unlikely: the caller compares a uniform sample on 0-1 against
        // this, and nothing in that range is below a negative number.
        Assert.True(chance < 0f);
    }

    [Fact]
    public void AGrazingHitDeflectsAlmostAlways()
    {
        // Incidence approaching a right angle means the round is nearly parallel to the surface.
        float chance = GunShotBallistics.DeflectionChance(grazeLimitDegrees: 30f, incidenceRadians: MathF.PI / 2f);

        Assert.Equal(1f, chance, 1e-6f);
    }

    [Fact]
    public void TheDeflectionChanceRisesWithTheGrazingAngle()
    {
        float[] chances =
        [
            GunShotBallistics.DeflectionChance(30f, 0.5f),
            GunShotBallistics.DeflectionChance(30f, 1.0f),
            GunShotBallistics.DeflectionChance(30f, 1.4f),
        ];

        Assert.True(chances[0] < chances[1]);
        Assert.True(chances[1] < chances[2]);
    }

    [Fact]
    public void ASurfaceWithNoLimitDeflectsNothing()
    {
        // The original guards the divide with an explicit zero test, so this is not an accident of the
        // arithmetic - a limit of zero means the surface never ricochets.
        Assert.Equal(0f, GunShotBallistics.DeflectionChance(0f, MathF.PI / 2f));
        Assert.Equal(0f, GunShotBallistics.DeflectionChance(0f, 0f));
    }

    [Fact]
    public void ARoundMayOnlyDeflectOnce()
    {
        Assert.True(GunShotBallistics.CanDeflect(hasAlreadyDeflected: false));
        Assert.False(GunShotBallistics.CanDeflect(hasAlreadyDeflected: true));
    }

    [Fact]
    public void TheStepOffIsFiveMillimetres()
    {
        // Without it the next cast would meet the same face and the loop would not terminate.
        Assert.Equal(20.48f, GunShotBallistics.PenetrationStepOffUnits);
        Assert.Equal(0.005f, GunShotBallistics.PenetrationStepOffUnits / GunShotBallistics.UnitsPerMetre, 1e-7f);
    }

    [Fact]
    public void RangeIsConsumedByDistanceTravelledUntilItRunsOut()
    {
        // The loop condition is "> 0", so a round that exactly exhausts its range stops.
        float range = GunShotBallistics.RangeInUnits(new WeaponType { EffectiveRange = 1f });
        Assert.Equal(4096f, range);

        range = GunShotBallistics.ConsumeRange(range, 1000f);
        Assert.Equal(3096f, range);

        range = GunShotBallistics.ConsumeRange(range, 3096f);
        Assert.Equal(0f, range);
        Assert.False(range > 0f);
    }

    [Fact]
    public void TheShotPoolIsFifteenAndOverflowIsSilent()
    {
        // Worth stating as a limit rather than discovering it: a sixteenth live round is counted and
        // consumes its ammunition, but never exists. The shotgun is the realistic way to reach this,
        // since it fires several projectiles per round.
        Assert.Equal(15, GunShotBallistics.MaximumLiveShots);

        WeaponType spas = new() { BulletsPerRound = 6 };
        Assert.True(
            spas.BulletsPerRound * 3 > GunShotBallistics.MaximumLiveShots,
            "three shotgun rounds in flight already exceed the pool");
    }
}
