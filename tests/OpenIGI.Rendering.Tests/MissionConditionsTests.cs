using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins what the campaign screen's hour and weather choices actually mean to the renderer.
/// </summary>
public sealed class MissionConditionsTests
{
    /// <summary>
    /// Default hands the level back its own hour, which is the only value that reproduces the authored sun.
    /// </summary>
    /// <remarks>
    /// Negative is <c>r_time_of_day</c>'s own word for "the level's own", so the mapping has to stay
    /// negative rather than merely small: a zero would be a real hour -- midnight -- and would quietly
    /// turn "default" into the darkest choice of the three.
    /// </remarks>
    [Fact]
    public void DefaultKeepsTheLevelsOwnHour()
    {
        Assert.True(MissionConditions.Default.StartHour < 0f);
        Assert.Equal(MissionTimeOfDay.Default, MissionConditions.Default.TimeOfDay);
        Assert.Equal(MissionWeather.Default, MissionConditions.Default.Weather);
        Assert.False(MissionConditions.Default.ForcesWeather);
    }

    /// <summary>Both choices open an hour clear of the horizon, on opposite sides of the arc.</summary>
    /// <remarks>
    /// The offset is the whole of what makes either choice legible. At the crossing itself the sun is half
    /// a sun and the level reads the same ambiguous half-light whichever side of the day it is on: a night
    /// that looks like a late afternoon, and a day that is a wall of orange. So the test is not only that
    /// the hours are on the right sides of the arc -- <c>((hour - 6) / 12) * pi</c> is the phase the sun's
    /// elevation is a sine of -- but that neither of them sits ON the crossing.
    /// </remarks>
    [Fact]
    public void DayAndNightBothOpenClearOfTheHorizon()
    {
        MissionConditions day = MissionConditions.Default with { TimeOfDay = MissionTimeOfDay.Day };
        MissionConditions night = MissionConditions.Default with { TimeOfDay = MissionTimeOfDay.Night };

        Assert.Equal(MissionConditions.SunriseHour + MissionConditions.HorizonOffsetHours, day.StartHour);
        Assert.Equal(MissionConditions.SunsetHour + MissionConditions.HorizonOffsetHours, night.StartHour);

        // Sun up at the day hour, sun down at the night hour, and neither of them grazing zero.
        float dayElevation = MathF.Sin((day.StartHour - 6f) / 12f * MathF.PI);
        float nightElevation = MathF.Sin((night.StartHour - 6f) / 12f * MathF.PI);

        Assert.True(dayElevation > 0.2f, $"day opens too near the horizon (sin {dayElevation})");
        Assert.True(nightElevation < -0.2f, $"night opens too near the horizon (sin {nightElevation})");
    }

    /// <summary>A chosen weather overrides the player's own style and falls in levels that author none.</summary>
    /// <remarks>
    /// Both halves matter. Without the override a campaign set to snow would rain in the three missions
    /// whose data says rain; without the forcing it would do nothing at all in the other eleven.
    /// </remarks>
    [Theory]
    [InlineData(MissionWeather.Rain, WeatherKind.Rain)]
    [InlineData(MissionWeather.Snow, WeatherKind.Snow)]
    public void AChosenWeatherOverridesAndForces(MissionWeather chosen, WeatherKind expected)
    {
        MissionConditions conditions = MissionConditions.Default with { Weather = chosen };

        Assert.Equal(expected, conditions.WeatherStyle(WeatherKind.Rain));
        Assert.Equal(expected, conditions.WeatherStyle(WeatherKind.Snow));
        Assert.True(conditions.ForcesWeather);
    }

    /// <summary>Default leaves the style the player's own setting asks for.</summary>
    [Theory]
    [InlineData(WeatherKind.Rain)]
    [InlineData(WeatherKind.Snow)]
    public void DefaultWeatherLeavesThePlayersOwnStyle(WeatherKind preference) =>
        Assert.Equal(preference, MissionConditions.Default.WeatherStyle(preference));
}
