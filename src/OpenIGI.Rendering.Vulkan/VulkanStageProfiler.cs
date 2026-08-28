using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// Times each <see cref="RenderStage"/> on the GPU with timestamp queries, the Vulkan counterpart to
/// <c>GLStageProfiler</c>.
/// </summary>
/// <remarks>
/// <para>
/// A stage is bracketed by two <c>vkCmdWriteTimestamp</c> calls recorded into the frame's command
/// buffer; the difference between the two values, scaled by the device's <c>timestampPeriod</c>, is the
/// GPU nanoseconds the stage took. Timing GPU work on the CPU with a stopwatch measures only how long
/// the driver took to write the commands, which is unrelated to how long the GPU took to run them — so
/// the measurement has to ride in the command stream.
/// </para>
/// <para>
/// <b>Why the results lag, and why that costs nothing here.</b> A query's result is not ready when the
/// stage ends; it is ready once the GPU has drained that frame. The frame loop already waits on a
/// slot's fence before it hands the slot back out, so by the time a slot comes round again its previous
/// frame's timestamps are guaranteed readable. This reads them then — two frames late — rather than
/// stalling the pipeline to read them now. A number two frames old is just as good for judging a budget.
/// </para>
/// <para>
/// <b>One pool, partitioned by slot.</b> Each in-flight slot owns a fixed block of the pool. A frame
/// resets its own block at the top (outside any render pass) and writes into it; the block is not read
/// until the same slot is reused, by when the fence guarantees the writes have landed. Stages that run
/// more than once in a frame — one shadow query per cascade — sum into the stage they belong to, because
/// the budget is for the stage, not for one visit to it.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanStageProfiler : IDisposable
{
    private const int RollingSamples = BenchmarkSchedule.MeasuredFrames;
    private const int WarmUpFrames = BenchmarkSchedule.WarmUpFrames;

    // Timestamps per slot per frame. Two per stage range, and a frame times well under thirty ranges
    // (roughly twenty stages, a few of them more than once), so this is comfortable headroom.
    private const int SlotCapacity = 128;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly double _periodMilliseconds;
    private readonly int _slots;
    private readonly QueryPool _pool;

    private readonly Queue<double>[] _samples = CreateSampleQueues();
    private readonly double[] _sampleTotals = new double[StageCount];
    private readonly double[] _peak = new double[StageCount];
    private readonly bool[] _seen = new bool[StageCount];
    private readonly bool[] _active = new bool[StageCount];

    private readonly Queue<double> _totalSamples = new(RollingSamples);
    private double _totalSampleSum;
    private double _totalPeak;

    // A frame's per-stage sums, reused as each slot's results are read back.
    private readonly double[] _frameStage = new double[StageCount];

    // Per-slot recording state. Each slot's ranges name query indices inside that slot's block, and are
    // read back the next time the slot is used.
    private readonly List<Range>[] _slotRanges;
    private readonly int[] _slotNext;
    private readonly bool[] _slotOpen;
    private readonly RenderStage[] _slotOpenStage;
    private readonly uint[] _slotOpenStart;

    private readonly ulong[] _readback = new ulong[SlotCapacity];
    private readonly List<RenderStageTiming> _report = [];

    private bool _enabled;
    private bool _reportStale = true;
    private bool _disposed;
    private long _readFrames;

    /// <summary>Creates a profiler on a device that supports timestamps.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool lives on.</param>
    /// <param name="timestampPeriodNanoseconds">Nanoseconds per timestamp tick, from the device limits.</param>
    /// <param name="framesInFlight">How many slots the frame loop cycles through.</param>
    public VulkanStageProfiler(Vk vk, VulkanDevice device, float timestampPeriodNanoseconds, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);

        _vk = vk;
        _device = device.Device;
        _periodMilliseconds = timestampPeriodNanoseconds / 1_000_000.0;
        _slots = framesInFlight;

        _slotRanges = new List<Range>[framesInFlight];
        _slotNext = new int[framesInFlight];
        _slotOpen = new bool[framesInFlight];
        _slotOpenStage = new RenderStage[framesInFlight];
        _slotOpenStart = new uint[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _slotRanges[i] = [];
        }

        QueryPoolCreateInfo createInfo = new()
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = (uint)(SlotCapacity * framesInFlight),
        };

        QueryPool pool;
        VkCheck.Ok(_vk.CreateQueryPool(_device, &createInfo, null, &pool), "vkCreateQueryPool");
        _pool = pool;
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

    /// <summary>
    /// Opens the frame: reads back the results this slot last recorded, then resets the slot's block so
    /// this frame can write into it. Must run outside a render pass, at the top of the frame.
    /// </summary>
    /// <param name="commandBuffer">The frame's command buffer, freshly begun.</param>
    /// <param name="slot">The in-flight slot this frame owns.</param>
    public void BeginFrame(CommandBuffer commandBuffer, int slot)
    {
        if (_disposed || !_enabled)
        {
            return;
        }

        // The slot's previous frame is complete — the frame loop waited its fence before handing the
        // slot back — so whatever it recorded is now readable.
        if (_slotRanges[slot].Count > 0)
        {
            ReadSlot(slot);
        }

        _slotRanges[slot].Clear();
        _slotNext[slot] = 0;
        _slotOpen[slot] = false;

        _vk.CmdResetQueryPool(commandBuffer, _pool, (uint)(slot * SlotCapacity), (uint)SlotCapacity);
    }

    /// <summary>Gets the stage currently being timed on <paramref name="slot"/>, or null if none is.</summary>
    /// <remarks>
    /// So a range that has to be interrupted -- the geometry pass closing early because something wants to
    /// dispatch compute over its results -- can be put back afterwards instead of leaving everything that
    /// follows it untimed.
    /// </remarks>
    public RenderStage? OpenStage(int slot) => _slotOpen[slot] ? _slotOpenStage[slot] : null;

    /// <summary>Starts timing a stage.</summary>
    /// <param name="stage">The stage about to run.</param>
    /// <param name="commandBuffer">The command buffer being recorded.</param>
    /// <param name="slot">The in-flight slot this frame owns.</param>
    /// <remarks>Does nothing when timing is off, when a stage is already open, or when the block is full.</remarks>
    public void Begin(RenderStage stage, CommandBuffer commandBuffer, int slot)
    {
        if (_disposed || !_enabled || _slotOpen[slot] || _slotNext[slot] + 2 > SlotCapacity)
        {
            return;
        }

        uint index = (uint)((slot * SlotCapacity) + _slotNext[slot]);
        _vk.CmdWriteTimestamp(commandBuffer, PipelineStageFlags.BottomOfPipeBit, _pool, index);
        _slotNext[slot]++;
        _slotOpen[slot] = true;
        _slotOpenStage[slot] = stage;
        _slotOpenStart[slot] = index;
    }

    /// <summary>Stops timing the stage begun by the matching <see cref="Begin"/>.</summary>
    /// <param name="commandBuffer">The command buffer being recorded.</param>
    /// <param name="slot">The in-flight slot this frame owns.</param>
    public void End(CommandBuffer commandBuffer, int slot)
    {
        if (_disposed || !_slotOpen[slot])
        {
            return;
        }

        uint index = (uint)((slot * SlotCapacity) + _slotNext[slot]);
        _vk.CmdWriteTimestamp(commandBuffer, PipelineStageFlags.BottomOfPipeBit, _pool, index);
        _slotNext[slot]++;
        _slotOpen[slot] = false;
        _slotRanges[slot].Add(new Range(_slotOpenStage[slot], _slotOpenStart[slot], index));
    }

    /// <summary>Gets the stages measured so far, in frame order.</summary>
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
        _vk.DestroyQueryPool(_device, _pool, null);
    }

    // Reads one slot's recorded ranges and folds them into the rolling figures as a single frame.
    private void ReadSlot(int slot)
    {
        int count = _slotNext[slot];
        if (count <= 0)
        {
            return;
        }

        uint first = (uint)(slot * SlotCapacity);
        fixed (ulong* data = _readback)
        {
            // The fence for this slot has been waited, so every query in the block has landed; ask for
            // 64-bit results without the wait bit, which would otherwise re-stall on already-ready data.
            Result result = _vk.GetQueryPoolResults(
                _device,
                _pool,
                first,
                (uint)count,
                (nuint)(count * sizeof(ulong)),
                data,
                sizeof(ulong),
                QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit);

            if (result != Result.Success)
            {
                return;
            }
        }

        Array.Clear(_frameStage);
        bool anyRan = false;
        foreach (Range range in _slotRanges[slot])
        {
            int startLocal = (int)(range.Start - first);
            int endLocal = (int)(range.End - first);
            if (startLocal < 0 || endLocal >= count || endLocal <= startLocal)
            {
                continue;
            }

            // Unsigned so a tick counter that wrapped between the two reads still subtracts correctly.
            ulong ticks = _readback[endLocal] - _readback[startLocal];
            _frameStage[(int)range.Stage] += ticks * _periodMilliseconds;
            anyRan = true;
        }

        Fold(anyRan);
    }

    // Folds the frame accumulated in _frameStage into the rolling samples, discarding the warm-up.
    private void Fold(bool anyRan)
    {
        _readFrames++;
        bool warm = _readFrames > WarmUpFrames;

        Array.Clear(_active);
        double gpuTotal = 0.0;
        bool anyStageRan = false;

        for (int i = 0; i < _frameStage.Length; i++)
        {
            if (_frameStage[i] <= 0.0)
            {
                continue;
            }

            double milliseconds = _frameStage[i];
            gpuTotal += milliseconds;
            anyStageRan = true;

            if (!warm)
            {
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

        _ = anyRan;
    }

    // Forgets the history, so peaks reported after switching timing back on belong to the run watched.
    private void Reset()
    {
        foreach (Queue<double> samples in _samples)
        {
            samples.Clear();
        }

        Array.Clear(_sampleTotals);
        Array.Clear(_peak);
        Array.Clear(_seen);
        Array.Clear(_active);
        Array.Clear(_frameStage);

        _totalSamples.Clear();
        _totalSampleSum = 0.0;
        _totalPeak = 0.0;
        _readFrames = 0;

        for (int i = 0; i < _slots; i++)
        {
            _slotRanges[i].Clear();
            _slotNext[i] = 0;
            _slotOpen[i] = false;
        }

        _report.Clear();
        _reportStale = true;
    }

    private readonly record struct Range(RenderStage Stage, uint Start, uint End);
}
