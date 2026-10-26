using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Both games' weather as the originals run it (<c>Rain_Update</c>, <c>0x499CF0</c>; I.G.I. 2's <c>Rain_UpdateParticles</c>).</summary>
public sealed class RetailPrecipitationTests
{
    [Fact]
    public void It_fades_in_by_a_sixtieth_a_tick_while_it_falls_and_out_when_it_stops()
    {
        RetailPrecipitation rain = new(snow: false) { IsFalling = true };
        rain.Tick();
        Assert.Equal(RetailPrecipitation.FadeStep, rain.Fade, 6);

        for (int i = 0; i < 100; i++)
        {
            rain.Tick();
        }

        Assert.Equal(1f, rain.Fade);
        rain.IsFalling = false;
        rain.Tick();
        Assert.Equal(1f - RetailPrecipitation.FadeStep, rain.Fade, 6);
    }

    [Fact]
    public void Nothing_falls_or_comes_in_while_it_is_faded_out()
    {
        RetailPrecipitation rain = new(snow: false);
        rain.Tick();
        Assert.Equal(0, rain.Count);
    }

    [Fact]
    public void A_drop_comes_in_per_307_units_fallen_until_there_are_eighty()
    {
        RetailPrecipitation rain = new(snow: false) { IsFalling = true };

        // 2048 units the first tick: six drops, 204.8 carried over.
        rain.Tick();
        Assert.Equal(6, rain.Count);
        Assert.All(rain.Drops.ToArray(), drop => Assert.Equal(RetailPrecipitation.LayerUnits - RetailPrecipitation.RainFallUnits, drop.Z));

        for (int i = 0; i < 40; i++)
        {
            rain.Tick();
        }

        Assert.Equal(RetailPrecipitation.MaximumDrops, rain.Count);
    }

    [Fact]
    public void A_drop_falls_through_its_layer_and_comes_round_to_the_top()
    {
        RetailPrecipitation rain = new(snow: false) { IsFalling = true };
        rain.Tick();
        float before = rain.Drops[0].Z;
        rain.Tick();
        Assert.Equal(before - RetailPrecipitation.RainFallUnits, rain.Drops[0].Z, 3);

        for (int i = 0; i < 20; i++)
        {
            rain.Tick();
        }

        Assert.All(rain.Drops.ToArray(), drop => Assert.InRange(drop.Z, 0f, RetailPrecipitation.LayerUnits));
    }

    [Fact]
    public void A_flake_drifts_but_stays_in_its_tile()
    {
        RetailPrecipitation snow = new(snow: true) { IsFalling = true };
        for (int i = 0; i < 600; i++)
        {
            snow.Tick();
        }

        Assert.Equal(RetailPrecipitation.MaximumDrops, snow.Count);
        Assert.All(snow.Drops.ToArray(), flake =>
        {
            Assert.InRange(flake.X, 0f, RetailPrecipitation.TileUnits);
            Assert.InRange(flake.Y, 0f, RetailPrecipitation.TileUnits);
            Assert.InRange(flake.Group, 0, 3);
        });
    }
}
