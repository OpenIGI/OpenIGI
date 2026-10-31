using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>Where each human stood at any moment of the last stretch of snapshots, between the ticks they were taken on.</summary>
/// <remarks>
/// <para>
/// A client draws everyone but itself a little in the past, at <see cref="Net.TickClockSync.InterpolationTick"/>, so that
/// there is nearly always a snapshot either side of the moment drawn: the body moves smoothly however the snapshots
/// arrive. Position, velocity and view blend between the two; what the body is doing and holding is the earlier one's.
/// Past the newest snapshot it carries the body on along its velocity, for at most <see cref="MaxExtrapolationTicks"/>,
/// and then holds it where it got to.
/// </para>
/// <para>
/// The server keeps one too, over its own snapshots, and that is its lag compensation: a shot is traced against the humans
/// where this puts them at the moment the shooter's client drew them (its command's tick less its interpolation delay),
/// which is by construction where the shooter saw them. Source rewinds the same way, to the same moment.
/// </para>
/// </remarks>
public sealed class SnapshotInterpolator
{
    private readonly List<Snapshot> _snapshots = [];

    /// <summary>Initializes a new instance of the <see cref="SnapshotInterpolator"/> class.</summary>
    /// <param name="capacity">How many snapshots it keeps: a second's worth or so.</param>
    public SnapshotInterpolator(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        Capacity = capacity;
    }

    /// <summary>Gets how many snapshots it keeps.</summary>
    public int Capacity { get; }

    /// <summary>Gets or sets how many ticks past the newest snapshot a body is carried on (<c>cl_extrapolate_amount</c>).</summary>
    public double MaxExtrapolationTicks { get; set; } = 6;

    /// <summary>Gets the oldest tick it can still answer for, or <see cref="long.MaxValue"/> when empty.</summary>
    public long OldestTick => _snapshots.Count > 0 ? _snapshots[0].Tick : long.MaxValue;

    /// <summary>Gets the newest tick it holds, or <see cref="long.MinValue"/> when empty.</summary>
    public long NewestTick => _snapshots.Count > 0 ? _snapshots[^1].Tick : long.MinValue;

    /// <summary>Takes a snapshot, in whatever order they arrive; a tick already held is ignored.</summary>
    /// <param name="snapshot">The snapshot.</param>
    public void Add(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        int index = _snapshots.FindIndex(s => s.Tick >= snapshot.Tick);
        if (index >= 0 && _snapshots[index].Tick == snapshot.Tick)
        {
            return;
        }

        _snapshots.Insert(index < 0 ? _snapshots.Count : index, snapshot);
        while (_snapshots.Count > Capacity)
        {
            _snapshots.RemoveAt(0);
        }
    }

    /// <summary>Gets a human as it was at a moment.</summary>
    /// <param name="tick">The moment, a fractional tick.</param>
    /// <param name="id">The human.</param>
    /// <param name="human">Where it stood and what it was doing then.</param>
    /// <returns><see langword="false"/> when no snapshot held it at or before that moment.</returns>
    public bool Sample(double tick, int id, out ReplicatedHuman human)
    {
        human = default;
        int after = _snapshots.FindIndex(s => s.Tick > tick);
        int before = (after < 0 ? _snapshots.Count : after) - 1;

        // Nothing at or before the moment that holds it: look no further back than the oldest snapshot.
        while (before >= 0 && !_snapshots[before].TryGet(id, out _))
        {
            before--;
        }

        if (before < 0)
        {
            return false;
        }

        Snapshot from = _snapshots[before];
        from.TryGet(id, out ReplicatedHuman a);
        int next = after < 0 ? -1 : _snapshots.FindIndex(after, s => s.TryGet(id, out _));
        if (next < 0)
        {
            double ahead = Math.Min(tick - from.Tick, MaxExtrapolationTicks);
            human = a with { Position = Int3.From(a.Position.ToVector() + (ToDouble(a.Velocity) * ahead)) };
            return true;
        }

        Snapshot to = _snapshots[next];
        to.TryGet(id, out ReplicatedHuman b);
        double t = (tick - from.Tick) / (to.Tick - from.Tick);
        human = a with
        {
            Position = Int3.From(Vector3d.Lerp(a.Position.ToVector(), b.Position.ToVector(), t)),
            Velocity = new Vector3f(
                (float)(a.Velocity.X + ((b.Velocity.X - a.Velocity.X) * t)),
                (float)(a.Velocity.Y + ((b.Velocity.Y - a.Velocity.Y) * t)),
                (float)(a.Velocity.Z + ((b.Velocity.Z - a.Velocity.Z) * t))),
            Yaw = LerpAngle(a.Yaw, b.Yaw, t),
            Pitch = LerpAngle(a.Pitch, b.Pitch, t),
        };
        return true;
    }

    /// <summary>Forgets everything, as a map changes.</summary>
    public void Clear() => _snapshots.Clear();

    private static Vector3d ToDouble(Vector3f v) => new(v.X, v.Y, v.Z);

    // The short way round: a heading crossing from 359 degrees to 1 turns two degrees, not 358.
    private static ushort LerpAngle(ushort a, ushort b, double t) => unchecked((ushort)(a + (int)Math.Round((short)(b - a) * t)));
}
