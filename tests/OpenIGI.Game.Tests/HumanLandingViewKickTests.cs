using OpenIGI.Game.Player;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the landing dip against the shape <c>0x464BD0</c> plays: one half period of a sine, fifteen
/// ticks long, sampled before the phase steps.
/// </summary>
public sealed class HumanLandingViewKickTests
{
    [Fact]
    public void AnUninstalledKickHoldsTheViewLevel()
    {
        HumanLandingViewKick kick = new();

        kick.Advance();

        Assert.False(kick.IsActive);
        Assert.Equal(0f, kick.OffsetUnits);
        Assert.Equal(0f, kick.ViewModelOffsetUnits);
    }

    [Fact]
    public void TheDipOpensAtZeroBecauseTheSineIsSampledBeforeTheStep()
    {
        HumanLandingViewKick kick = new();
        kick.Install(-HumanFallDamage.MaximumViewKickUnits);

        kick.Advance();

        // sin(0). The arming tick shows no jump; the view starts falling on the tick after.
        Assert.Equal(0f, kick.OffsetUnits);
        Assert.True(kick.IsActive);
    }

    [Fact]
    public void TheViewOnlyEverDropsAndReturnsWithoutOvershooting()
    {
        HumanLandingViewKick kick = new();
        kick.Install(-HumanFallDamage.MaximumViewKickUnits);

        float deepest = 0f;
        int deepestTick = -1;

        for (int tick = 0; kick.IsActive; tick++)
        {
            kick.Advance();

            // A half sine of a negative amplitude never rises above the level it started from.
            Assert.True(kick.OffsetUnits <= 0f);

            if (kick.OffsetUnits < deepest)
            {
                deepest = kick.OffsetUnits;
                deepestTick = tick;
            }
        }

        // The trough sits at the midpoint of the fifteen ticks. It misses the full amplitude by half a
        // percent because the phase steps over pi/2 rather than landing on it: sin(7pi/15) is 0.9945.
        Assert.Equal(-HumanFallDamage.MaximumViewKickUnits, deepest, 6f);
        Assert.InRange(deepestTick, 6, 8);
    }

    [Fact]
    public void TheDipLastsTheFifteenTicksTheQuarterTurnOfPhaseBuys()
    {
        HumanLandingViewKick kick = new();
        kick.Install(-512f);

        int ticks = 0;
        while (kick.IsActive)
        {
            kick.Advance();
            ticks++;
        }

        // 0.20943952 is pi/15, so the phase reaches pi on the fifteenth step -- half a second at 30 Hz.
        Assert.Equal(HumanLandingViewKick.DurationTicks, ticks);

        kick.Advance();
        Assert.Equal(0f, kick.OffsetUnits);
    }

    [Fact]
    public void TheRigTakesAnExactShareOfTheCamera()
    {
        HumanLandingViewKick kick = new();
        kick.Install(-1024f);

        for (int tick = 0; tick < 5; tick++)
        {
            kick.Advance();
            Assert.Equal(kick.OffsetUnits * 0.075f, kick.ViewModelOffsetUnits, 1e-4f);
        }
    }

    [Fact]
    public void LandingAgainMidDipRestartsThePhase()
    {
        HumanLandingViewKick kick = new();
        kick.Install(-1024f);

        // Walk into the trough, where the view is at its lowest.
        for (int tick = 0; tick < 8; tick++)
        {
            kick.Advance();
        }

        Assert.True(kick.OffsetUnits < -900f);

        // 0x4659B0 zeroes the phase as well as writing the amplitude, so the dip starts over.
        kick.Install(-1024f);
        kick.Advance();

        Assert.Equal(0f, kick.OffsetUnits);
        Assert.True(kick.IsActive);
    }

    [Fact]
    public void InstallingZeroDisarmsTheDip()
    {
        HumanLandingViewKick kick = new();
        kick.Install(-1024f);
        kick.Advance();

        // The original's gate is a literal `!= 0.0` on the amplitude; a landing at rest arms nothing.
        kick.Install(0f);
        kick.Advance();

        Assert.False(kick.IsActive);
        Assert.Equal(0f, kick.OffsetUnits);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-136.5333f, -128f)]
    [InlineData(-1092.2667f, -1024f)]
    [InlineData(-4096f, -1024f)]
    public void TheAmplitudeIsTheLandingsOwnClampedKick(float velocityUnitsPerTick, float expectedKick)
    {
        // -clamp(speed * 0.125, 0, 1) * 1024, so the dip reaches its full quarter-metre at 8 m/s and
        // every faster landing -- 4096 units a tick is 30 m/s -- drops the view exactly as far.
        HumanFallImpact impact = HumanFallDamage.Calculate(velocityUnitsPerTick, 1f);

        Assert.Equal(expectedKick, impact.ViewKickUnits, 0.5f);
    }
}
