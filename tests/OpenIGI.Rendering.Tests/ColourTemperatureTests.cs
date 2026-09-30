using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>That a colour temperature changes a light's tint and never its strength.</summary>
public sealed class ColourTemperatureTests
{
    [Theory]
    [InlineData(2000f)]
    [InlineData(3000f)]
    [InlineData(5800f)]
    [InlineData(6504f)]
    [InlineData(10000f)]
    public void Every_temperature_is_equally_bright(float kelvin)
    {
        Vector3f colour = ColourTemperature.ToLinearRgb(kelvin);

        Assert.Equal(1f, (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z), 3);
        Assert.True(colour.X >= 0f && colour.Y >= 0f && colour.Z >= 0f);
    }

    [Fact]
    public void Daylight_is_very_nearly_white()
    {
        // D65 sits a hair off the Planckian locus, so "nearly" is the claim.
        Vector3f colour = ColourTemperature.ToLinearRgb(6504f);

        Assert.InRange(colour.X, 0.9f, 1.1f);
        Assert.InRange(colour.Y, 0.9f, 1.1f);
        Assert.InRange(colour.Z, 0.9f, 1.15f);
    }

    [Fact]
    public void An_old_bulb_is_warm_and_a_cold_led_is_blue()
    {
        Vector3f warm = ColourTemperature.ToLinearRgb(3000f);
        Vector3f cold = ColourTemperature.ToLinearRgb(10000f);

        Assert.True(warm.X > warm.Y && warm.Y > warm.Z);
        Assert.True(cold.Z > cold.X);
    }
}
