using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

public sealed class BenchmarkScheduleTests
{
    [Fact]
    public void PresentationClockDoesNotAccumulateRoundingDrift()
    {
        Assert.Equal(8, BenchmarkSchedule.MillisecondsForFrame(0));
        Assert.Equal(33, BenchmarkSchedule.MillisecondsForFrame(3));
        Assert.Equal(1000, BenchmarkSchedule.MillisecondsForFrame(119));
        Assert.Equal(721, BenchmarkSchedule.MeasurementEndFrame);
        Assert.Equal(727, BenchmarkSchedule.TotalFrames);
        Assert.Equal(
            BenchmarkSchedule.MeasuredFrames + 1,
            BenchmarkSchedule.MeasurementEndFrame - BenchmarkSchedule.WarmUpFrames);
        Assert.Equal(4, BenchmarkSchedule.PresentationRate / BenchmarkSchedule.SimulationRate);
    }
}
