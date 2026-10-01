namespace OpenIGI.Rendering;

/// <summary>Shared constants and exact clock mapping for deterministic GPU benchmarks.</summary>
public static class BenchmarkSchedule
{
    /// <summary>Presentation samples per synthetic second.</summary>
    public const int PresentationRate = 120;

    /// <summary>Simulation ticks per synthetic second.</summary>
    public const int SimulationRate = 30;

    /// <summary>Frames discarded while shaders, queries and residency settle.</summary>
    public const int WarmUpFrames = 120;

    /// <summary>Frames included in the output distribution.</summary>
    public const int MeasuredFrames = 600;

    /// <summary>Last frame index included by the wall-frame tracker.</summary>
    /// <remarks>
    /// Frame durations are intervals, so the first post-warm-up frame establishes the timestamp and
    /// the following 600 frames produce the requested 600 measurements.
    /// </remarks>
    public const int MeasurementEndFrame = WarmUpFrames + MeasuredFrames + 1;

    /// <summary>Unmeasured frames that let asynchronous GL queries become readable without a stall.</summary>
    public const int QueryDrainFrames = 6;

    /// <summary>Total rendered frames in one benchmark process.</summary>
    public const int TotalFrames = MeasurementEndFrame + QueryDrainFrames;

    /// <summary>Maps a zero-based presentation frame to its absolute synthetic clock.</summary>
    /// <param name="frame">The zero-based presentation frame.</param>
    /// <returns>Absolute synthetic milliseconds, rounded down without accumulated drift.</returns>
    public static int MillisecondsForFrame(long frame) =>
        (int)(((frame + 1L) * 1000L) / PresentationRate);
}
