using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>The Brightness setting on the original's direct path, as the Modulate2X fills that carry it.</summary>
public sealed class DirectBrightnessTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.004f)]
    [InlineData(float.NaN)]
    public void NeutralBrightnessDrawsNothing(float brightness)
    {
        Span<Bgra32> fills = stackalloc Bgra32[DirectBrightness.MaximumFills];
        Assert.Equal(0, DirectBrightness.Fills(brightness, targetEncodes: true, fills));
    }

    [Theory]
    [InlineData(0.5f, true, 1)]
    [InlineData(1.414f, true, 1)]
    [InlineData(3f, false, 2)]
    [InlineData(16f, true, 4)]
    public void TheFillsTogetherScaleLinearLightByTheSetting(float brightness, bool targetEncodes, int expectedFills)
    {
        Span<Bgra32> fills = stackalloc Bgra32[DirectBrightness.MaximumFills];
        int count = DirectBrightness.Fills(brightness, targetEncodes, fills);

        Assert.Equal(expectedFills, count);

        float scale = 1f;
        foreach (Bgra32 fill in fills[..count])
        {
            float display = fill.R / 255f;

            // What one Modulate2X fill does to linear light: on an encoding target the blend runs on decoded
            // values, on a plain one on the encoding.
            scale *= targetEncodes ? 2f * DecodeSrgb(display) : MathF.Pow(2f * display, 2.2f);
        }

        Assert.InRange(scale, brightness * 0.98f, brightness * 1.02f);
    }

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}
