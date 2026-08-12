using System.Diagnostics;

namespace OpenIGI.Rendering;

/// <summary>Tracks wall-frame FPS and tail latency over a bounded rolling window.</summary>
public sealed class FramePerformanceTracker
{
    private const int Capacity = 600;
    private readonly double[] _milliseconds = new double[Capacity];
    private int _next;
    private int _count;
    private long _lastTimestamp;

    /// <summary>Discards all samples and the timestamp used to start the next interval.</summary>
    public void Reset()
    {
        _next = 0;
        _count = 0;
        _lastTimestamp = 0;
    }

    /// <summary>Adds the interval since the previous call using the monotonic system timer.</summary>
    public void NoteFrame() => NoteTimestamp(Stopwatch.GetTimestamp());

    /// <summary>Adds the interval since a supplied monotonic timestamp.</summary>
    /// <param name="timestamp">A timestamp expressed in <see cref="Stopwatch.Frequency"/> units.</param>
    public void NoteTimestamp(long timestamp)
    {
        if (_lastTimestamp != 0 && timestamp > _lastTimestamp)
        {
            Add((timestamp - _lastTimestamp) * 1000.0 / Stopwatch.Frequency);
        }

        _lastTimestamp = timestamp;
    }

    /// <summary>Adds a precomputed wall-frame duration.</summary>
    /// <param name="milliseconds">The positive finite frame duration.</param>
    public void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0.0)
        {
            return;
        }

        _milliseconds[_next] = milliseconds;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    /// <summary>Calculates current average, tail latency and FPS figures.</summary>
    /// <returns>The current statistics, or a default value before any samples.</returns>
    public FramePerformanceStatistics Statistics()
    {
        if (_count == 0)
        {
            return default;
        }

        Span<double> samples = stackalloc double[_count];
        double sum = 0.0;
        double peak = 0.0;
        for (int i = 0; i < _count; i++)
        {
            double value = _milliseconds[i];
            samples[i] = value;
            sum += value;
            peak = Math.Max(peak, value);
        }

        double average = sum / _count;
        double p95 = PerformanceStatistics.Percentile(samples, 0.95);
        double[] sorted = samples.ToArray();
        Array.Sort(sorted);
        int slowCount = Math.Max(1, (int)Math.Ceiling(sorted.Length * 0.01));
        double slowTotal = 0.0;
        for (int i = sorted.Length - slowCount; i < sorted.Length; i++)
        {
            slowTotal += sorted[i];
        }
        double slowAverage = slowTotal / slowCount;
        return new(
            1000.0 / average,
            average,
            slowAverage > 0.0 ? 1000.0 / slowAverage : 0.0,
            p95,
            peak,
            _count);
    }
}

/// <summary>Rolling wall-frame statistics used by the overlay and benchmark output.</summary>
/// <param name="FramesPerSecond">FPS derived from the mean frame duration.</param>
/// <param name="AverageMilliseconds">Mean wall-frame duration.</param>
/// <param name="OnePercentLowFramesPerSecond">FPS derived from the 99th-percentile duration.</param>
/// <param name="P95Milliseconds">95th-percentile wall-frame duration.</param>
/// <param name="PeakMilliseconds">Worst wall-frame duration.</param>
/// <param name="SampleCount">Number of rolling samples.</param>
public readonly record struct FramePerformanceStatistics(
    double FramesPerSecond,
    double AverageMilliseconds,
    double OnePercentLowFramesPerSecond,
    double P95Milliseconds,
    double PeakMilliseconds,
    int SampleCount);
