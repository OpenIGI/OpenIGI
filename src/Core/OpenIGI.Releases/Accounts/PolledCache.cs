namespace OpenIGI.Releases.Accounts;

/// <summary>
/// What a page reads from the master, kept by what it was asked for: one request at a time, taken in on the frame it finishes,
/// asked again once it is older than <see cref="Fresh"/>, and a failed one not asked again until as long has passed -- what was
/// read before shown on meanwhile.
/// </summary>
/// <typeparam name="T">What is read.</typeparam>
/// <param name="fresh">How long a reading stays fresh, and a failure stands.</param>
/// <param name="now">The clock.</param>
public sealed class PolledCache<T>(TimeSpan fresh, Func<DateTimeOffset> now)
    where T : class
{
    private readonly Dictionary<string, (T Value, DateTimeOffset At)> _kept = [];
    private readonly Dictionary<string, DateTimeOffset> _failed = [];
    private (string Key, Task<T> Task)? _asked;

    /// <summary>Gets how long a reading stays fresh.</summary>
    public TimeSpan Fresh => fresh;

    /// <summary>What was read for a key, or <see langword="null"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The reading.</returns>
    public T? Get(string key) => _kept.TryGetValue(key, out (T Value, DateTimeOffset At) kept) ? kept.Value : null;

    /// <summary>How a key's reading stands: read, being asked for, or failed.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its state.</returns>
    public FeedState State(string key) =>
        _kept.ContainsKey(key) ? FeedState.Ready : _asked?.Key == key ? FeedState.Loading : _failed.ContainsKey(key) ? FeedState.Failed : FeedState.Loading;

    /// <summary>Takes in a finished request. Whether one was finished.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Collect()
    {
        if (_asked is not { Task.IsCompleted: true } done)
        {
            return false;
        }

        _asked = null;
        if (done.Task.IsCompletedSuccessfully)
        {
            _kept[done.Key] = (done.Task.Result, now());
            _failed.Remove(done.Key);
        }
        else if (_kept.TryGetValue(done.Key, out (T Value, DateTimeOffset At) before))
        {
            _kept[done.Key] = (before.Value, now());
        }
        else
        {
            _failed[done.Key] = now();
        }

        return true;
    }

    /// <summary>Asks for a key's reading where it is missing or old, nothing else is being asked for, and it has not just failed.</summary>
    /// <param name="key">The key.</param>
    /// <param name="read">Asks for it.</param>
    /// <returns>Whether it was asked for.</returns>
    public bool Ask(string key, Func<Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        DateTimeOffset at = now();
        bool old = !_kept.TryGetValue(key, out (T Value, DateTimeOffset At) kept) || at - kept.At > fresh;
        bool failing = _failed.TryGetValue(key, out DateTimeOffset failed) && at - failed <= fresh;
        if (!old || _asked is not null || failing)
        {
            return false;
        }

        _asked = (key, read());
        return true;
    }

    /// <summary>Forgets a key's reading and its failure, so the next asks for it at once.</summary>
    /// <param name="key">The key.</param>
    public void Forget(string key)
    {
        _kept.Remove(key);
        _failed.Remove(key);
    }

    /// <summary>Forgets every reading, so the next asks afresh.</summary>
    public void Clear()
    {
        _kept.Clear();
        _failed.Clear();
    }
}
