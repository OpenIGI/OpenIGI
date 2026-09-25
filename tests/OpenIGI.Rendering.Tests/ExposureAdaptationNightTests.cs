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
    public void A_full_night_lets_it_open_four_tenths_of_a_stop_further()
    {
        // Four tenths, not two: with the whole frame metered, a night's average sits on this floor, so the
        // floor is the night's exposure, and it is held where the approved night's views already were.
        Assert.Equal(10f * MathF.Pow(2f, 0.4f), new ExposureAdaptation().Adapt(DarkScene(), 1f / 60f, darkAdaptation: 1f), 3);
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
