using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The gust and glimmer fields: that they vary, and that they vary <em>smoothly</em>.</summary>
/// <remarks>
/// Both faults these guard against are invisible in a screenshot. A field that came out constant makes
/// the storm look even rather than broken; a field with no correlation length makes it look slightly
/// dirty, and in neither case does anyone think to suspect the noise.
/// </remarks>
public sealed class StormNoiseTests
{
    [Fact]
    public void SmoothNoise_StaysInsideZeroToOne()
    {
        for (int i = 0; i < 2000; i++)
        {
            float t = i * 0.137f;
            float value = StormRenderer.SmoothNoise(t, t * 0.31f, -t * 0.77f);
            Assert.InRange(value, 0f, 1f);
        }
    }

    [Fact]
    public void SmoothNoise_MovesLittleOverASmallStep()
    {
        // The property the gust field is entirely about: two drops a hand's breadth apart, in a field
        // whose cell is metres across, must be given nearly the same density. The raw hash fails this
        // by an order of magnitude, which is what made the sheets of rain not read as sheets.
        float worst = 0f;
        for (int i = 0; i < 500; i++)
        {
            float t = i * 0.911f;
            float here = StormRenderer.SmoothNoise(t, t * 0.5f, t * 0.25f);
            float there = StormRenderer.SmoothNoise(t + 0.01f, t * 0.5f, t * 0.25f);
            worst = MathF.Max(worst, MathF.Abs(here - there));
        }

        Assert.True(worst < 0.05f, $"a hundredth of a cell moved the field by {worst:F3}");
    }

    [Fact]
    public void SmoothNoise_IsNotConstant()
    {
        float low = 1f;
        float high = 0f;
        for (int i = 0; i < 500; i++)
        {
            float value = StormRenderer.SmoothNoise(i * 1.7f, i * 0.3f, i * 2.1f);
            low = MathF.Min(low, value);
            high = MathF.Max(high, value);
        }

        Assert.True(high - low > 0.5f, $"the field only spans {high - low:F2}");
    }

    [Fact]
    public void SmoothNoise_AgreesWithTheHashOnTheLattice()
    {
        // It is the hash, interpolated — so at whole coordinates the two must be the same number.
        for (int i = -3; i <= 3; i++)
        {
            Assert.Equal(StormRenderer.Noise(i, i * 2, i * 3), StormRenderer.SmoothNoise(i, i * 2, i * 3), 4);
        }
    }
}
