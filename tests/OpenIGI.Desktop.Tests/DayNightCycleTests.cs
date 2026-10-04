using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The cycle's opening state, held to the one thing a player would notice: a mission opens under the sun
/// its authors placed, whatever hour they placed it at, unless somebody asked for another -- and, where
/// the player has asked time to pass, the rate it then passes at and how far into night that has got.
/// </summary>
public sealed class DayNightCycleTests
{
    private static DirectionalLightSettings AuthoredAt(float hour)
    {
        // A mid-afternoon sun, low in the west, so noon and the authored hour disagree plainly.
        Vector3f direction = new(-0.55f, 0.35f, 0.76f);
        direction /= direction.Length;
        return new DirectionalLightSettings(
            direction,
            new Vector3f(1f, 0.95f, 0.85f),
            new Vector3f(0.2f, 0.22f, 0.25f),
            Beta: 0f,
            Gamma: 0f,
            TimeOfDay: hour,
            Intensity: 18f,
            AffectsTerrain: true,
            AffectsObjects: true);
    }

    [Fact]
    public void ALevelOpensOnItsOwnAuthoredSun()
    {
        DirectionalLightSettings authored = AuthoredAt(15.5f);
        var cycle = new DayNightCycle();

        cycle.ResetForLevel(authored);
        DirectionalLightSettings applied = cycle.Apply(authored);

        Assert.Equal(15.5f, cycle.TimeHours, 4);
        Assert.Equal(authored.Direction.X, applied.Direction.X, 3);
        Assert.Equal(authored.Direction.Y, applied.Direction.Y, 3);
        Assert.Equal(authored.Direction.Z, applied.Direction.Z, 3);
    }

    [Fact]
    public void AFixedNoonIsNotTheAuthoredSunForAnAfternoonLevel()
    {
        // The reason the reset keys on the level's hour: the arc only passes through the authored
        // direction at the authored hour.
        DirectionalLightSettings authored = AuthoredAt(15.5f);
        var cycle = new DayNightCycle();

        cycle.Reset(12f);
        DirectionalLightSettings applied = cycle.Apply(authored);

        float dot = (applied.Direction.X * authored.Direction.X)
            + (applied.Direction.Y * authored.Direction.Y)
            + (applied.Direction.Z * authored.Direction.Z);
        Assert.True(dot < 0.999f, $"noon should not reproduce a 15:30 sun, but the directions agree to {dot:F4}");
    }

    [Fact]
    public void AnExplicitHourWinsOverTheAuthoredOne()
    {
        var cycle = new DayNightCycle();
        cycle.ResetForLevel(AuthoredAt(15.5f), overrideHours: 22f);
        Assert.Equal(22f, cycle.TimeHours, 4);
    }

    /// <summary>A minute of play, at the day lengths r_day_length spans: a minute, the default, half a day.</summary>
    [Theory]
    [InlineData(60f)]
    [InlineData(2400f)]
    [InlineData(43200f)]
    public void TheAutomaticCycleAdvancesTheClockAtWhateverRateItIsGiven(float secondsPerDay)
    {
        var cycle = new DayNightCycle();
        var keyboard = new KeyboardState();
        cycle.Reset(12f);

        // The rate is the player's, so what is fixed is not a number of hours but the relationship: a
        // minute of play is a minute's worth of whatever a day has been set to cost.
        const float playSeconds = 60f;
        float step = secondsPerDay / DayNightCycle.AutomaticTicksPerDay;

        // Counted rather than accumulated towards a float total: at the fast end this is 3600 passes of a
        // sixtieth of a second, and summing that in single precision lands a hair under a minute and buys
        // one extra pass -- which is a whole tick of sky, and a test failing on its own arithmetic.
        int passes = (int)MathF.Round(playSeconds / step);

        for (int i = 0; i < passes; i++)
        {
            cycle.Update(keyboard, step, secondsPerDay);
        }

        float expected = (12f + (24f * (passes * step / secondsPerDay))) % 24f;
        Assert.Equal(expected, cycle.TimeHours, 3);
    }

    [Fact]
    public void ADayLengthOfNoughtIsTheClockStandingStill()
    {
        // How the caller says "time is not passing" -- a menu, a paused mission -- without a second flag
        // that could disagree with the rate beside it.
        var cycle = new DayNightCycle();
        var keyboard = new KeyboardState();
        cycle.Reset(15.5f);

        for (int i = 0; i < 600; i++)
        {
            cycle.Update(keyboard, 1f, automaticSecondsPerDay: 0f);
        }

        Assert.Equal(15.5f, cycle.TimeHours, 4);
    }

    [Theory]
    [InlineData(1f / 240f)]
    [InlineData(1f / 30f)]
    [InlineData(0.25f)]
    [InlineData(2f)]
    public void TheSameMinuteOfPlayIsTheSameMuchDayAtAnyFrameRate(float passSeconds)
    {
        // The clock moves in whole ticks, so a pass shorter than one has to BANK its seconds rather than
        // drop them -- otherwise a fast machine would run a slower day than a slow one, which is the bug
        // this shape of clock invites. Checked as the property it actually is: a minute of play is a
        // minute's worth of sky however the minute was cut up, to within the one tick still banked at the
        // end. Comparing two clocks outright would NOT do it -- one of them legitimately holds unspent
        // seconds -- and that is exactly the mistake this test was written as first.
        const float secondsPerDay = 2400f;
        const float playSeconds = 60f;

        var cycle = new DayNightCycle();
        var keyboard = new KeyboardState();
        cycle.Reset(12f);

        int passes = (int)MathF.Round(playSeconds / passSeconds);

        for (int i = 0; i < passes; i++)
        {
            cycle.Update(keyboard, passSeconds, secondsPerDay);
        }

        float expected = 12f + (24f * (passes * passSeconds / secondsPerDay));
        float step = secondsPerDay / DayNightCycle.AutomaticTicksPerDay;
        float tolerance = 24f * ((step + passSeconds) / secondsPerDay);

        Assert.True(
            MathF.Abs(cycle.TimeHours - expected) <= tolerance,
            $"a minute of play in {passSeconds:F4}s passes reached {cycle.TimeHours:F5}h, not {expected:F5}h "
                + $"(tolerance {tolerance:F5}h): the ticking is losing or inventing time at this frame rate");
    }

    [Fact]
    public void NightfallIsNoughtUnderTheAuthoredMiddaySunAndOneAtMidnight()
    {
        // What the exposure reads to know how much brightness to take off. Midday must give back exactly
        // nothing, or a level nobody has scrubbed would open darker than its authors lit it.
        DirectionalLightSettings authored = AuthoredAt(12f);
        var cycle = new DayNightCycle();

        cycle.ResetForLevel(authored);
        cycle.Apply(authored);
        Assert.Equal(0f, cycle.Nightfall, 4);

        cycle.Reset(0f);
        cycle.Apply(authored);
        Assert.Equal(1f, cycle.Nightfall, 4);
    }

    [Fact]
    public void NightfallComesOnThroughDuskRatherThanAtAMoment()
    {
        // The half of the ask the endpoints cannot show: between the two it has to move, and only ever
        // one way, so the picture darkens into the night and lightens back out of it.
        DirectionalLightSettings authored = AuthoredAt(12f);
        var cycle = new DayNightCycle();

        float previous = -1f;
        bool moved = false;

        for (float hour = 12f; hour <= 24f; hour += 0.5f)
        {
            cycle.Reset(hour % 24f);
            cycle.Apply(authored);

            Assert.True(
                cycle.Nightfall >= previous - 1e-5f,
                $"nightfall fell back from {previous:F4} to {cycle.Nightfall:F4} at {hour:F1}h");

            moved |= cycle.Nightfall is > 0.01f and < 0.99f;
            previous = cycle.Nightfall;
        }

        Assert.True(moved, "nightfall never took an intermediate value: the transition is a step, not a fade");
    }

    [Fact]
    public void ALevelWithNoUsableHourOpensAtNoon()
    {
        var cycle = new DayNightCycle();

        cycle.ResetForLevel(null);
        Assert.Equal(12f, cycle.TimeHours, 4);

        cycle.ResetForLevel(AuthoredAt(0f));
        Assert.Equal(12f, cycle.TimeHours, 4);
    }

    [Fact]
    public void TheMoonStandsOppositeTheSun()
    {
        DirectionalLightSettings authored = AuthoredAt(12f);
        var cycle = new DayNightCycle();

        cycle.Reset(15f);
        DirectionalLightSettings applied = cycle.Apply(authored);

        Assert.Equal(-applied.Direction.X, cycle.MoonDirection.X, 4);
        Assert.Equal(-applied.Direction.Y, cycle.MoonDirection.Y, 4);
        Assert.Equal(-applied.Direction.Z, cycle.MoonDirection.Z, 4);
    }

    [Fact]
    public void TheStarsComeOutOnlyOnceTheSunIsWellDown()
    {
        DirectionalLightSettings authored = AuthoredAt(12f);
        var cycle = new DayNightCycle();

        cycle.Reset(12f);
        cycle.Apply(authored);
        Assert.Equal(0f, cycle.StarVisibility);

        cycle.Reset(0f);
        cycle.Apply(authored);
        Assert.Equal(1f, cycle.StarVisibility);
    }

    [Fact]
    public void DuskAndDawnNeverPassThroughTotalDarkness()
    {
        // A full moon rises as the sun sets, so between day and night the light hands over rather than
        // going out. It used to go out: the sun faded to nothing just under the horizon and only then did
        // the moon begin to rise, which left every dusk and dawn a stretch with no light and no sky at all.
        //
        // Measured as the direct light and the fill together, because that is what the ground gets: with
        // both sun and moon on the horizon the direct light is honestly dim, and the twilight sky carries
        // the scene. Together they must never fall under a moonlit night's.
        DirectionalLightSettings authored = AuthoredAt(12f);
        var cycle = new DayNightCycle();
        float floor = Luminance(DayNightCycle.DefaultNightColour) + Luminance(DayNightCycle.DefaultNightAmbient);

        for (float hour = 0f; hour < 24f; hour += 0.01f)
        {
            cycle.Reset(hour);
            DirectionalLightSettings applied = cycle.Apply(authored);
            float lit = Luminance(applied.Colour) + Luminance(applied.Ambient);

            Assert.True(
                lit >= floor * 0.999f,
                $"At {hour:0.00} h the light and fill are {lit:0.#####}, under a moonlit night's {floor:0.#####}.");
        }
    }

    /// <summary>The sun's height asked for on its own is the height the frame lights with.</summary>
    /// <remarks>
    /// The lamps' photocells read it from outside the frame, before the sun has been applied. If the two
    /// answers drifted apart, a lamp would come on under a sky the picture still shows as daylight.
    /// </remarks>
    [Fact]
    public void TheSunsHeightOnItsOwnIsTheHeightTheFrameLightsWith()
    {
        DirectionalLightSettings authored = AuthoredAt(15.5f);
        var cycle = new DayNightCycle();

        for (float hour = 0f; hour < 24f; hour += 0.25f)
        {
            // Re-oriented as well, since the photocells must see the sun the player has turned.
            cycle.Reset(hour);
            cycle.RotateAxis(2, 0.4f);
            float asked = cycle.SunSine(authored);
            cycle.Apply(authored);

            Assert.Equal(cycle.SunDirection.Z, asked, 5);
        }
    }

    /// <summary>A level's opening hour is marked as one; the clock being scrubbed or reset by key is not.</summary>
    [Fact]
    public void OnlyALevelsOpeningMovesTheEpoch()
    {
        var cycle = new DayNightCycle();
        int before = cycle.LevelEpoch;

        cycle.Reset(6f);
        Assert.Equal(before, cycle.LevelEpoch);

        cycle.ResetForLevel(AuthoredAt(15.5f));
        Assert.Equal(before + 1, cycle.LevelEpoch);
    }

    private static float Luminance(Vector3f c) => (0.2126f * c.X) + (0.7152f * c.Y) + (0.0722f * c.Z);
}
