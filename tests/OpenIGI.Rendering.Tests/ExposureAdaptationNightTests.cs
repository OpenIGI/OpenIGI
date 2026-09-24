using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That the eye opens further under a night sky than under a dim day, and no further than the histogram
/// can measure.
/// </summary>
public sealed class ExposureAdaptationNightTests
{
    [Fact]
    public void A_dim_day_stops_the_eye_at_ten_times()
    {
        Assert.Equal(10f, new ExposureAdaptation().Adapt(DarkScene(), 1f / 60f), 3);
    }

    [Fact]
    public void A_full_night_lets_it_open_two_stops_further()
    {
        Assert.Equal(40f, new ExposureAdaptation().Adapt(DarkScene(), 1f / 60f, darkAdaptation: 1f), 1);
    }

    [Fact]
    public void A_bright_scene_is_metered_the_same_by_night_as_by_day()
    {
        float day = new ExposureAdaptation().Adapt(Scene(bin: 40), 1f / 60f);
        float night = new ExposureAdaptation().Adapt(Scene(bin: 40), 1f / 60f, darkAdaptation: 1f);

        Assert.Equal(day, night, 5);
    }

    // Every pixel in the second bucket: 2^-7.8, darker than either floor.
    private static uint[] DarkScene() => Scene(bin: 1);

    private static uint[] Scene(int bin)
    {
        uint[] histogram = new uint[ExposureAdaptation.Bins];
        histogram[bin] = 1_000_000u;
        return histogram;
    }
}
