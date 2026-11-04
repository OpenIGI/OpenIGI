using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>
/// One human a detection record is offered this tick, as it describes itself to the sweep (message <c>0x489620</c>).
/// </summary>
/// <param name="Key">Who it is; a slot that finds another human at its index starts over.</param>
/// <param name="Mark">The point the cone and the sight ray test, in engine units.</param>
/// <param name="Origin">Its object origin (<c>Human+32</c>), which a gun aims at, in engine units.</param>
/// <param name="Team">Its side.</param>
/// <param name="Alive">Whether it is alive.</param>
/// <param name="Task">Its task, which the sight ray does not collide with, or <see langword="null"/>.</param>
public readonly record struct AiDetectable(
    object Key,
    Vector3d Mark,
    Vector3d Origin,
    int Team,
    bool Alive,
    GameTask? Task = null);

/// <summary>
/// A detection record that keeps every human it tracks, sorted into enemies and friends in the order each was
/// registered: <c>0x4896C0</c> (init), <c>0x489750</c> (sweep) and <c>0x4898D0</c> (per-slot test).
/// </summary>
/// <remarks>
/// <para>
/// The sweep runs one tick in four on the record's phase. Each offered human at index <c>i</c> owns slot <c>i</c>: one the
/// record's flags take -- alive or dead, of its own team or not -- is linked onto the enemies or friends list (at the
/// tail, so the lists keep registration order); one they do not is unlinked. A slot that finds a different human at its
/// index is unlinked and linked again. A slot that is already linked stays on the list it joined.
/// </para>
/// <para>
/// Slots past the end of this tick's offer are left as they are, as the sweep only walks the offer.
/// </para>
/// </remarks>
public sealed class AiDetectionRecord
{
    /// <summary>Flag bit: live humans of the record's own team are tracked as friends.</summary>
    public const int LiveFriends = 1;

    /// <summary>Flag bit: dead humans of the record's own team are tracked as friends.</summary>
    public const int DeadFriends = 2;

    /// <summary>Flag bit: live humans of another team are tracked as enemies.</summary>
    public const int LiveEnemies = 4;

    /// <summary>Flag bit: dead humans of another team are tracked as enemies.</summary>
    public const int DeadEnemies = 8;

    private readonly int _flags;
    private readonly int _coneInterval;
    private readonly int _rayInterval;
    private readonly List<Entry> _slots = [];
    private readonly LinkedList<Entry> _enemies = new();
    private readonly LinkedList<Entry> _friends = new();
    private AiDetectionSchedule _schedule;

    /// <summary>Initializes a new instance of the <see cref="AiDetectionRecord"/> class, as <c>0x4896C0</c> does.</summary>
    /// <param name="flags">Which humans it tracks: <see cref="LiveFriends"/>, <see cref="LiveEnemies"/> and so on.</param>
    /// <param name="phase">Which of the four ticks it sweeps on, the global counter's next value.</param>
    /// <param name="coneTicks">The cone test's interval in ticks (divided by four into sweeps).</param>
    /// <param name="rayTicks">The sight ray's interval in ticks (divided by four into sweeps).</param>
    public AiDetectionRecord(int flags, int phase, int coneTicks, int rayTicks)
    {
        _flags = flags;
        _coneInterval = coneTicks / AiDetectionSchedule.PhaseCount;
        _rayInterval = rayTicks / AiDetectionSchedule.PhaseCount;
        _schedule = new AiDetectionSchedule(phase & 3, _coneInterval, _rayInterval);
    }

    /// <summary>Gets which of the four ticks the record sweeps on.</summary>
    public int Phase => _schedule.Phase;

    /// <summary>Gets the enemies' keys in list order, with whether each is marked seen.</summary>
    public IEnumerable<(object Key, bool Seen)> Enemies => _enemies.Select(static entry => (entry.Key!, entry.Slot.IsVisible));

    /// <summary>Forgets everything and starts again on a new phase: <c>0x489B90</c>, then <c>0x4896C0</c>.</summary>
    /// <param name="phase">The global counter's next value.</param>
    public void Reset(int phase)
    {
        foreach (Entry entry in _slots)
        {
            Unlink(entry);
        }

        _schedule = new AiDetectionSchedule(phase & 3, _coneInterval, _rayInterval);
    }

    /// <summary>One sweep, when the record's phase comes up (<c>0x489750</c>).</summary>
    /// <param name="tick">The flow tick the phase is compared against.</param>
    /// <param name="eye">Where the record looks from and through which cone.</param>
    /// <param name="team">The record owner's side.</param>
    /// <param name="offered">Every human, in the global detection list's order.</param>
    /// <param name="lineOfSight">The sight ray from the eye to a human's mark (<c>0x489B20</c>).</param>
    public void Update(
        int tick,
        in AiEyeSnapshot eye,
        int team,
        IReadOnlyList<AiDetectable> offered,
        Func<AiDetectable, bool> lineOfSight)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(lineOfSight);
        if (!_schedule.SweepsOn(tick))
        {
            return;
        }

        AiEyeSnapshot snapshot = eye;
        for (int index = 0; index < offered.Count; index++)
        {
            while (_slots.Count <= index)
            {
                _slots.Add(new Entry());
            }

            Entry entry = _slots[index];
            AiDetectable human = offered[index];
            LinkedList<Entry>? list = ListFor(human, team);
            if (list is null)
            {
                Unlink(entry);
                continue;
            }

            if (entry.Node is null)
            {
                Link(entry, list, human.Key, tick);
            }
            else if (!ReferenceEquals(entry.Key, human.Key))
            {
                Unlink(entry);
                Link(entry, list, human.Key, tick);
            }

            Vector3d mark = human.Mark;
            _schedule.Sweep(
                entry.Slot,
                () => snapshot.IsVisible(mark),
                () => lineOfSight(human),
                (mark - snapshot.Position).Length);
        }
    }

    /// <summary>The first enemy, in list order, whose slot is marked seen and who is still offered.</summary>
    /// <param name="offered">This tick's humans, which give the enemy's live position.</param>
    /// <returns>The enemy as it is offered now, or <see langword="null"/> when none is seen.</returns>
    /// <remarks>
    /// An <c>AIStationaryGun</c>'s run (<c>0x46F390</c>) walks its owner's enemy list for the first slot whose seen byte
    /// (<c>+41</c>) is set and whose task still exists, and aims at that task's position as it is now.
    /// </remarks>
    public AiDetectable? FirstSeenEnemy(IReadOnlyList<AiDetectable> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        foreach (Entry entry in _enemies)
        {
            if (!entry.Slot.IsVisible)
            {
                continue;
            }

            for (int index = 0; index < offered.Count; index++)
            {
                if (ReferenceEquals(offered[index].Key, entry.Key))
                {
                    return offered[index];
                }
            }
        }

        return null;
    }

    // 0x489750: same team takes the friend bits, another team the enemy bits; alive and dead each have their own.
    private LinkedList<Entry>? ListFor(in AiDetectable human, int team)
    {
        if (human.Team == team)
        {
            return (_flags & (human.Alive ? LiveFriends : DeadFriends)) != 0 ? _friends : null;
        }

        return (_flags & (human.Alive ? LiveEnemies : DeadEnemies)) != 0 ? _enemies : null;
    }

    // 0x489500: the slot starts afresh at the list's tail.
    private static void Link(Entry entry, LinkedList<Entry> list, object key, int tick)
    {
        entry.Key = key;
        entry.Slot.Register(tick);
        entry.Node = list.AddLast(entry);
    }

    // 0x4894E0: off its list, its handle released.
    private static void Unlink(Entry entry)
    {
        if (entry.Node is not { } node)
        {
            return;
        }

        node.List?.Remove(node);
        entry.Node = null;
        entry.Key = null;
        entry.Slot.Unregister();
    }

    private sealed class Entry
    {
        public AiDetectionSlot Slot { get; } = new();

        public object? Key { get; set; }

        public LinkedListNode<Entry>? Node { get; set; }
    }
}
