using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>An event a soldier has heard and not yet handled, <c>HumanAIEvent_t</c> (88 bytes).</summary>
public sealed class Igi2AiEventRecord
{
    /// <summary>Gets or sets the event, an <c>AIEVENT_*</c> value.</summary>
    public int Event { get; set; }

    /// <summary>Gets or sets the team it concerns.</summary>
    public int Team { get; set; }

    /// <summary>Gets or sets where it happened; the origin when it happened nowhere.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its direction as heard: the shot's, its reverse, or from the soldier toward it.</summary>
    public Vector3f Direction { get; set; }

    /// <summary>Gets or sets what it is about, <c>ptReferenceQTaskRef</c>.</summary>
    public object? Reference { get; set; }

    /// <summary>Gets or sets who raised it, <c>ptHumanOriginatorQTaskRef</c>.</summary>
    public object? Originator { get; set; }

    /// <summary>Gets or sets what only this kind of event carries, <c>uEventSpecificData</c>.</summary>
    public object? Specific { get; set; }
}

/// <summary>
/// A soldier's heard events, waiting by kind, and the order its script ranks the kinds in (<c>HumanAI_s</c>: <c>atEvents</c>,
/// <c>atEventTypeStacks</c>, <c>aisEventPrioritized</c>, <c>anEventPriorityList</c>).
/// </summary>
/// <remarks>
/// <para>
/// Thirty-two records are kept; an event heard with all of them waiting is lost. Each kind waits in its own queue, oldest first.
/// The script ranks kinds by declaring them (<c>AIFunction_SetEventPriority</c>): the first declared is the most important, and
/// only declared kinds are heard once the soldier has started.
/// </para>
/// </remarks>
public sealed class Igi2AiEventList
{
    /// <summary>How many kinds of event there are, <c>AIEVENT_*</c> 0 to 46.</summary>
    public const int EventTypes = 47;

    /// <summary>How many events a soldier keeps waiting.</summary>
    public const int Capacity = 32;

    private readonly Queue<Igi2AiEventRecord>[] _byType = new Queue<Igi2AiEventRecord>[EventTypes];
    private readonly bool[] _prioritized = new bool[EventTypes];
    private readonly int[] _priorityList = new int[EventTypes];
    private readonly int[] _countLastTick = new int[EventTypes];

    /// <summary>Initializes a new instance of the <see cref="Igi2AiEventList"/> class.</summary>
    public Igi2AiEventList()
    {
        for (int i = 0; i < EventTypes; i++)
        {
            _byType[i] = new Queue<Igi2AiEventRecord>();
        }
    }

    /// <summary>Gets how many records are in use, <c>nEventsUsed</c>.</summary>
    public int Used { get; private set; }

    /// <summary>Gets how many kinds the script has ranked, <c>nEventPriorities</c>.</summary>
    public int PriorityCount { get; private set; }

    /// <summary>Gets the kind at a rank.</summary>
    /// <param name="rank">The rank, 0 being the most important.</param>
    /// <returns>The kind.</returns>
    public int KindAt(int rank) => _priorityList[rank];

    /// <summary>Gets whether a kind is ranked.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> when the script declared it.</returns>
    public bool IsPrioritized(int kind) => (uint)kind < EventTypes && _prioritized[kind];

    /// <summary>Gets how many events of a kind were let go at the last dispatch, <c>anEventCountLastTick</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The count.</returns>
    public int CountLastTick(int kind) => _countLastTick[kind];

    /// <summary>Gets the events of a kind waiting, oldest first.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The queue.</returns>
    public IReadOnlyCollection<Igi2AiEventRecord> Of(int kind) => _byType[kind];

    /// <summary>The oldest event of a kind, or <see langword="null"/>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The event.</returns>
    public Igi2AiEventRecord? Head(int kind) => _byType[kind].Count != 0 ? _byType[kind].Peek() : null;

    /// <summary>Ranks a kind after those already ranked, once (<c>AIFunction_SetEventPriority</c>).</summary>
    /// <param name="kind">The kind.</param>
    public void Prioritize(int kind)
    {
        if ((uint)kind >= EventTypes || _prioritized[kind])
        {
            return;
        }

        _prioritized[kind] = true;
        _priorityList[PriorityCount++] = kind;
    }

    /// <summary>Forgets every rank, as the script is loaded again (<c>HumanAI_UpdateInternalDataHandler</c>).</summary>
    public void ClearPriorities() => PriorityCount = 0;

    /// <summary>The rank of a kind, or -1 when unranked (<c>HumanAI_GetEventPriority</c>).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The rank.</returns>
    public int RankOf(int kind)
    {
        for (int i = 0; i < PriorityCount; i++)
        {
            if (_priorityList[i] == kind)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Takes a record for a new event, or <see langword="null"/> when all are waiting (<c>HumanAI_CreateEvent</c>).</summary>
    /// <returns>The record, not yet queued.</returns>
    public Igi2AiEventRecord? Create()
    {
        if (Used >= Capacity)
        {
            return null;
        }

        Used++;
        return new Igi2AiEventRecord();
    }

    /// <summary>Queues an event behind those of its kind.</summary>
    /// <param name="record">The event, from <see cref="Create"/>.</param>
    public void Add(Igi2AiEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _byType[record.Event].Enqueue(record);
    }

    /// <summary>Lets every waiting event of a kind go, counting them (<c>HumanAI_RunScript</c>'s flush).</summary>
    /// <param name="kind">The kind.</param>
    public void Flush(int kind)
    {
        _countLastTick[kind] = _byType[kind].Count;
        Used -= _byType[kind].Count;
        _byType[kind].Clear();
    }

    /// <summary>Lets every waiting event of a kind go, <c>HumanAI_RemoveEventsOfType</c>.</summary>
    /// <param name="kind">The kind.</param>
    public void RemoveKind(int kind)
    {
        if ((uint)kind < EventTypes)
        {
            Used -= _byType[kind].Count;
            _byType[kind].Clear();
        }
    }

    /// <summary>Lets every waiting event go (<c>HumanAI_RemoveEvents</c>).</summary>
    public void Clear()
    {
        foreach (Queue<Igi2AiEventRecord> queue in _byType)
        {
            Used -= queue.Count;
            queue.Clear();
        }
    }

    /// <summary>Pushes an event straight onto its kind's queue without taking a record, as a dummy the script runs on.</summary>
    /// <param name="record">The event.</param>
    internal void PushTransient(Igi2AiEventRecord record) => _byType[record.Event].Enqueue(record);

    /// <summary>Takes back an event pushed with <see cref="PushTransient"/>, which is the last of its kind.</summary>
    /// <param name="record">The event.</param>
    internal void PopTransient(Igi2AiEventRecord record)
    {
        Queue<Igi2AiEventRecord> queue = _byType[record.Event];
        Igi2AiEventRecord[] kept = [.. queue.Where(e => !ReferenceEquals(e, record))];
        queue.Clear();
        foreach (Igi2AiEventRecord e in kept)
        {
            queue.Enqueue(e);
        }
    }
}
