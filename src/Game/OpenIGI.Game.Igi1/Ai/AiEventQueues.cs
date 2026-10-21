using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The twenty-four per-event-type queues an AI accumulates into, the priority order it drains them in, and
/// the per-type enable mask that gates them.
/// </summary>
/// <remarks>
/// <para>
/// The layout is fixed by three tables being contiguous inside <c>HumanAI</c>, the same argument that placed
/// the locomotion state table: the queues run <c>+1776</c>..<c>+2064</c> (24 × a 12-byte list head), the
/// priority order <c>+2076</c>..<c>+2172</c> (24 ints), the per-type drained counts
/// <c>+2172</c>..<c>+2268</c> (24 ints) with the order's length at <c>+2268</c>, and the enable mask
/// <c>+2276</c>..<c>+2300</c> (24 bytes) ending exactly where the pending-action list begins. Every one of
/// those spans is 24 wide, which is the event count.
/// </para>
/// <para>
/// <b>Three rules together are the whole arbitration</b> (<c>0x44FD50</c>):
/// </para>
/// <list type="number">
/// <item>
/// The queues are visited in <b>priority order</b>, not type order. The order holds only the types the script
/// declared, in the order it declared them: <c>AIFunction_SetEventPriority</c> (<c>0x44E0C0</c>) enables a type
/// and appends it the first time it is named, and a type nobody named is never admitted.
/// </item>
/// <item>
/// <b>At most one event type is dispatched per tick.</b> The first non-empty queue whose position in the
/// order is below the cap runs the script; everything after it is skipped for dispatch.
/// </item>
/// <item>
/// <b>Every queue is then drained regardless.</b> An event that was not dispatched is <em>discarded, not
/// deferred</em> — so a guard that is busy simply never learns about the shot it heard this tick.
/// </item>
/// </list>
/// <para>
/// The cap defaults to 24 (anything may dispatch) and otherwise comes from the <b>head pending action's</b>
/// priority at <c>+8</c>. So a running action states how interruptible it is by naming a position in the
/// order, and only events ranked strictly above that position can cut in.
/// </para>
/// </remarks>
public sealed class AiEventQueues
{
    /// <summary>How many event types there are, and the width of every table here.</summary>
    public const int TypeCount = 24;

    /// <summary>The cap used when no pending action names one — every position may dispatch.</summary>
    public const int UnboundedPriorityCap = TypeCount;

    private readonly List<AiEventRecord>[] _queues = new List<AiEventRecord>[TypeCount];
    private readonly int[] _drained = new int[TypeCount];
    private readonly bool[] _enabled = new bool[TypeCount];
    private readonly List<AiEvent> _order = [];

    /// <summary>Initializes a new instance of the <see cref="AiEventQueues"/> class.</summary>
    /// <remarks>
    /// Starts with an empty order and <b>every type disabled</b>, which is the constructor's state:
    /// <c>0x44F060</c> zeroes the mask and a script turns on what it wants to hear.
    /// </remarks>
    public AiEventQueues()
    {
        for (int i = 0; i < TypeCount; i++)
        {
            _queues[i] = [];
        }
    }

    /// <summary>Gets a value indicating whether the AI has been through its first tick.</summary>
    /// <remarks>
    /// <c>HumanAI+15636</c>. Before it is set, <b>only <see cref="AiEvent.Create"/> is admitted</b> — the
    /// enable mask is not consulted at all and any other type is dropped, which is what guarantees an AI's
    /// first event is its creation.
    /// </remarks>
    public bool IsInitialised { get; set; }

    /// <summary>Gets or sets a value indicating whether the AI is dead.</summary>
    /// <remarks><c>HumanAI+15034</c>, the death latch. A dead AI admits nothing at all.</remarks>
    public bool IsDead { get; set; }

    /// <summary>Gets the priority order the queues are visited in — the declared types, in declaration order.</summary>
    public IReadOnlyList<AiEvent> PriorityOrder => _order;

    /// <summary>Gets how many events of a type are queued.</summary>
    /// <param name="type">The event type.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered event.</exception>
    public int PendingCount(AiEvent type) => _queues[Index(type)].Count;

    /// <summary>Gets whether any queue holds a pending event.</summary>
    /// <remarks>
    /// The idle fallback consults this: <c>AIEVENT_IDLE</c> is posted <b>only when nothing is
    /// pending</b> — posting it unconditionally let a standing guard's Idle dispatch
    /// starve the gunshot it had just heard, since one type dispatches per tick and the rest are
    /// drained unheard.
    /// </remarks>
    public bool HasAnyPending
    {
        get
        {
            foreach (List<AiEventRecord> queue in _queues)
            {
                if (queue.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets a type's head record, or <see langword="null"/> when its queue is empty.</summary>
    /// <param name="type">The event type.</param>
    /// <remarks>
    /// What the action natives read mid-dispatch: <c>sub_44D850</c> copies the current event's position
    /// (record <c>+16</c>) out of the queue head for <c>LookAtEvent</c>/<c>MoveToEvent</c>.
    /// </remarks>
    public AiEventRecord? HeadOf(AiEvent type)
    {
        List<AiEventRecord> queue = _queues[Index(type)];
        return queue.Count > 0 ? queue[0] : null;
    }

    /// <summary>Gets how many events of a type were drained by the last <see cref="Pump"/>.</summary>
    /// <param name="type">The event type.</param>
    /// <returns>The count, which the engine keeps at <c>+2172 + 4 × type</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered event.</exception>
    public int DrainedCount(AiEvent type) => _drained[Index(type)];

    /// <summary>Turns an event type on or off for this AI.</summary>
    /// <param name="type">The event type.</param>
    /// <param name="enabled">Whether to admit it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered event.</exception>
    public void SetEnabled(AiEvent type, bool enabled) => _enabled[Index(type)] = enabled;

    /// <summary>Gets a value indicating whether an event type is admitted.</summary>
    /// <param name="type">The event type.</param>
    /// <returns>Whether it is enabled.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered event.</exception>
    public bool IsEnabled(AiEvent type) => _enabled[Index(type)];

    /// <summary>Declares an event type — <c>AIFunction_SetEventPriority</c> (<c>0x44E0C0</c>).</summary>
    /// <param name="type">The event type the script wants to hear.</param>
    /// <remarks>
    /// The first call for a type sets its enable byte and appends it to the order, so the order is the
    /// declaration order. Naming a type again changes nothing.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a registered event.</exception>
    public void Declare(AiEvent type)
    {
        int index = Index(type);
        if (_order.Contains(type))
        {
            return;
        }

        _enabled[index] = true;
        _order.Add(type);
    }

    /// <summary>Dispatches the AI's creation — what <c>0x44F410</c> does on its first tick.</summary>
    /// <param name="dispatch">Runs the AI script for an event type at a priority position.</param>
    /// <returns>Whether the script produced anything.</returns>
    /// <remarks>
    /// Creation does not go through the ordered pump — the order is empty until the creation branch declares
    /// it. The engine queues one CREATE record, runs the script for it directly with position <c>-1</c>
    /// (<c>sub_44FC90(ai, 0, -1)</c>), frees it, and marks the AI initialised.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public bool DispatchCreate(Func<AiEvent, int, bool> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        List<AiEventRecord> queue = _queues[Index(AiEvent.Create)];
        queue.Add(AiEventRecord.Simple(AiEvent.Create));
        bool handled = dispatch(AiEvent.Create, -1);
        queue.Clear();
        IsInitialised = true;
        return handled;
    }

    /// <summary>Offers an event to the AI, applying the admission rules that do not need the world.</summary>
    /// <param name="record">The event.</param>
    /// <param name="owner">The receiving AI's own task id, so an AI does not hear its own events.</param>
    /// <param name="worldTests">
    /// Resolves the tests that need the world — view, range, side and friend-or-foe. Called at most once, and
    /// only when <see cref="AiEventRecord.Filter"/> asks for something. Pass <see langword="null"/> to admit
    /// anything that gets past the unconditional gates.
    /// </param>
    /// <returns><see langword="true"/> when the event was queued.</returns>
    /// <remarks>
    /// The unconditional gates, in <c>0x450110</c>'s order: a dead AI takes nothing; <b>an AI never receives
    /// an event naming it as either the source or the subject</b>; before initialisation only
    /// <see cref="AiEvent.Create"/> passes; after it, the type must be enabled.
    /// </remarks>
    public bool Post(AiEventRecord record, int owner, Func<AiEventFilter, bool>? worldTests)
    {
        if (IsDead)
        {
            return false;
        }

        // 0x450110's self-filter: the owner being either end of the event drops it.
        if (owner != 0 && (owner == record.Subject || owner == record.Source))
        {
            return false;
        }

        if (!IsInitialised)
        {
            if (record.Type != AiEvent.Create)
            {
                return false;
            }
        }
        else if (!_enabled[Index(record.Type)])
        {
            return false;
        }

        if (record.Filter != AiEventFilter.None && worldTests is not null && !worldTests(record.Filter))
        {
            return false;
        }

        _queues[(int)record.Type].Add(record);
        Queued?.Invoke(record);
        return true;
    }

    /// <summary>Diagnostics: told of every record <see cref="Post"/> accepts, as retail's <c>0x450520</c> is reached.</summary>
    public Action<AiEventRecord>? Queued { get; set; }

    /// <summary>Runs one tick of the pump: dispatch at most one type, then drain everything.</summary>
    /// <param name="priorityCap">
    /// How far down the priority order an event may still interrupt. <see cref="UnboundedPriorityCap"/> when
    /// no action is pending, otherwise the head pending action's own priority.
    /// </param>
    /// <param name="dispatch">
    /// Runs the AI script for an event type, returning whether it produced an action. A type that produces
    /// nothing does <b>not</b> stop a later type from being tried.
    /// </param>
    /// <returns>The event type that was dispatched, or <see langword="null"/> when none was.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public AiEvent? Pump(int priorityCap, Func<AiEvent, int, bool> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        // 0x44FD50 walks the declared order once: the first non-empty queue under the cap whose handler
        // produces something is dispatched, and every queue is drained as the walk passes it. So an event
        // posted during the dispatch (SetCombat's COMBAT) survives into the next tick when its queue was
        // already passed, and is lost when its queue comes later.
        AiEvent? dispatched = null;
        for (int position = 0; position < _order.Count; position++)
        {
            AiEvent type = _order[position];
            int slot = (int)type;
            if (dispatched is null && position < priorityCap && _queues[slot].Count != 0 && dispatch(type, position))
            {
                dispatched = type;
            }

            // Drained whether or not it was dispatched — an undelivered event is lost, not deferred.
            _drained[slot] = _queues[slot].Count;
            _queues[slot].Clear();
        }

        return dispatched;
    }

    private static int Index(AiEvent type)
    {
        int index = (int)type;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(type));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TypeCount, nameof(type));
        return index;
    }
}
