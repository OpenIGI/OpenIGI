using System.Diagnostics;

namespace OpenIGI.Engine.Time;

/// <summary>
/// The real clock: milliseconds since this instance was created.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces <c>Sys_GetTickCount</c> (<c>0x490370</c>), which subtracts a start-up bias from
/// <c>GetTickCount</c> so that the engine only ever sees time relative to launch. The returned integer
/// keeps that signed 32-bit millisecond shape and wrap, while the backing monotonic clock has enough
/// resolution for the remastered presentation.
/// </para>
/// <para>
/// The original's counter has roughly 10–16 ms resolution and the tick schedule is built around that.
/// A higher-resolution clock is <em>allowed</em> — the schedule is defined by absolute deadlines rather
/// than by measured deltas, so finer readings change only which frame notices a deadline, never the
/// simulation — but the coarse case is the one the guards in <see cref="GameClock"/> were designed for.
/// </para>
/// </remarks>
public sealed class SystemTimeSource : ITimeSource
{
    private readonly long _preciseBias = Stopwatch.GetTimestamp();

    /// <inheritdoc/>
    /// <remarks>
    /// Truncating the monotonic high-resolution clock retains the scheduler's integer millisecond
    /// contract and signed wrap, but lets it notice a deadline on the next millisecond instead of the
    /// next 10–16 ms Windows timer quantum. The absolute deadline formula and 30 Hz tick count do not
    /// change.
    /// </remarks>
    public int Milliseconds => unchecked((int)Math.Truncate(PreciseMilliseconds));

    /// <inheritdoc/>
    /// <remarks>
    /// The simulation reads the truncated integer property; presentation reads this fractional value.
    /// Both therefore agree on when a tick period begins without quantising multiple renders together.
    /// </remarks>
    public double PreciseMilliseconds
    {
        get
        {
            double elapsed = (Stopwatch.GetTimestamp() - _preciseBias) * 1000.0 / Stopwatch.Frequency;
            const double counterRange = 4294967296.0;
            elapsed %= counterRange;
            return elapsed > int.MaxValue ? elapsed - counterRange : elapsed;
        }
    }
}
