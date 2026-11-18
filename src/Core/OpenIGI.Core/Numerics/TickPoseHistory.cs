namespace OpenIGI.Core.Numerics;

/// <summary>A thing's pose as one frame draws it, between its last two ticks.</summary>
/// <param name="Position">Where it is drawn.</param>
/// <param name="Orientation">Which way it is drawn turned.</param>
/// <param name="Serial">A number no other thing the history holds has, for as long as it holds this one.</param>
public readonly record struct TickPose(Vector3d Position, Matrix3f Orientation, long Serial);

/// <summary>
/// Where each of a set of things stood at its last two ticks, for drawing it between them: a thing the simulation moves once a
/// tick, drawn at its tick pose alone, steps at the tick rate while the camera glides between ticks.
/// </summary>
/// <typeparam name="TKey">What names a thing from tick to tick.</typeparam>
/// <remarks>
/// A thing seen at consecutive ticks is drawn between the two; one whose last pose was not the tick before -- new, back in view
/// after a while, or asked after across a skipped tick -- is drawn where it is, with nothing to come from.
/// </remarks>
public sealed class TickPoseHistory<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries;
    private long _nextSerial;

    /// <summary>Initializes a new instance of the <see cref="TickPoseHistory{TKey}"/> class.</summary>
    /// <param name="comparer">How keys are told apart, or <see langword="null"/> for their own equality.</param>
    /// <param name="firstSerial">The serial the first thing held is given; each after takes the next.</param>
    public TickPoseHistory(IEqualityComparer<TKey>? comparer = null, long firstSerial = 1)
    {
        _entries = new Dictionary<TKey, Entry>(comparer);
        _nextSerial = firstSerial;
    }

    /// <summary>Gets how many things the history holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Notes a thing's pose at a tick and gives where the frame draws it.</summary>
    /// <param name="key">The thing.</param>
    /// <param name="tick">The tick the pose is for.</param>
    /// <param name="position">Where it stands at that tick.</param>
    /// <param name="orientation">Which way it faces at that tick.</param>
    /// <param name="blend">How far the frame is from the tick before to this one, 0 to 1.</param>
    /// <param name="seen">When it was asked after, on whatever clock <see cref="Forget"/> is given.</param>
    /// <returns>The drawn pose.</returns>
    public TickPose Present(TKey key, long tick, Vector3d position, Matrix3f orientation, float blend, long seen)
    {
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry(_nextSerial++);
            _entries[key] = entry;
        }

        if (entry.Tick != tick)
        {
            bool next = entry.Tick == tick - 1;
            entry.From = next ? entry.To : position;
            entry.FromOrientation = next ? entry.ToOrientation : orientation;
            entry.Tick = tick;
        }

        // Asked again within its tick, the tick's latest pose is where it goes, from where it was.
        entry.To = position;
        entry.ToOrientation = orientation;
        entry.Seen = seen;

        if (blend >= 1f || (entry.From == entry.To && entry.FromOrientation == entry.ToOrientation))
        {
            return new TickPose(entry.To, entry.ToOrientation, entry.Serial);
        }

        float t = Math.Clamp(blend, 0f, 1f);
        return new TickPose(
            Vector3d.Lerp(entry.From, entry.To, t),
            Interpolation.InterpolateRotation(entry.FromOrientation, entry.ToOrientation, t),
            entry.Serial);
    }

    /// <summary>Forgets every thing not asked after since a given time, so the history holds only what is still drawn.</summary>
    /// <param name="before">The time before which a thing last seen is forgotten.</param>
    public void Forget(long before)
    {
        List<TKey>? stale = null;
        foreach (KeyValuePair<TKey, Entry> pair in _entries)
        {
            if (pair.Value.Seen < before)
            {
                (stale ??= []).Add(pair.Key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (TKey key in stale)
        {
            _entries.Remove(key);
        }
    }

    /// <summary>Forgets everything.</summary>
    public void Clear() => _entries.Clear();

    private sealed class Entry(long serial)
    {
        public long Serial { get; } = serial;

        public long Tick { get; set; } = long.MinValue;

        public Vector3d From { get; set; }

        public Vector3d To { get; set; }

        public Matrix3f FromOrientation { get; set; }

        public Matrix3f ToOrientation { get; set; }

        public long Seen { get; set; }
    }
}
