using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

public sealed class PerformanceStatisticsTests
{
    [Fact]
    public void PercentileUsesNearestRankWithoutChangingInput()
    {
        double[] samples = [4, 1, 3, 2, 100];

        Assert.Equal(100, PerformanceStatistics.Percentile(samples, 0.95));
        Assert.Equal([4, 1, 3, 2, 100], samples);
    }

    [Fact]
    public void FrameTrackerReportsAverageP95PeakAndOnePercentLow()
    {
        FramePerformanceTracker tracker = new();
        for (int i = 0; i < 99; i++)
        {
            tracker.Add(5);
        }
        tracker.Add(10);

        FramePerformanceStatistics result = tracker.Statistics();
        Assert.Equal(100, result.OnePercentLowFramesPerSecond, 6);
        Assert.Equal(5, result.P95Milliseconds, 6);
        Assert.Equal(10, result.PeakMilliseconds, 6);
        Assert.Equal(100, result.SampleCount);
    }
}
