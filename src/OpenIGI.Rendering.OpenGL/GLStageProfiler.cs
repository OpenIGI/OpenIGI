using OpenIGI.Rendering;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// Times each <see cref="RenderStage"/> on the GPU with <c>GL_TIME_ELAPSED</c> queries.
/// </summary>
/// <remarks>
/// <para>
/// The remaster track set a per-stage frame budget at R3 and then went five stages without measuring
/// anything against it. This is the instrument that makes the budget checkable: shadows ≤1.0 ms, fog
/// ≤1.5, occlusion ≤1.0, lighting ≤2.0, reflections ≤1.5, post ≤1.5, at 1080p on ultra.
/// </para>
/// <para>
/// <b>Why not a stopwatch.</b> Timing GL calls on the CPU measures how long the driver took to write
/// commands into a buffer, which is unrelated to how long the GPU took to run them — the GPU is
/// typically a frame behind, and a stage that costs 3 ms of GPU time can be submitted in 20 µs. A
/// timer query is recorded <em>in the command stream</em> and answers with GPU nanoseconds.
/// </para>
/// <para>
/// <b>Why the results lag.</b> A query's result is not available when the stage ends; it is available
/// once the GPU has caught up, one or two frames later. Reading it immediately means blocking until
/// the GPU drains, which destroys the pipelining that the frame time depends on — so this never asks
/// for a result it has not first been told is ready, and reports slightly stale numbers instead. That
/// is the right trade for an overlay: a number two frames old is just as good for judging a budget,
/// and a stall would make every number wrong.
/// </para>
/// <para>
/// <b>One at a time.</b> GL allows exactly one active <c>GL_TIME_ELAPSED</c> query per context — they
/// cannot nest. The stages are sequential, so this is not a constraint in practice, but a stage begun
/// while another is open is dropped rather than allowed to corrupt both.
/// </para>
/// </remarks>
internal sealed class GLStageProfiler : IDisposable
{
    private const int RollingSamples = BenchmarkSchedule.MeasuredFrames;

    /// <summary>The most queries left outstanding before new stages stop being timed.</summary>
    /// <remarks>
    /// A backstop against a driver that never reports a result: without it the pool would grow by one
    /// query per stage per frame forever. Three frames' worth of stages is far more than the one or two
    /// frames of lag that actually occur.
    /// </remarks>
    private const int MaxPending = 128;

    /// <summary>How many frames are measured and thrown away before anything is believed.</summary>
    /// <remarks>
    /// The frames right after a level loads are not the frames a budget is about: textures are still
    /// uploading, shaders are still being compiled by the driver, and the first shadow render measured
    /// 226 ms against a steady-state 0.6. Left in, that one frame is the peak for the rest of the run
    /// and the peak column says nothing about play.
    /// </remarks>
    private const int WarmUpFrames = BenchmarkSchedule.WarmUpFrames;

    private readonly GL _gl;
    private readonly Queue<double>[] _samples = CreateSampleQueues();
    private readonly Queue<double> _totalSamples = new(RollingSamples);
    private readonly double[] _sampleTotals = new double[StageCount];
    private double _totalSampleSum;
    private double _totalPeak;
    private readonly double[] _peak = new double[StageCount];
    private readonly bool[] _seen = new bool[StageCount];
    private readonly bool[] _active = new bool[StageCount];

    // A stage may be timed more than once in a frame -- there is one shadow query per cascade -- and
    // the budget is for the stage, not for one visit to it. So a frame's samples are summed here and
    // only folded into the smoothed figure once the frame is known to be finished, which is when a
    // query belonging to a later frame turns up.
    private readonly double[] _frameTotal = new double[StageCount];
    private readonly bool[] _frameRan = new bool[StageCount];

    private readonly Queue<uint> _spare = new();
    private readonly List<Pending> _pending = [];
    private readonly List<RenderStageTiming> _report = [];

    private bool _enabled;
    private bool _open;
    private bool _reportStale = true;
    private bool _disposed;
    private long _frame;
    private long _accumulating = -1;

    /// <summary>Creates a profiler on a context.</summary>
    /// <param name="gl">The GL entry points.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gl"/> is <see langword="null"/>.</exception>
    public GLStageProfiler(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
    }

    private static int StageCount => Enum.GetValues<RenderStage>().Length;

    private static Queue<double>[] CreateSampleQueues()
    {
        Queue<double>[] result = new Queue<double>[StageCount];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Queue<double>(RollingSamples);
        }

        return result;
    }

    /// <summary>Gets or sets whether stages are timed.</summary>
    /// <remarks>Switching it off forgets the history, so peaks belong to the run being watched.</remarks>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            if (!value)
            {
                Reset();
            }
        }
    }

    /// <summary>Starts timing a stage.</summary>
    /// <param name="stage">The stage about to run.</param>
    /// <remarks>Does nothing when timing is off, or when another stage is already being timed.</remarks>
    public bool Begin(RenderStage stage)
    {
        if (!_enabled || _open || _disposed || _pending.Count >= MaxPending)
        {
            return false;
        }

        uint query = _spare.Count > 0 ? _spare.Dequeue() : _gl.GenQuery();
        _gl.BeginQuery(QueryTarget.TimeElapsed, query);
        _pending.Add(new Pending(stage, query, _frame));
        _open = true;
        return true;
    }

    /// <summary>Stops timing the stage begun by the matching <see cref="Begin"/>.</summary>
    public void End()
    {
        if (!_open)
        {
            return;
        }

        _gl.EndQuery(QueryTarget.TimeElapsed);
        _open = false;
    }

    /// <summary>Collects whatever results the driver has ready, without waiting for any of them.</summary>
    /// <remarks>Called once a frame, after the frame's work has been submitted.</remarks>
    public void Collect()
    {
        if (_disposed)
        {
            return;
        }

        // Front to back: the queries were issued in order, so the first unready one means every query
        // after it is unready too, and testing the rest is wasted calls.
        int resolved = 0;

        while (resolved < _pending.Count)
        {
            Pending entry = _pending[resolved];

            _gl.GetQueryObject(entry.Query, QueryObjectParameterName.ResultAvailable, out int ready);
            if (ready == 0)
            {
                break;
            }

            _gl.GetQueryObject(entry.Query, QueryObjectParameterName.Result, out ulong nanoseconds);

            // A query from a later frame is the proof that the one being accumulated is complete. There
            // is no other signal: the driver reports results when it reports them, and a stage that did
            // not run this frame issues no query to say so.
            if (entry.Frame != _accumulating)
            {
                Flush();
                _accumulating = entry.Frame;
            }

            _frameTotal[(int)entry.Stage] += nanoseconds / 1_000_000.0;
            _frameRan[(int)entry.Stage] = true;

            _spare.Enqueue(entry.Query);
            resolved++;
        }

        if (resolved > 0)
        {
            _pending.RemoveRange(0, resolved);
        }

        _frame++;
    }

    /// <summary>Gets the stages measured so far, in frame order.</summary>
    /// <returns>One entry per known stage. Stages absent from the last frame are marked inactive.</returns>
    public IReadOnlyList<RenderStageTiming> Timings()
    {
        if (!_reportStale)
        {
            return _report;
        }

        _report.Clear();

        for (int i = 0; i < _seen.Length; i++)
        {
            Queue<double> samples = _samples[i];
            double average = samples.Count > 0 ? _sampleTotals[i] / samples.Count : 0.0;
            double p95 = samples.Count > 0
                ? PerformanceStatistics.Percentile(samples.ToArray(), 0.95)
                : 0.0;
            _report.Add(new RenderStageTiming(
                (RenderStage)i,
                average,
                p95,
                _peak[i],
                _active[i],
                samples.Count));
        }

        _reportStale = false;
        return _report;
    }

    /// <summary>Gets the distribution of the per-frame sum, rather than a sum of stage percentiles.</summary>
    public GpuFrameTiming TotalTiming()
    {
        if (_totalSamples.Count == 0)
        {
            return default;
        }

        return new(
            _totalSampleSum / _totalSamples.Count,
            PerformanceStatistics.Percentile(_totalSamples.ToArray(), 0.95),
            _totalPeak,
            _totalSamples.Count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        End();

        foreach (Pending entry in _pending)
        {
            _gl.DeleteQuery(entry.Query);
        }

        foreach (uint query in _spare)
        {
            _gl.DeleteQuery(query);
        }

        _pending.Clear();
        _spare.Clear();
    }

    /// <summary>Folds a finished frame's totals into the smoothed figures.</summary>
    private void Flush()
    {
        // Measured and discarded rather than not measured: the queries have to run through the warm-up
        // for the pool to be warm too, and a query issued for the first time is itself slower.
        bool warm = _accumulating >= WarmUpFrames;
        double gpuTotal = 0.0;
        bool anyStageRan = false;

        Array.Clear(_active);

        for (int i = 0; i < _frameRan.Length; i++)
        {
            if (!_frameRan[i])
            {
                continue;
            }

            double milliseconds = _frameTotal[i];
            gpuTotal += milliseconds;
            anyStageRan = true;

            if (!warm)
            {
                _frameTotal[i] = 0.0;
                _frameRan[i] = false;
                continue;
            }

            Queue<double> samples = _samples[i];
            if (samples.Count == RollingSamples)
            {
                _sampleTotals[i] -= samples.Dequeue();
            }

            samples.Enqueue(milliseconds);
            _sampleTotals[i] += milliseconds;

            _peak[i] = Math.Max(_peak[i], milliseconds);
            _seen[i] = true;
            _active[i] = true;
            _frameTotal[i] = 0.0;
            _frameRan[i] = false;
            _reportStale = true;
        }

        if (warm && anyStageRan)
        {
            if (_totalSamples.Count == RollingSamples)
            {
                _totalSampleSum -= _totalSamples.Dequeue();
            }

            _totalSamples.Enqueue(gpuTotal);
            _totalSampleSum += gpuTotal;
            _totalPeak = Math.Max(_totalPeak, gpuTotal);
        }
    }

    private void Reset()
    {
        foreach (Queue<double> samples in _samples)
        {
            samples.Clear();
        }

        Array.Clear(_sampleTotals);
        _totalSamples.Clear();
        _totalSampleSum = 0.0;
        _totalPeak = 0.0;
        Array.Clear(_peak);
        Array.Clear(_seen);
        Array.Clear(_active);
        Array.Clear(_frameTotal);
        Array.Clear(_frameRan);
        _accumulating = -1;

        // Closed before anything is let go of. Switching the overlay off can land between a Begin and
        // its End, and both deleting an active query and handing one back to the pool for a later
        // BeginQuery to pick up are errors.
        End();

        // Deleted rather than recycled. Their results are of no interest now, but the GPU has not
        // necessarily reached them yet, and a query cannot be re-begun while it is still in flight.
        foreach (Pending entry in _pending)
        {
            _gl.DeleteQuery(entry.Query);
        }

        _pending.Clear();
        _report.Clear();
        _reportStale = true;

        // So the warm-up is paid again the next time it is switched on: whatever is being investigated
        // then is a different run, and may well be a different level.
        _frame = 0;
    }

    private readonly record struct Pending(RenderStage Stage, uint Query, long Frame);

    /// <summary>Times a stage for as long as it is in scope.</summary>
    /// <remarks>
    /// The stages guard themselves — the fog returns early when the fog is off — and each has several
    /// ways out. A scope puts the <see cref="End"/> on every one of them, and placing its declaration
    /// below a stage's guards is what keeps a stage that did nothing out of the report entirely, rather
    /// than in it reading zero.
    /// </remarks>
    public readonly struct Scope : IDisposable
    {
        private readonly GLStageProfiler? _owner;

        internal Scope(GLStageProfiler? owner, RenderStage stage)
        {
            _owner = owner?.Begin(stage) == true ? owner : null;
        }

        /// <summary>Closes the stage.</summary>
        public void Dispose() => _owner?.End();
    }
}
