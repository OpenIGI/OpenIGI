namespace OpenIGI.Engine.Time;

/// <summary>
/// The engine's only wall-clock source: a millisecond counter measured from start-up.
/// </summary>
/// <remarks>
/// <para>
/// The original reads time in exactly one place, <c>Sys_GetTickCount</c> (<c>0x490370</c>), which is
/// <c>GetTickCount() - bias</c> — a biased 32-bit millisecond counter with roughly 10–16 ms resolution.
/// The single <c>QueryPerformanceCounter</c> call in the binary profiles texture releases and never
/// reaches the frame schedule.
/// </para>
/// <para>
/// The value is a signed 32-bit integer because that is how the frame arbiter compares it: the tick
/// deadline is truncated to 64 bits and only its low dword is compared, signed. Wrap-around behaviour
/// therefore has to match, so widening this to 64 bits would be a behaviour change, not a cleanup.
/// </para>
/// </remarks>
public interface ITimeSource
{
    /// <summary>Gets the milliseconds elapsed since the source was created.</summary>
    int Milliseconds { get; }
}
