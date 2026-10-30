namespace OpenIGI.Game.Multiplayer;

/// <summary>The recent snapshots one end holds, by tick: the server's sent ones for a client, a client's received ones.</summary>
/// <remarks>
/// On the server it is a client's: each snapshot sent is kept until it is too old, and the newest one the client has
/// acknowledged is the baseline the next is written against. On a client it is what came in, which a snapshot names as its
/// baseline. Either way a tick is held for <see cref="Capacity"/> snapshots and then forgotten.
/// </remarks>
public sealed class SnapshotHistory
{
    private readonly Snapshot?[] _ring;
    private Snapshot? _acknowledged;

    /// <summary>Initializes a new instance of the <see cref="SnapshotHistory"/> class.</summary>
    /// <param name="capacity">How many snapshots it holds.</param>
    public SnapshotHistory(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new Snapshot?[capacity];
    }

    /// <summary>Gets how many snapshots it holds.</summary>
    public int Capacity => _ring.Length;

    /// <summary>Gets the newest snapshot kept, or <see langword="null"/>.</summary>
    public Snapshot? Newest { get; private set; }

    /// <summary>Gets the newest acknowledged snapshot still held: the baseline to write the next against.</summary>
    public Snapshot? Baseline => _acknowledged is { } acked && Get(acked.Tick) is not null ? acked : null;

    /// <summary>Keeps a snapshot.</summary>
    /// <param name="snapshot">The snapshot.</param>
    public void Add(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ring[Slot(snapshot.Tick)] = snapshot;
        if (Newest is null || snapshot.Tick > Newest.Tick)
        {
            Newest = snapshot;
        }
    }

    /// <summary>Gets the snapshot of a tick, if still held.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The snapshot, or <see langword="null"/>.</returns>
    public Snapshot? Get(long tick) => _ring[Slot(tick)] is { } held && held.Tick == tick ? held : null;

    /// <summary>Notes that the other end holds a snapshot, so it may be the baseline.</summary>
    /// <param name="tick">The tick it acknowledged.</param>
    public void Acknowledge(long tick)
    {
        if (Get(tick) is { } snapshot && (_acknowledged is null || tick > _acknowledged.Tick))
        {
            _acknowledged = snapshot;
        }
    }

    /// <summary>Forgets everything, as a map changes.</summary>
    public void Clear()
    {
        Array.Clear(_ring);
        _acknowledged = null;
        Newest = null;
    }

    private int Slot(long tick) => (int)(((tick % _ring.Length) + _ring.Length) % _ring.Length);
}
