using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The gun's clock and its firing gate.
/// </summary>
public sealed class GunFiringTests
{
    [Theory]
    // The Glock: 400 rounds a minute is exactly 4.5 ticks, and the gun uses 4.
    [InlineData(400, 4)]
    // The AK-47: 600 is exactly 3.
    [InlineData(600, 3)]
    // The Minimi: 700 is 2.571, so 2.
    [InlineData(700, 2)]
    // The twin Uzis: 1400 is 1.285, so 1.
    [InlineData(1400, 1)]
    // The Jackhammer: 180 is 10 exactly.
    [InlineData(180, 10)]
    public void TheRateTruncatesRatherThanRounds(int roundsPerMinute, int expected)
    {
        Assert.Equal(expected, GunTiming.TicksPerRound(roundsPerMinute));
    }

    [Fact]
    public void TruncatingMakesTheGunFasterThanItsStatedRate()
    {
        // 4.5 ticks would be 400 rounds a minute. 4 ticks is 450. The gun runs FASTER than its
        // parameter says, never slower - which is the direction truncation always takes it.
        double exact = GunTiming.SecondsPerMinute / 400 * GunTiming.TicksPerSecond;
        Assert.Equal(4.5, exact);

        int actual = GunTiming.TicksPerRound(400);
        Assert.True(actual < exact);
        Assert.Equal(450.0, GunTiming.SecondsPerMinute * GunTiming.TicksPerSecond / actual);
    }

    [Fact]
    public void TheRecoilPhaseUsesTheUntruncatedRate()
    {
        // The one place the value is NOT truncated. The phase decays over the exact 4.5 ticks while the
        // cooldown that gates the next round uses 4, so the two are deliberately out of step.
        float step = GunTiming.RecoilPhaseStep(400);
        Assert.Equal(GunTiming.RecoilPhaseOnFire / 4.5f, step, 1e-4f);
        Assert.NotEqual(GunTiming.RecoilPhaseOnFire / GunTiming.TicksPerRound(400), step);
    }

    [Fact]
    public void TheRecoilPhaseSettlesExactlyAtZeroFromEitherSide()
    {
        float phase = GunTiming.RecoilPhaseOnFire;
        for (int i = 0; i < 100; i++)
        {
            phase = GunTiming.AdvanceRecoilPhase(phase, 400);
        }

        Assert.Equal(0f, phase);

        // And from below, which the original handles as a separate branch.
        phase = -GunTiming.RecoilPhaseOnFire;
        for (int i = 0; i < 100; i++)
        {
            phase = GunTiming.AdvanceRecoilPhase(phase, 400);
        }

        Assert.Equal(0f, phase);
    }

    [Fact]
    public void TheTriggerRampTakesAnAndAHalfSecondsAndResetsInstantly()
    {
        float ramp = 0f;
        for (int i = 0; i < 45; i++)
        {
            ramp = GunTiming.AdvanceTriggerRamp(ramp, triggerHeld: true);
        }

        Assert.Equal(1f, ramp);

        // Held longer it saturates rather than overshooting.
        Assert.Equal(1f, GunTiming.AdvanceTriggerRamp(ramp, triggerHeld: true));

        // Released it snaps, rather than decaying.
        Assert.Equal(0f, GunTiming.AdvanceTriggerRamp(ramp, triggerHeld: false));
    }

    [Fact]
    public void ASemiAutomaticFiresOncePerPull()
    {
        WeaponType pistol = Weapon(roundsPerMinute: 400, clip: 17, burst: 1);
        GunState gun = new();
        gun.Tick(pistol, triggerHeld: false);

        Assert.Equal(GunFireBlock.None, GunFiring.Evaluate(pistol, gun, true, true));
        gun.OnRoundFired(pistol, consumesAmmunition: true);

        // Holding the trigger down gets nothing more, however long it is held.
        for (int i = 0; i < 60; i++)
        {
            gun.Tick(pistol, triggerHeld: true);
            Assert.Equal(GunFireBlock.BurstComplete, GunFiring.Evaluate(pistol, gun, true, true));
        }

        // Releasing and pulling again does.
        gun.Tick(pistol, triggerHeld: false);
        Assert.Equal(GunFireBlock.None, GunFiring.Evaluate(pistol, gun, true, true));
    }

    [Fact]
    public void AnAutomaticFiresAtItsRateUntilTheMagazineIsEmpty()
    {
        // Burst equal to the magazine is how the data spells "fully automatic", so the same comparison
        // that stops a pistol after one round never stops this until the clip does.
        WeaponType rifle = Weapon(roundsPerMinute: 600, clip: 30, burst: 30);
        GunState gun = new();
        gun.Tick(rifle, triggerHeld: false);
        Assert.Equal(30, gun.RoundsInClip);

        int fired = 0;
        int ticks = 0;
        while (ticks < 1000 && fired < 30)
        {
            if (GunFiring.Evaluate(rifle, gun, true, true) == GunFireBlock.None)
            {
                gun.OnRoundFired(rifle, consumesAmmunition: true);
                fired++;
            }

            gun.Tick(rifle, triggerHeld: true);
            ticks++;
        }

        Assert.Equal(30, fired);
        Assert.Equal(0, gun.RoundsInClip);

        // Because "fully automatic" is spelled as a burst equal to the magazine, the two limits run out
        // on the SAME round - and the burst is tested first, so that is what reports.
        Assert.Equal(GunFireBlock.BurstComplete, GunFiring.Evaluate(rifle, gun, true, true));

        // Release, and let the last round's cooldown run out - that is tested before the magazine, so
        // until it clears the gun reports Cooling rather than Empty.
        gun.Tick(rifle, triggerHeld: false);
        Assert.Equal(GunFireBlock.Cooling, GunFiring.Evaluate(rifle, gun, true, true));

        while (gun.CooldownTicks != 0)
        {
            gun.Tick(rifle, triggerHeld: false);
        }

        // Now the burst is reset and the cooldown is clear, so the empty magazine is what stops it.
        Assert.Equal(GunFireBlock.Empty, GunFiring.Evaluate(rifle, gun, true, true));

        // Three ticks a round at 600. The first round goes off immediately, so the thirtieth lands 29
        // gaps of three ticks later - the magazine empties in 87 ticks, a shade under three seconds.
        Assert.Equal(3, GunTiming.TicksPerRound(600));
        Assert.Equal(29 * 3, ticks - 1);
    }

    [Fact]
    public void AGunWithNoAmmunitionRecordNeverRunsOut()
    {
        // The emplacement guns. The original only tests the magazine when the ammunition record is
        // present, so an unfed gun fires for ever - and its clip count goes nowhere.
        WeaponType emplacement = Weapon(roundsPerMinute: 600, clip: 1, burst: 1);
        GunState gun = new();
        gun.Tick(emplacement, triggerHeld: false);

        gun.OnRoundFired(emplacement, consumesAmmunition: false);

        // The magazine is untouched, because the decrement is behind the same test as the gate.
        Assert.Equal(1, gun.RoundsInClip);

        // With the burst and the cooldown both clear, an empty magazine stops a fed gun and not an
        // unfed one - which is the only thing the ammunition record changes here.
        gun.RoundsInClip = 0;
        gun.RoundsThisBurst = 0;
        gun.CooldownTicks = 0;
        Assert.Equal(GunFireBlock.None, GunFiring.Evaluate(emplacement, gun, true, false));
        Assert.Equal(GunFireBlock.Empty, GunFiring.Evaluate(emplacement, gun, true, true));
    }

    [Fact]
    public void TheMagazineLoadsItselfOnceFromTheSentinel()
    {
        WeaponType rifle = Weapon(roundsPerMinute: 600, clip: 30, burst: 30);
        GunState gun = new();

        Assert.Equal(GunTiming.UnloadedClipSentinel, gun.RoundsInClip);
        gun.Tick(rifle, triggerHeld: false);
        Assert.Equal(30, gun.RoundsInClip);

        // And never again: once loaded the count is whatever firing leaves it.
        gun.RoundsInClip = 7;
        gun.Tick(rifle, triggerHeld: false);
        Assert.Equal(7, gun.RoundsInClip);
    }

    [Fact]
    public void SpreadStepsAtTheFourthRoundRatherThanRamping()
    {
        // The whole accuracy model in one comparison: rounds 0 to 3 of a burst use the minimum and
        // everything after uses the maximum. Nothing interpolates.
        WeaponType rifle = Weapon(roundsPerMinute: 600, clip: 30, burst: 30);

        for (int round = 0; round <= 3; round++)
        {
            Assert.Equal(
                MathF.Abs(rifle.MinimumSpread * GunFiring.DegreesToRadians),
                GunFiring.ConeAngle(rifle, round));
        }

        for (int round = 4; round < 10; round++)
        {
            Assert.Equal(
                MathF.Abs(rifle.MaximumSpread * GunFiring.DegreesToRadians),
                GunFiring.ConeAngle(rifle, round));
        }
    }

    [Fact]
    public void TheConeAngleIsAbsoluteBecauseTheShippedMinimumIsNegative()
    {
        // The retail data stores the spread pair as a signed range about zero - the AK-47's is
        // -3.5 to 3.5 - so without the absolute value every burst would open with a negative cone.
        WeaponType ak = Weapon(roundsPerMinute: 600, clip: 30, burst: 30, minSpread: -3.5f, maxSpread: 3.5f);

        Assert.True(GunFiring.ConeAngle(ak, 0) > 0f);
        Assert.Equal(GunFiring.ConeAngle(ak, 0), GunFiring.ConeAngle(ak, 9));
    }

    [Fact]
    public void TheConeRadiusIsAProductOfThreeSamplesSoShotsCluster()
    {
        // Three uniform samples multiplied together, not one - which biases the group heavily toward
        // the centre. A uniform draw would put half the shots beyond 0.5 of the cone; this puts about
        // an eighth there.
        const float cone = 0.1f;

        (float radius, float bearing) = GunFiring.SampleCone(cone, [1f, 1f, 1f, 1f]);
        Assert.Equal(cone, radius);
        Assert.Equal(GunFiring.TwoPi, bearing);

        (radius, bearing) = GunFiring.SampleCone(cone, [0.5f, 0.5f, 0.5f, 0f]);
        Assert.Equal(cone * 0.125f, radius);
        Assert.Equal(0f, bearing);

        // A single zero collapses the whole radius, which a sum would not do.
        (radius, _) = GunFiring.SampleCone(cone, [1f, 0f, 1f, 0.5f]);
        Assert.Equal(0f, radius);
    }

    [Fact]
    public void TheCasingSpeedIsFourWholeValuesAndTruncates()
    {
        // Integer arithmetic: multiply first, then divide and lose the remainder. 4 km/h is exactly
        // 151.7 units a tick and the casing gets 151.
        Assert.Equal(151, GunCasing.LaunchSpeed(0));
        Assert.Equal(189, GunCasing.LaunchSpeed(1));
        Assert.Equal(227, GunCasing.LaunchSpeed(2));
        Assert.Equal(265, GunCasing.LaunchSpeed(3));

        // Nothing between them - the draw is over whole kilometres per hour.
        Assert.Equal(4, GunCasing.SpeedRange);

        // And the same quantity in the player's floating-point conversion does not truncate, so the two
        // paths really do disagree.
        float asFloat = 4 * (GunCasing.SpeedNumerator / (float)GunCasing.SpeedDenominator);
        Assert.True(asFloat > GunCasing.LaunchSpeed(0));
        Assert.True(asFloat - GunCasing.LaunchSpeed(0) < 1f);
    }

    [Fact]
    public void TheSubCounterPeriodsTruncateTwice()
    {
        // An integer divide of an already-truncated value. At 400 rounds a minute the rate is 4 ticks,
        // so the periods are 1 and 0 - and a period of zero means the counter steps every tick.
        Assert.Equal(4, GunTiming.TicksPerRound(400));
        Assert.Equal(1, GunTiming.FirstSubCounterPeriod(400));
        Assert.Equal(0, GunTiming.SecondSubCounterPeriod(400));

        // The Jackhammer is slow enough for both to be non-zero.
        Assert.Equal(10, GunTiming.TicksPerRound(180));
        Assert.Equal(3, GunTiming.FirstSubCounterPeriod(180));
        Assert.Equal(1, GunTiming.SecondSubCounterPeriod(180));
    }

    [Fact]
    public void TheSubCountersOnlyRunOnceARoundHasGoneOff()
    {
        // Both start at zero and the per-tick tests for non-zero, so an unfired gun never steps them.
        WeaponType rifle = Weapon(roundsPerMinute: 180, clip: 10, burst: 10);
        GunState gun = new();

        for (int i = 0; i < 20; i++)
        {
            gun.Tick(rifle, triggerHeld: false);
        }

        Assert.Equal(0, gun.FirstSubCounter);
        Assert.Equal(0, gun.SecondSubCounter);

        gun.OnRoundFired(rifle, consumesAmmunition: true);
        Assert.Equal(GunTiming.FirstSubCounterStart, gun.FirstSubCounter);
        Assert.Equal(GunTiming.SecondSubCounterStart, gun.SecondSubCounter);

        gun.Tick(rifle, triggerHeld: true);
        gun.Tick(rifle, triggerHeld: true);
        gun.Tick(rifle, triggerHeld: true);
        gun.Tick(rifle, triggerHeld: true);
        Assert.True(gun.FirstSubCounter > GunTiming.FirstSubCounterStart);
    }

    private static WeaponType Weapon(
        int roundsPerMinute,
        int clip,
        int burst,
        float minSpread = 1f,
        float maxSpread = 5f) => new()
        {
            RoundsPerMinute = roundsPerMinute,
            RoundsPerClip = clip,
            MaximumRoundsPerBurst = burst,
            MinimumSpread = minSpread,
            MaximumSpread = maxSpread,
            BulletsPerRound = 1,
        };
}
