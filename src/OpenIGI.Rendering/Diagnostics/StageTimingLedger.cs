namespace OpenIGI.Rendering.Diagnostics;

/// <summary>
/// The rolling statistics behind the stage timings: what each stage costs, what the worst frame cost,
/// and how much of the GPU frame no stage claims.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the measuring because none of it is a graphics question. Where the numbers come from
/// differs by backend — timestamp queries written around a pass, read back once the frame has drained —
/// but what is done with them is arithmetic, and arithmetic that is worth checking: a rolling window, a
/// percentile, a peak that must survive the window scrolling past it, and a warm-up that must be
/// discarded or the first frames' compile stalls are reported as the steady state.
/// </para>
/// <para>
/// Smoothed rather than instantaneous because a single frame's query is far noisier than the thing
/// being judged: the driver batches, the clock boosts, and a stage costing a steady 0.8 ms reports
/// anywhere from 0.4 to 2 ms frame to frame. A budget is a claim about the steady state, so the steady
/// state is what is shown.
/// </para>
/// </remarks>
public sealed class StageTimingLedger
{
    private static readonly int StageCount = Enum.GetValues<RenderStage>().Length;

    private readonly int _window;
    private readonly int _warmUpFrames;
    private readonly Queue<double>[] _samples;
    private readonly double[] _sampleTotals;
    private readonly double[] _peak;
    private readonly bool[] _active;
    private readonly double[] _frame;
    private readonly Queue<double> _totalSamples = [];
    private readonly Queue<double> _measuredSamples = [];
    private readonly List<RenderStageTiming> _report = [];
    private double _totalSampleSum;
    private double _totalPeak;
    private double _measuredSampleSum;
    private double _measuredPeak;
    private int _completedFrames;
    private bool _reportStale = true;

    /// <summary>Initialises a new instance of the <see cref="StageTimingLedger"/> class.</summary>
    /// <param name="window">How many frames the rolling statistics cover.</param>
    /// <param name="warmUpFrames">
    /// How many completed frames to discard before recording anything. The first frames of a run are
    /// pipeline compiles and cold caches, and reporting them as the steady state makes every budget
    /// look blown.
    /// </param>
    public StageTimingLedger(int window = BenchmarkSchedule.MeasuredFrames, int warmUpFrames = BenchmarkSchedule.WarmUpFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmUpFrames);

        _window = window;
        _warmUpFrames = warmUpFrames;
        _samples = new Queue<double>[StageCount];
        _sampleTotals = new double[StageCount];
        _peak = new double[StageCount];
        _active = new bool[StageCount];
        _frame = new double[StageCount];

        for (int i = 0; i < StageCount; i++)
        {
            _samples[i] = new Queue<double>(window);
        }
    }

    /// <summary>Gets how many frames have been completed, warm-up included.</summary>
    public int CompletedFrames => _completedFrames;

    /// <summary>Adds what one stage cost in the frame being gathered.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="milliseconds">What it cost.</param>
    /// <remarks>
    /// Summed rather than replaced, because a stage that runs more than once in a frame — one shadow
    /// query per cascade — is still one stage: the budget is for the stage, not for one visit to it.
    /// </remarks>
    public void Observe(RenderStage stage, double milliseconds)
    {
        if (milliseconds > 0.0)
        {
            _frame[(int)stage] += milliseconds;
        }
    }

    /// <summary>Folds the gathered frame into the rolling figures.</summary>
    /// <param name="measuredMilliseconds">
    /// The whole GPU frame as measured end to end, or zero when it was not measured. Subtracting the
    /// sum of the stages from it leaves the work no stage claims, which is the one figure that can see
    /// a pass nobody timed — exactly how a millisecond of lamp-shadow tracing read as free for a whole
    /// session.
    /// </param>
    public void CompleteFrame(double measuredMilliseconds = 0.0)
    {
        _completedFrames++;

        bool warm = _completedFrames > _warmUpFrames;
        double total = 0.0;
        bool anyRan = false;

        Array.Clear(_active);

        for (int i = 0; i < _frame.Length; i++)
        {
            double milliseconds = _frame[i];

            if (milliseconds <= 0.0)
            {
                continue;
            }

            total += milliseconds;
            anyRan = true;

            if (!warm)
            {
                continue;
            }

            Push(_samples[i], ref _sampleTotals[i], milliseconds);
            _peak[i] = Math.Max(_peak[i], milliseconds);
            _active[i] = true;
            _reportStale = true;
        }

        if (warm && anyRan)
        {
            Push(_totalSamples, ref _totalSampleSum, total);
            _totalPeak = Math.Max(_totalPeak, total);

            if (measuredMilliseconds > 0.0)
            {
                Push(_measuredSamples, ref _measuredSampleSum, measuredMilliseconds);
                _measuredPeak = Math.Max(_measuredPeak, measuredMilliseconds);
            }
        }

        Array.Clear(_frame);
    }

    /// <summary>Gets the stages measured so far, in frame order.</summary>
    /// <returns>One entry per stage, whether or not it has ever run.</returns>
    public IReadOnlyList<RenderStageTiming> Timings()
    {
        if (!_reportStale)
        {
            return _report;
        }

        _report.Clear();

        for (int i = 0; i < StageCount; i++)
        {
            Queue<double> samples = _samples[i];

            _report.Add(new RenderStageTiming(
                (RenderStage)i,
                samples.Count > 0 ? _sampleTotals[i] / samples.Count : 0.0,
                samples.Count > 0 ? PerformanceStatistics.Percentile([.. samples], 0.95) : 0.0,
                _peak[i],
                _active[i],
                samples.Count));
        }

        _reportStale = false;
        return _report;
    }

    /// <summary>Gets the whole GPU frame as measured end to end, not as a sum of what was bracketed.</summary>
    /// <returns>The distribution, or nothing when no frame has been measured.</returns>
    public GpuFrameTiming MeasuredTiming() => Distribution(_measuredSamples, _measuredSampleSum, _measuredPeak);

    /// <summary>Gets the distribution of the per-frame sum, rather than a sum of stage percentiles.</summary>
    /// <returns>The distribution, or nothing when no frame has been folded.</returns>
    public GpuFrameTiming TotalTiming() => Distribution(_totalSamples, _totalSampleSum, _totalPeak);

    /// <summary>Forgets the history.</summary>
    /// <remarks>
    /// So that peaks reported after switching timing back on belong to the run being watched rather
    /// than to whatever happened the last time it was on.
    /// </remarks>
    public void Reset()
    {
        foreach (Queue<double> samples in _samples)
        {
            samples.Clear();
        }

        Array.Clear(_sampleTotals);
        Array.Clear(_peak);
        Array.Clear(_active);
        Array.Clear(_frame);

        _totalSamples.Clear();
        _totalSampleSum = 0.0;
        _totalPeak = 0.0;
        _measuredSamples.Clear();
        _measuredSampleSum = 0.0;
        _measuredPeak = 0.0;
        _completedFrames = 0;

        _report.Clear();
        _reportStale = true;
    }

    private static GpuFrameTiming Distribution(Queue<double> samples, double sum, double peak) =>
        samples.Count == 0
            ? default
            : new GpuFrameTiming(
                sum / samples.Count,
                PerformanceStatistics.Percentile([.. samples], 0.95),
                peak,
                samples.Count);

    /// <summary>Adds a sample to a window, dropping the oldest when it is full.</summary>
    /// <remarks>
    /// The running sum is kept alongside rather than recomputed, which is why the dropped sample has to
    /// be subtracted here and not anywhere else.
    /// </remarks>
    private void Push(Queue<double> samples, ref double sum, double value)
    {
        if (samples.Count == _window)
        {
            sum -= samples.Dequeue();
        }

        samples.Enqueue(value);
        sum += value;
    }
}
