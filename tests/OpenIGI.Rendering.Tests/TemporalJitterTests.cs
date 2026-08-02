using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins the sub-pixel offsets temporal anti-aliasing samples with.
/// </summary>
/// <remarks>
/// These are worth testing because their failures are invisible in the shape of the output and only
/// show up as a picture that never quite converges. A sequence that clumps, or that leaves a corner of
/// the pixel unvisited, still produces a plausible image — just a slightly wrong one, permanently.
/// </remarks>
public sealed class TemporalJitterTests
{
    [Theory]
    [InlineData(1, 2, 0.5f)]
    [InlineData(2, 2, 0.25f)]
    [InlineData(3, 2, 0.75f)]
    [InlineData(5, 2, 0.625f)]
    [InlineData(1, 3, 1f / 3f)]
    [InlineData(4, 3, 4f / 9f)]
    public void TheRadicalInverseMirrorsTheIndexsDigitsAboutThePoint(int index, int radix, float expected) =>
        Assert.Equal(expected, TemporalJitter.Halton(index, radix), 5);

    [Fact]
    public void EveryOffsetStaysInsideItsOwnPixel()
    {
        // Outside ±0.5 the sample belongs to the neighbouring pixel, and accumulating it is a blur
        // being built deliberately.
        for (int frame = 0; frame < TemporalJitter.Length * 3; frame++)
        {
            (float x, float y) = TemporalJitter.At(frame);

            Assert.InRange(x, -0.5f, 0.5f);
            Assert.InRange(y, -0.5f, 0.5f);
        }
    }

    [Fact]
    public void TheSequenceRepeatsOnItsPeriodAndSurvivesANegativeFrame()
    {
        Assert.Equal(TemporalJitter.At(0), TemporalJitter.At(TemporalJitter.Length));
        Assert.Equal(TemporalJitter.At(3), TemporalJitter.At(TemporalJitter.Length + 3));

        // A frame counter that has wrapped past int.MaxValue must not index backwards out of the
        // sequence, which is a crash rather than a wrong picture.
        (float x, float y) = TemporalJitter.At(-1);
        Assert.InRange(x, -0.5f, 0.5f);
        Assert.InRange(y, -0.5f, 0.5f);
    }

    [Fact]
    public void NoTwoFramesInAPeriodSampleTheSamePoint()
    {
        HashSet<(float, float)> seen = [];

        for (int frame = 0; frame < TemporalJitter.Length; frame++)
        {
            Assert.True(seen.Add(TemporalJitter.At(frame)), $"frame {frame} repeats an earlier offset");
        }
    }

    [Fact]
    public void EveryQuadrantOfThePixelIsVisitedWithinOnePeriod()
    {
        // The property that makes it low-discrepancy rather than merely non-repeating: a history that
        // survives only a few frames should already be an even average, not one biased into a corner.
        int[] quadrants = new int[4];

        for (int frame = 0; frame < TemporalJitter.Length; frame++)
        {
            (float x, float y) = TemporalJitter.At(frame);
            quadrants[((x < 0f) ? 0 : 1) + ((y < 0f) ? 0 : 2)]++;
        }

        Assert.All(quadrants, count => Assert.True(count >= 3, $"a quadrant took only {count} of 16 samples"));
    }

    [Fact]
    public void TheOffsetsAverageToNearThePixelCentre()
    {
        float sumX = 0f;
        float sumY = 0f;

        for (int frame = 0; frame < TemporalJitter.Length; frame++)
        {
            (float x, float y) = TemporalJitter.At(frame);
            sumX += x;
            sumY += y;
        }

        // A sequence whose mean is off-centre shifts the whole converged image by that much, which
        // reads as the picture being subtly misaligned rather than as anti-aliasing being wrong.
        Assert.Equal(0f, sumX / TemporalJitter.Length, 1);
        Assert.Equal(0f, sumY / TemporalJitter.Length, 1);
    }
}
