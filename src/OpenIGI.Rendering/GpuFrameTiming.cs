namespace OpenIGI.Rendering;

/// <summary>Statistics for the sum of all timed GPU stages in each completed frame.</summary>
public readonly record struct GpuFrameTiming(
    double Milliseconds,
    double P95Milliseconds,
    double PeakMilliseconds,
    int SampleCount);
