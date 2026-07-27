namespace OpenIGI.Engine.Time;

/// <summary>
/// The real clock: milliseconds since this instance was created.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces <c>Sys_GetTickCount</c> (<c>0x490370</c>), which subtracts a start-up bias from
/// <c>GetTickCount</c> so that the engine only ever sees time relative to launch.
/// <see cref="Environment.TickCount"/> is the same underlying counter on Windows and the closest
/// equivalent elsewhere.
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
    private readonly int _bias = Environment.TickCount;

    /// <inheritdoc/>
    /// <remarks>Unchecked subtraction, so the value wraps exactly as the original's does.</remarks>
    public int Milliseconds => unchecked(Environment.TickCount - _bias);
}
