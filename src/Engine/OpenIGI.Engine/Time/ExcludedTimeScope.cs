namespace OpenIGI.Engine.Time;

/// <summary>
/// Closes an excluded-time span opened by <see cref="GameClock.ExcludeTime"/>.
/// </summary>
/// <remarks>
/// The original pairs <c>Time_ExcludeBegin</c> and <c>Time_ExcludeEnd</c> by hand across long load
/// sequences; a scope makes the pairing hard to get wrong. Disposing twice is a no-op, so an accidental
/// double dispose cannot corrupt the schedule.
/// </remarks>
public struct ExcludedTimeScope : IDisposable
{
    private GameClock? _clock;

    internal ExcludedTimeScope(GameClock clock) => _clock = clock;

    /// <summary>Ends the span.</summary>
    public void Dispose()
    {
        GameClock? clock = _clock;
        _clock = null;
        clock?.EndExcludedTime();
    }
}
