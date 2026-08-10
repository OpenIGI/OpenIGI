using OpenIGI.Core.Numerics;

namespace OpenIGI.Engine.Time;

/// <summary>
/// The fixed-timestep schedule: absolute per-tick deadlines, the catch-up cap, the one-render-per-tick
/// latch and the excluded-time stopwatch.
/// </summary>
/// <remarks>
/// <para>
/// This is the timing half of the original's root <c>Flow</c> task (fields <c>+0x30 … +0x50</c>), split
/// out so it can be reasoned about and tested without a task tree.
/// <see cref="OpenIGI.Engine.Flow.FlowTask"/> owns one and does nothing to it that <c>Flow_Update</c>
/// (<c>0x402260</c>) does not.
/// </para>
/// <para>
/// Every rule here is part of the determinism contract, and each exists for a reason that is not
/// obvious from the code: see the original for the disassembly it comes from.
/// The short version is that simulation time is <c>tickCount / tickRate</c> and never wall clock, so a
/// slow machine loses time instead of taking bigger steps.
/// </para>
/// </remarks>
public sealed class GameClock
{
    /// <summary>The simulation rate the engine runs at, in hertz.</summary>
    public const int DefaultTickRate = 30;

    /// <summary>The largest <see cref="CatchUpCount"/> a tick is still allowed at.</summary>
    /// <remarks>
    /// <para>
    /// Once <see cref="CatchUpCount"/> passes this, the arbiter forces a render even though the sim is
    /// behind. The simulation then permanently loses that time — deliberate slow motion under load,
    /// which physics and AI timings assume.
    /// </para>
    /// <para>
    /// The budget is tested before the tick and spent after it, so an arbitrarily long stall produces
    /// <em>eleven</em> consecutive ticks, not ten. Keeping the constant at the original's 10 rather than
    /// renaming it to the burst length keeps it checkable against the disassembly.
    /// </para>
    /// </remarks>
    public const int MaxCatchUpTicks = 10;

    /// <summary>The tick count below which a tick may only follow a render.</summary>
    public const int GuardedStartupTicks = 3;

    /// <summary>The number of opening ticks and frames whose cost is excluded from the schedule.</summary>
    public const int ExcludedStartupCount = 2;

    private readonly ITimeSource _time;
    private int _exclusionDepth;
    private int _exclusionStart;

    /// <summary>Initializes a new instance of the <see cref="GameClock"/> class.</summary>
    /// <param name="time">The wall-clock source.</param>
    /// <param name="tickRate">The initial simulation rate in hertz.</param>
    /// <exception cref="ArgumentNullException"><paramref name="time"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    public GameClock(ITimeSource time, int tickRate = DefaultTickRate)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        _time = time;
        SetTickRate(tickRate);
    }

    /// <summary>Gets the simulation rate in hertz.</summary>
    public int TickRate { get; private set; }

    /// <summary>Gets the number of simulation ticks since the last <see cref="SetTickRate"/>.</summary>
    public int TickCount { get; private set; }

    /// <summary>Gets the number of rendered frames since the last <see cref="SetTickRate"/>.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Gets the number of consecutive ticks since the last render.</summary>
    public int CatchUpCount { get; private set; }

    /// <summary>Gets a value indicating whether a frame has been rendered since the last tick.</summary>
    /// <remarks>
    /// The latch behind "at most one render per tick". <see cref="SetTickRate"/> sets it, which is what
    /// makes a tick — never a render — the first thing that happens after a rate change.
    /// </remarks>
    public bool HasRendered { get; private set; }

    /// <summary>Gets the clock reading tick zero is measured from.</summary>
    public int TimeBaseMilliseconds { get; private set; }

    /// <summary>Gets the accumulated time the schedule pretends did not happen.</summary>
    /// <seealso cref="BeginExcludedTime"/>
    public int ExcludedMilliseconds { get; private set; }

    /// <summary>Gets or sets a per-frame drift applied to the time base.</summary>
    /// <remarks>
    /// The original adds <c>Flow+0x4C</c> to <c>Flow+0x30</c> once per frame, unconditionally. Nothing in
    /// the retail build ever writes it, so it is zero in practice — but it is the documented way to slew
    /// the whole schedule, and leaving it out would quietly drop a feature.
    /// </remarks>
    public int TimeAdjustMilliseconds { get; set; }

    /// <summary>Gets a value indicating whether an excluded-time span is currently open.</summary>
    public bool IsExcludingTime => _exclusionDepth > 0;

    /// <summary>Gets or sets a value indicating whether renders may repeat between ticks.</summary>
    /// <remarks>
    /// <para>
    /// The original's render latch admits <em>one</em> render per tick, which caps the whole
    /// presentation at the simulation's 30 Hz however fast the machine is. With this set, a frame
    /// with no tick due renders instead of idling, so the display runs as fast as it can while the
    /// tick schedule — the deadlines, the catch-up budget, everything the simulation can observe —
    /// is untouched: ticks still fire exactly when their absolute deadlines pass.
    /// </para>
    /// <para>
    /// Off is the original's behaviour and stays the setting for the vanilla preset and for
    /// <c>-lockstep</c> runs, whose synthetic clock advances per frame and needs the original's
    /// one-to-one pairing to stay reproducible.
    /// </para>
    /// </remarks>
    public bool UncappedRenders { get; set; }

    /// <summary>Gets the current wall-clock reading.</summary>
    public int Now => _time.Milliseconds;

    /// <summary>Gets the deadline for the next tick, in the time source's milliseconds.</summary>
    /// <remarks>
    /// <para>
    /// <c>trunc(1000.0 / tickRate * tickCount + (int32)(timeBase + excluded))</c>. The target is absolute,
    /// derived from the tick <em>index</em>, so rounding never accumulates: the schedule is
    /// 0, 33, 66, 100, 133, … not repeated additions of 33.
    /// </para>
    /// <para>
    /// The two details that look like noise and are not: the sum of the base and the excluded time is a
    /// 32-bit integer addition performed <em>before</em> the widening to <see cref="double"/>, so it
    /// wraps; and the conversion back truncates toward zero rather than rounding, because the original
    /// runs the whole binary with the x87 rounding mode forced to chop (re-note §3.1).
    /// </para>
    /// </remarks>
    public long DueMilliseconds
    {
        get
        {
            int origin = unchecked(TimeBaseMilliseconds + ExcludedMilliseconds);
            return IgiMath.Trunc((1000.0 / TickRate * TickCount) + origin);
        }
    }

    /// <summary>Gets how far through the current tick period a clock reading sits, from 0 to 1.</summary>
    /// <param name="nowMilliseconds">The wall-clock reading for this frame.</param>
    /// <returns>0 just after a tick ran, approaching 1 as the next falls due; 1 once it is overdue.</returns>
    /// <remarks>
    /// <para>
    /// For the presentation only, never the simulation: a renderer drawing between two fixed-rate
    /// ticks uses this as the blend between the states they computed, which is what makes an uncapped
    /// frame rate look uncapped while the simulation stays at <see cref="TickRate"/> exactly.
    /// </para>
    /// <para>
    /// Measured between the previous and next per-tick <em>deadlines</em>, so it inherits everything
    /// the schedule already accounts for — the excluded-time stopwatch, the wrap, the absolute
    /// per-index targets. Overdue readings clamp to 1 rather than extrapolate: during a catch-up
    /// burst the honest picture is the newest completed tick, not a guess past it.
    /// </para>
    /// </remarks>
    public float TickFraction(int nowMilliseconds)
        => TickFraction((double)nowMilliseconds);

    /// <summary>Gets presentation progress through the current tick period from a precise clock.</summary>
    /// <param name="nowMilliseconds">The elapsed wall time, including any fractional millisecond.</param>
    /// <returns>0 just after a tick ran, approaching 1 as the next falls due; 1 once it is overdue.</returns>
    /// <remarks>
    /// Simulation scheduling remains integer and retail-compatible. Only rendering calls this overload:
    /// retaining the fraction prevents a coarse Windows tick counter from holding several high-refresh
    /// frames at the exact same interpolated position, which is perceived most strongly while strafing.
    /// </remarks>
    public float TickFraction(double nowMilliseconds)
    {
        if (TickCount == 0)
        {
            return 1f;
        }

        int origin = unchecked(TimeBaseMilliseconds + ExcludedMilliseconds);
        int previousDue = unchecked((int)IgiMath.Trunc((1000.0 / TickRate * (TickCount - 1)) + origin));
        int nextDue = unchecked((int)DueMilliseconds);

        // Signed differences so both sides wrap together, exactly as IsTickDue compares.
        int period = unchecked(nextDue - previousDue);
        if (period <= 0)
        {
            return 1f;
        }

        // The precise source retains the signed 32-bit counter's wrap while adding a fractional part.
        // Normalize the subtraction exactly as unchecked int arithmetic would on either side of it.
        double elapsed = nowMilliseconds - previousDue;
        const double counterRange = 4294967296.0;
        if (elapsed > int.MaxValue)
        {
            elapsed -= counterRange;
        }
        else if (elapsed < int.MinValue)
        {
            elapsed += counterRange;
        }
        return (float)Math.Clamp(elapsed / period, 0.0, 1.0);
    }

    /// <summary>Decides what the frame at a given clock reading may do.</summary>
    /// <param name="nowMilliseconds">The wall-clock reading for this frame.</param>
    /// <returns>The action the caller should perform.</returns>
    /// <remarks>
    /// Takes the reading rather than sampling it, because the original samples the clock exactly once per
    /// frame and every branch shares that one value.
    /// </remarks>
    public FrameAction Decide(int nowMilliseconds)
    {
        if (IsTickDue(nowMilliseconds))
        {
            return FrameAction.Tick;
        }

        return HasRendered && !UncappedRenders ? FrameAction.Idle : FrameAction.Render;
    }

    /// <summary>Determines whether a simulation tick may run at a given clock reading.</summary>
    /// <param name="nowMilliseconds">The wall-clock reading for this frame.</param>
    /// <returns><see langword="true"/> when all three conditions hold.</returns>
    /// <remarks>
    /// The deadline must have been <em>passed</em>, not merely reached; the catch-up budget must not be
    /// spent; and until <see cref="GuardedStartupTicks"/> ticks have run, a tick may only follow a render.
    /// The comparison is against the low 32 bits of the deadline, signed, so both sides wrap together.
    /// </remarks>
    public bool IsTickDue(int nowMilliseconds)
    {
        if (nowMilliseconds <= unchecked((int)DueMilliseconds))
        {
            return false;
        }

        if (CatchUpCount > MaxCatchUpTicks)
        {
            return false;
        }

        return HasRendered || TickCount >= GuardedStartupTicks;
    }

    /// <summary>
    /// Gets a value indicating whether the tick about to run is one of the opening ticks whose cost is
    /// excluded from the schedule.
    /// </summary>
    public bool IsStartupTick => TickCount < ExcludedStartupCount;

    /// <summary>
    /// Gets a value indicating whether the frame about to render is one of the opening frames whose cost
    /// is excluded from the schedule.
    /// </summary>
    public bool IsStartupFrame => FrameCount < ExcludedStartupCount;

    /// <summary>Records that a simulation tick has run.</summary>
    /// <remarks>Clears the render latch and spends one unit of the catch-up budget.</remarks>
    public void CompleteTick()
    {
        TickCount++;
        HasRendered = false;
        CatchUpCount++;
    }

    /// <summary>Records that a frame has been rendered.</summary>
    /// <remarks>Sets the render latch and refills the catch-up budget.</remarks>
    public void CompleteRender()
    {
        FrameCount++;
        HasRendered = true;
        CatchUpCount = 0;
    }

    /// <summary>Records a tick and a render for a frame that ran outside the schedule.</summary>
    /// <remarks>
    /// While paused, loading or playing a cutscene the original runs one update and one render per frame
    /// with no deadline check, advancing both counters but leaving the render latch and the catch-up
    /// budget exactly as they were — so the first scheduled frame afterwards resumes from the state the
    /// pause interrupted. See re-note §5.1.
    /// </remarks>
    public void CompleteUnscheduledFrame()
    {
        TickCount++;
        FrameCount++;
    }

    /// <summary>Restarts the schedule at a new simulation rate.</summary>
    /// <param name="tickRate">The new rate in hertz.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    /// <remarks>
    /// Rebases the clock and zeroes the tick and frame counters, so tick zero is "now". It deliberately
    /// leaves <see cref="CatchUpCount"/> and <see cref="TimeAdjustMilliseconds"/> alone, as
    /// <c>Time_SetTickRate</c> (<c>0x402820</c>) does — the boot path zeroes those once, separately.
    /// </remarks>
    public void SetTickRate(int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

        TimeBaseMilliseconds = _time.Milliseconds;
        ExcludedMilliseconds = 0;
        TickCount = 0;
        FrameCount = 0;
        TickRate = tickRate;
        HasRendered = true;
    }

    /// <summary>Resets the counters the boot path clears once, outside <see cref="SetTickRate"/>.</summary>
    public void ResetDrift()
    {
        CatchUpCount = 0;
        TimeAdjustMilliseconds = 0;
        ExcludedMilliseconds = 0;
    }

    /// <summary>Takes over another clock's whole schedule, counters and all.</summary>
    /// <param name="other">The clock whose schedule to continue.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// For handing the session from one <see cref="Flow.GameLoop"/> to another mid-run, which is what
    /// starting a mission from the menu's already-built backdrop does: the level is promoted rather than
    /// reloaded, and the loop that owns it takes over. A fresh loop builds its own clock starting at tick
    /// zero, and a schedule that restarts underneath a running session is not a cosmetic problem —
    /// <c>-lockstep</c> derives its synthetic milliseconds from <see cref="FrameCount"/>, so a reset makes
    /// wall-clock time jump backwards and byte-identical capture stops being possible.
    /// </para>
    /// <para>
    /// Every field that describes where the schedule <em>is</em> moves across, including
    /// <see cref="HasRendered"/> and <see cref="CatchUpCount"/> — the one-render-per-tick latch and the
    /// catch-up budget are mid-frame state, and dropping either would let the incoming loop render twice
    /// on one tick or spend a catch-up burst it had not earned. What deliberately does not move is
    /// <see cref="UncappedRenders"/>: that is a presentation setting the host owns and re-asserts every
    /// frame, not part of the schedule.
    /// </para>
    /// <para>
    /// The two clocks must share an <see cref="ITimeSource"/> for this to mean anything; the time base is
    /// a reading of that source, so adopting it across two different sources would carry over a number
    /// measured against a different origin.
    /// </para>
    /// </remarks>
    public void AdoptScheduleFrom(GameClock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        TickRate = other.TickRate;
        TickCount = other.TickCount;
        FrameCount = other.FrameCount;
        CatchUpCount = other.CatchUpCount;
        HasRendered = other.HasRendered;
        TimeBaseMilliseconds = other.TimeBaseMilliseconds;
        ExcludedMilliseconds = other.ExcludedMilliseconds;
        TimeAdjustMilliseconds = other.TimeAdjustMilliseconds;
    }

    /// <summary>Applies <see cref="TimeAdjustMilliseconds"/> to the time base, as the end of a frame does.</summary>
    public void AdvanceTimeBase() =>
        TimeBaseMilliseconds = unchecked(TimeBaseMilliseconds + TimeAdjustMilliseconds);

    /// <summary>Starts a span of time the schedule should not count.</summary>
    /// <remarks>
    /// <para>
    /// Level loading and the first two ticks and frames after a rate change run inside one of these.
    /// Without it a multi-second load would leave the sim ten ticks behind and trigger a catch-up burst
    /// the moment play resumes.
    /// </para>
    /// <para>
    /// Nesting is by refcount and only the outermost span is timed, exactly as
    /// <c>Time_ExcludeBegin</c>/<c>End</c> (<c>0x4028D0</c>/<c>0x4028F0</c>) do.
    /// </para>
    /// </remarks>
    public void BeginExcludedTime()
    {
        if (_exclusionDepth == 0)
        {
            _exclusionStart = _time.Milliseconds;
        }

        _exclusionDepth++;
    }

    /// <summary>Ends a span started by <see cref="BeginExcludedTime"/>.</summary>
    /// <exception cref="InvalidOperationException">No span is open.</exception>
    /// <remarks>
    /// The original underflows its refcount silently and would then add garbage to the schedule on the
    /// next close. Unbalanced calls are a programming error with no defined original behaviour to match,
    /// so they throw instead.
    /// </remarks>
    public void EndExcludedTime()
    {
        if (_exclusionDepth == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(EndExcludedTime)} was called without a matching {nameof(BeginExcludedTime)}.");
        }

        if (--_exclusionDepth == 0)
        {
            ExcludedMilliseconds = unchecked(ExcludedMilliseconds + (_time.Milliseconds - _exclusionStart));
            _exclusionStart = 0;
        }
    }

    /// <summary>Opens an excluded-time span that closes when the returned value is disposed.</summary>
    /// <returns>A scope that ends the span.</returns>
    public ExcludedTimeScope ExcludeTime()
    {
        BeginExcludedTime();
        return new ExcludedTimeScope(this);
    }
}
