using OpenIGI.Rendering.Diagnostics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Diagnostics;

/// <summary>
/// Times each <see cref="RenderStage"/> on the GPU, with the timestamps written by the passes
/// themselves.
/// </summary>
/// <remarks>
/// <para>
/// Timing GPU work on the host with a stopwatch measures how long the driver took to write the
/// commands, which is unrelated to how long the GPU took to run them — so the measurement has to ride
/// in the command stream. Here that means a query set, and a pair of slots per pass that the pass
/// writes into as it begins and ends.
/// </para>
/// <para>
/// <b>A pass rather than an arbitrary range.</b> The RHI only lets a pass be timed, not whatever commands
/// happen to lie between two points, because that is the only shape a browser has. It is also the more honest
/// measurement: a timestamp written outside a pass measures the gap between passes as well as the pass. A
/// stage that spans several passes sums them, which is what the ledger already does for a stage that runs
/// more than once.
/// </para>
/// <para>
/// <b>Why the results lag, and why that costs nothing.</b> A query's result is not ready when the pass
/// ends; it is ready once the GPU has drained that frame. Each frame in flight owns a block of the set,
/// and a block is read when its slot comes round again — by which time the frame that wrote it has
/// certainly finished. A number two frames old is just as good for judging a budget as one that cost a
/// stall to obtain.
/// </para>
/// </remarks>
public sealed class StageProfiler : IDisposable
{
    /// <summary>How many timestamps one frame in flight may write.</summary>
    /// <remarks>
    /// Two per timed pass. Every global-illumination dispatch is a pass of its own, so an ultra frame
    /// times well over sixty; a frame that exceeds this stops timing rather than overwriting its
    /// neighbour's block, and what it stopped on would be missing from every total.
    /// </remarks>
    public const int SlotCapacity = 512;

    private readonly IRhiDevice _device;
    private readonly IRhiQuerySet? _queries;
    private readonly IRhiBuffer? _resolved;
    private readonly IRhiBuffer[] _readback;
    private readonly IRhiReadback?[] _pending;
    private readonly double _periodMilliseconds;
    private readonly int _slots;
    private readonly List<TimedPass>[] _slotPasses;
    private readonly int[] _slotNext;

    // What each slot's readback buffer holds: the passes of the frame whose times were copied into it,
    // kept apart from the slot's own list because the frame after starts that list again long before a
    // map on a browser comes back.
    private readonly List<TimedPass>[] _copiedPasses;
    private readonly int[] _copiedCount;
    private readonly ulong[] _results;
    private int _slot;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="StageProfiler"/> class.</summary>
    /// <param name="device">The device to time on.</param>
    /// <param name="framesInFlight">How many frames may be in flight, which is how many blocks there are.</param>
    /// <param name="timestampPeriodNanoseconds">
    /// How many nanoseconds one tick of the device's timestamp counter is, as the device reports it.
    /// </param>
    /// <remarks>
    /// A device that does not offer timestamp queries builds nothing and reports
    /// <see cref="Available"/> false. Every other member then does nothing, so nothing above has to ask
    /// before timing.
    /// </remarks>
    public StageProfiler(IRhiDevice device, int framesInFlight, double timestampPeriodNanoseconds = 1.0)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _device = device;
        _slots = framesInFlight;
        _periodMilliseconds = timestampPeriodNanoseconds / 1_000_000.0;
        _slotPasses = new List<TimedPass>[framesInFlight];
        _slotNext = new int[framesInFlight];
        _copiedPasses = new List<TimedPass>[framesInFlight];
        _copiedCount = new int[framesInFlight];
        _results = new ulong[SlotCapacity];
        _readback = [];
        _pending = new IRhiReadback?[framesInFlight];

        for (int i = 0; i < framesInFlight; i++)
        {
            _slotPasses[i] = [];
            _copiedPasses[i] = [];
        }

        if (!device.Features.TimestampQuery)
        {
            return;
        }

        int queries = SlotCapacity * framesInFlight;

        _queries = device.CreateQuerySet(new RhiQuerySetDescriptor(RhiQueryType.Timestamp, queries, "stages"));

        _resolved = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(queries * sizeof(ulong)),
            RhiBufferUsage.QueryResolve | RhiBufferUsage.CopySource,
            "stages.resolved"));

        // One per slot, so reading one frame's timings never waits on the frame written after it: a
        // readback is done when the last write to ITS buffer is, and a shared buffer's last write is
        // always the newest frame.
        _readback = new IRhiBuffer[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _readback[i] = device.CreateBuffer(new RhiBufferDescriptor(
                SlotCapacity * sizeof(ulong),
                RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
                $"stages.readback.{i}"));
        }

        Available = true;
    }

    /// <summary>Gets whether this device can be timed at all.</summary>
    public bool Available { get; }

    /// <summary>Gets or sets whether timing is being recorded.</summary>
    /// <remarks>Switching it on forgets the history, so the peaks reported belong to the run watched.</remarks>
    public bool Enabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Ledger.Reset();
        }
    }

    /// <summary>Gets the rolling statistics the measurements are folded into.</summary>
    public StageTimingLedger Ledger { get; } = new();

    /// <summary>Starts a frame, on the block belonging to one of the frames in flight.</summary>
    /// <param name="frameInFlight">Which block this frame writes into.</param>
    public void BeginFrame(int frameInFlight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _slot = frameInFlight % _slots;
        _slotNext[_slot] = 0;
        _slotPasses[_slot].Clear();
    }

    /// <summary>Claims a pair of slots for one pass, to put on its descriptor.</summary>
    /// <param name="stage">Which stage the pass belongs to.</param>
    /// <param name="label">The pass's own name, for the breakdown within a stage, or <see langword="null"/>.</param>
    /// <returns>
    /// Where the pass should write its times, or <see langword="null"/> when nothing is being timed —
    /// which is what a pass descriptor takes for a pass that is not measured.
    /// </returns>
    public RhiTimestampWrites? Time(RenderStage stage, string? label = null)
    {
        if (_disposed || !Enabled || _queries is null)
        {
            return null;
        }

        int next = _slotNext[_slot];

        // A frame that wants more than its block holds stops timing rather than writing into the next
        // slot's, which would report another frame's work as this one's.
        if (next + 2 > SlotCapacity)
        {
            return null;
        }

        int first = (_slot * SlotCapacity) + next;
        _slotNext[_slot] = next + 2;
        _slotPasses[_slot].Add(new TimedPass(stage, next, next + 1, label));

        return new RhiTimestampWrites(_queries, first, first + 1);
    }

    /// <summary>Copies the frame's timestamps out of the query set, ready to be read.</summary>
    /// <param name="encoder">The encoder to record the resolve into, outside any pass.</param>
    public void ResolveFrame(IRhiCommandEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        if (_disposed || _queries is null || _resolved is null || _readback.Length == 0)
        {
            return;
        }

        int count = _slotNext[_slot];

        if (count <= 0)
        {
            return;
        }

        // A map still in flight is an earlier frame's times not yet read, and a buffer with a map
        // outstanding cannot be copied into: this frame goes untimed and that one is kept. Throwing the
        // map away instead, as this did, threw away every frame on WebGPU -- whose maps come back a frame
        // later than the slot comes round -- and it reported no stage times at all.
        if (_pending[_slot] is { IsComplete: false })
        {
            return;
        }

        // A map that HAS come back but was not read yet is folded now, before this frame's copy replaces it.
        // The read at the end of the frame looks at the previous frame, which a GPU-bound frame has not
        // finished by then; throwing it away here as well meant a GPU-bound game reported no GPU time at all.
        if (_pending[_slot] is not null)
        {
            ReadCompletedFrame(_slot);
        }

        _pending[_slot]?.Dispose();
        _pending[_slot] = null;

        int first = _slot * SlotCapacity;
        ulong offset = (ulong)(first * sizeof(ulong));

        encoder.ResolveQuerySet(_queries, first, count, _resolved, offset);
        encoder.CopyBufferToBuffer(_resolved, offset, _readback[_slot], 0, (ulong)(count * sizeof(ulong)));

        _copiedPasses[_slot].Clear();
        _copiedPasses[_slot].AddRange(_slotPasses[_slot]);
        _copiedCount[_slot] = count;
    }

    /// <summary>Starts mapping the times a frame slot has just submitted.</summary>
    /// <param name="frameInFlight">The slot the submission just sent copied into.</param>
    /// <remarks>
    /// As the frame is submitted, not when the slot comes round again: a map asked for then has only that
    /// moment to complete in, and on WebGPU it never has.
    /// </remarks>
    public void RequestReadback(int frameInFlight)
    {
        if (_disposed || !Enabled || _readback.Length == 0)
        {
            return;
        }

        int slot = frameInFlight % _slots;

        if (_copiedCount[slot] > 0)
        {
            _pending[slot] ??= _device.ReadBuffer(_readback[slot], 0, (ulong)(_copiedCount[slot] * sizeof(ulong)));
        }
    }

    /// <summary>Reads a completed frame's timestamps and folds them into the statistics.</summary>
    /// <param name="frameInFlight">
    /// The block to read, which must belong to a frame the GPU has finished — the slot about to be
    /// reused is the one that qualifies.
    /// </param>
    /// <returns><see langword="true"/> when a frame was folded.</returns>
    public bool ReadCompletedFrame(int frameInFlight)
    {
        if (_disposed || !Enabled || _readback.Length == 0)
        {
            return false;
        }

        int slot = frameInFlight % _slots;
        List<TimedPass> passes = _copiedPasses[slot];
        int count = _copiedCount[slot];

        if (passes.Count == 0 || count <= 0)
        {
            return false;
        }

        // Asked for once and kept until it arrives: a browser cannot block for it, and neither should this.
        IRhiReadback data = _pending[slot] ??= _device.ReadBuffer(_readback[slot], 0, (ulong)(count * sizeof(ulong)));

        if (!data.Wait(TimeSpan.Zero) && !data.IsComplete)
        {
            return false;
        }

        ReadOnlySpan<ulong> ticks = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(data.GetData());

        if (ticks.Length < count)
        {
            return false;
        }

        ticks[..count].CopyTo(_results);

        foreach (TimedPass pass in passes)
        {
            // Unsigned, so a tick counter that wrapped between the two reads still subtracts correctly.
            ulong elapsed = _results[pass.End] - _results[pass.Start];
            double milliseconds = elapsed * _periodMilliseconds;
            Ledger.Observe(pass.Stage, milliseconds);

            if (pass.Label is { } label)
            {
                Ledger.ObservePass(label, pass.Stage, milliseconds);
            }
        }

        Ledger.CompleteFrame();

        // Read once: the slot's next copy asks again.
        passes.Clear();
        _copiedCount[slot] = 0;
        data.Dispose();
        _pending[slot] = null;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IRhiReadback? pending in _pending)
        {
            pending?.Dispose();
        }

        foreach (IRhiBuffer buffer in _readback)
        {
            buffer.Dispose();
        }

        _resolved?.Dispose();
        _queries?.Dispose();
    }

    /// <summary>One pass that was timed, and where in the block its two times went.</summary>
    /// <param name="Stage">The stage it belongs to.</param>
    /// <param name="Start">Where in the block the time it began went.</param>
    /// <param name="End">Where the time it ended went.</param>
    /// <param name="Label">Its own name, when it was given one.</param>
    private readonly record struct TimedPass(RenderStage Stage, int Start, int End, string? Label);
}
