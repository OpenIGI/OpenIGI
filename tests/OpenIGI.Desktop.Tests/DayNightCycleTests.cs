using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Desktop.World;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The cycle's opening state, held to the one thing a player would notice: a mission opens under the sun
/// its authors placed, whatever hour they placed it at, unless somebody asked for another.
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
}
