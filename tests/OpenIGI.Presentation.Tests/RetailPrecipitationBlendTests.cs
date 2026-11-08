using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class RetailPrecipitationBlendTests
{
    [Fact]
    public void A_blend_of_one_draws_the_tick_itself()
    {
        RetailPrecipitation rain = Ticked(snow: false, ticks: 20);
        WeatherDrop[] placed = new WeatherDrop[rain.Count];

        RetailPrecipitationRenderer.Interpolate(rain.Drops, snow: false, rain.FallUnits, 1f, placed);

        Assert.Equal(rain.Drops.ToArray(), placed);
    }

    [Fact]
    public void A_blend_of_nought_draws_the_tick_before()
    {
        RetailPrecipitation rain = Ticked(snow: false, ticks: 20);
        WeatherDrop[] before = rain.Drops.ToArray();
        rain.Tick();
        WeatherDrop[] placed = new WeatherDrop[rain.Count];

        RetailPrecipitationRenderer.Interpolate(rain.Drops, snow: false, rain.FallUnits, 0f, placed);

        // Drops let in on the last tick were not there before it; every older one is back where it stood.
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].Z, placed[i].Z, 2);
        }
    }

    [Fact]
    public void A_drop_that_came_round_its_layer_goes_back_to_its_foot()
    {
        WeatherDrop[] drops = [new(10f, 20f, RetailPrecipitation.LayerUnits - 100f, 0, 0f, 0f, 0)];
        WeatherDrop[] placed = new WeatherDrop[1];

        RetailPrecipitationRenderer.Interpolate(drops, snow: false, RetailPrecipitation.RainFallUnits, 0.5f, placed);

        Assert.Equal(RetailPrecipitation.RainFallUnits * 0.5f - 100f, placed[0].Z, 2);
    }

    [Fact]
    public void A_flake_is_drawn_back_along_its_drift()
    {
        RetailPrecipitation snow = Ticked(snow: true, ticks: 200);
        WeatherDrop[] before = snow.Drops.ToArray();
        snow.Tick();
        WeatherDrop[] placed = new WeatherDrop[snow.Count];

        RetailPrecipitationRenderer.Interpolate(snow.Drops, snow: true, snow.FallUnits, 0f, placed);

        for (int i = 0; i < before.Length; i++)
        {
            // A flake that crossed into the next tile is drawn just over its new tile's edge: the same place in the world.
            float dx = placed[i].X - before[i].X, dy = placed[i].Y - before[i].Y;
            Assert.True(Wraps(dx), $"flake {i} across: {dx}");
            Assert.True(Wraps(dy), $"flake {i} along: {dy}");
        }
    }

    private static bool Wraps(float delta) =>
        MathF.Abs(delta) < 0.05f || MathF.Abs(MathF.Abs(delta) - RetailPrecipitation.TileUnits) < 0.05f;

    private static RetailPrecipitation Ticked(bool snow, int ticks)
    {
        RetailPrecipitation weather = new(snow) { IsFalling = true };
        for (int i = 0; i < ticks; i++)
        {
            weather.Tick();
        }

        return weather;
    }
}
