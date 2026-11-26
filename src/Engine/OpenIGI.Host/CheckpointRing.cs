namespace OpenIGI.Host;

/// <summary>
/// Level checkpoints kept along a run, by the tick each was taken at: the latest at or before a moment found to go back to, those
/// from a moment on let go when what follows has changed, and every other one let go past a memory budget (the first is kept).
/// </summary>
/// <param name="budgetBytes">What the checkpoints may hold of their own before every other one is let go.</param>
/// <remarks>What the cutscene preview and the play-test's replay go back with.</remarks>
internal sealed class CheckpointRing(long budgetBytes)
{
    private readonly SortedList<int, LevelCheckpoint> _held = [];

    /// <summary>Gets how many are held.</summary>
    public int Count => _held.Count;

    /// <summary>Gets the one taken last along the run, or null.</summary>
    public LevelCheckpoint? Last => _held.Count > 0 ? _held.Values[^1] : null;

    /// <summary>Gets whether one is held for a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns><see langword="true"/> when one is.</returns>
    public bool Has(int tick) => _held.ContainsKey(tick);

    /// <summary>Keeps a checkpoint; every other one is let go when they hold more than the budget.</summary>
    /// <param name="checkpoint">The checkpoint, keyed by its tick.</param>
    public void Add(LevelCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _held[checkpoint.Tick] = checkpoint;
        if (_held.Values.Sum(static c => c.State.OwnBytes) > budgetBytes)
        {
            // A seek back then plays on twice as far at most.
            for (int i = _held.Count - 2; i > 0; i -= 2)
            {
                _held.RemoveAt(i);
            }
        }
    }

    /// <summary>Finds the latest checkpoint at or before a tick, or strictly before it.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="before">Whether one taken at the tick itself does not count.</param>
    /// <returns>The checkpoint, or null.</returns>
    public LevelCheckpoint? AtOrBefore(int tick, bool before = false)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (before ? _held.Keys[i] < tick : _held.Keys[i] <= tick)
            {
                return _held.Values[i];
            }
        }

        return null;
    }

    /// <summary>Lets go of every checkpoint taken at or after a tick.</summary>
    /// <param name="tick">The tick.</param>
    public void DropFrom(int tick)
    {
        for (int i = _held.Count - 1; i >= 0 && _held.Keys[i] >= tick; i--)
        {
            _held.RemoveAt(i);
        }
    }

    /// <summary>Lets go of them all.</summary>
    public void Clear() => _held.Clear();
}
