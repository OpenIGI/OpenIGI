namespace OpenIGI.Engine.Time;

/// <summary>
/// A clock the caller advances by hand, for deterministic replay, headless tools and tests.
/// </summary>
/// <remarks>
/// The frame schedule is defined entirely by what <see cref="ITimeSource.Milliseconds"/> returns, so
/// driving it from a script makes a whole run reproducible — including the cases that are hard to
/// provoke on a real clock, such as a stall long enough to hit the catch-up cap.
/// </remarks>
public sealed class ManualTimeSource : ITimeSource
{
    /// <summary>Initializes a new instance of the <see cref="ManualTimeSource"/> class.</summary>
    /// <param name="milliseconds">The initial reading.</param>
    public ManualTimeSource(int milliseconds = 0) => Milliseconds = milliseconds;

    /// <summary>Gets or sets the current reading.</summary>
    public int Milliseconds { get; set; }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="milliseconds">How far to advance. May be zero; negative values are rejected.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="milliseconds"/> is negative.</exception>
    /// <remarks>Wraps on overflow, matching the 32-bit counter the original reads.</remarks>
    public void Advance(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        Milliseconds = unchecked(Milliseconds + milliseconds);
    }
}
