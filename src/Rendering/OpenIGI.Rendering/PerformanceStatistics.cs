namespace OpenIGI.Rendering;

/// <summary>Allocation-free helpers shared by CPU and GPU performance trackers.</summary>
public static class PerformanceStatistics
{
    /// <summary>Returns a nearest-rank percentile without changing the caller's samples.</summary>
    public static double Percentile(ReadOnlySpan<double> samples, double percentile)
    {
        if (samples.IsEmpty)
        {
            return 0.0;
        }

        double[] sorted = samples.ToArray();
        Array.Sort(sorted);
        int rank = (int)Math.Ceiling(Math.Clamp(percentile, 0.0, 1.0) * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
