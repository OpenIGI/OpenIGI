namespace OpenIGI.Game.Ai;

/// <summary>One entry of an AI's action stack.</summary>
/// <remarks>
/// The engine's record is a list node (<c>0x450E10</c>) whose <c>+8</c> is the priority arbitration reads,
/// <c>+12</c> a countdown, <c>+16</c> the flag word, <c>+20</c> the start function, <c>+24</c> the function
/// that runs it and <c>+32</c> the one that ends it.
/// </remarks>
public sealed class AiPendingAction
{
    /// <summary>Initializes a new instance of the <see cref="AiPendingAction"/> class.</summary>
    /// <param name="priority">The position in the event priority order below which events may interrupt it.</param>
    /// <param name="countdown">Ticks left before it becomes runnable.</param>
    /// <param name="run">What running it does, or <see langword="null"/> when it has nothing to run.</param>
    /// <param name="pushable">Whether a newer action suspends it rather than ending it.</param>
    /// <param name="name">The <c>AIAction_*</c> native that produced it.</param>
    public AiPendingAction(int priority, int countdown, Action? run, bool pushable = false, string name = "")
    {
        ArgumentNullException.ThrowIfNull(name);
        Priority = priority;
        Countdown = countdown;
        Run = run;
        Pushable = pushable;
        Name = name;
    }

    /// <summary>Gets the priority at <c>+8</c> — the cap arbitration uses when this is the head action.</summary>
    public int Priority { get; }

    /// <summary>Gets or sets the countdown at <c>+12</c>.</summary>
    public int Countdown { get; set; }

    /// <summary>Gets a value indicating whether bit 0 of the flag word at <c>+16</c> — <c>AIACTIONFLAG_PUSHABLE</c> — is set.</summary>
    public bool Pushable { get; }

    /// <summary>Gets the <c>AIAction_*</c> native that produced it.</summary>
    public string Name { get; }

    /// <summary>Gets what running the action does, <see langword="null"/> when <c>+24</c> is empty.</summary>
    public Action? Run { get; }
}

/// <summary>
/// The stack of actions an AI has outstanding, and the one rule that decides what happens to it each tick.
/// </summary>
/// <remarks>
/// <para>
/// New actions go on at the <b>head</b> (<c>0x4AF910</c>). Before they do, <c>0x450EE0</c> ends every action
/// above the first <em>pushable</em> one; that one and everything beneath it stay, suspended, and carry on
/// once what was pushed over them has finished. A pushable combat survives a flashbang's flinch; a
/// non-pushable patrol does not survive combat.
/// </para>
/// <para>
/// Each tick <c>0x44FEC0</c> walks it from the head, and it is stranger than it looks:
/// </para>
/// <list type="bullet">
/// <item>
/// entries <b>without</b> a run function are skipped, but a skipped entry with a non-zero countdown gets it
/// decremented;
/// </item>
/// <item>
/// <b>only one countdown decrements per tick</b>, across the whole list — a latch is set by the first entry
/// that has one, and every later entry is passed over even if it is also counting down. So a queue of three
/// delays takes their sum, not their maximum;
/// </item>
/// <item>
/// the walk stops at the <b>first entry that does have a run function</b>, and runs exactly that one. Nothing
/// after it is looked at.
/// </item>
/// </list>
/// </remarks>
public sealed class AiPendingActionList
{
    private readonly List<AiPendingAction> _actions = [];

    /// <summary>Gets the actions, head first.</summary>
    public IReadOnlyList<AiPendingAction> Actions => _actions;

    /// <summary>Gets a value indicating whether anything is pending.</summary>
    /// <remarks>
    /// This is <c>0x450DC0</c>, and the pump uses it for two things: to pick the priority cap, and to decide
    /// whether to post <see cref="AiEvent.Idle"/> — <b>an AI is only idle when its list is empty</b>.
    /// </remarks>
    public bool HasPending => _actions.Count != 0;

    /// <summary>Gets the priority cap arbitration should use this tick.</summary>
    /// <remarks>
    /// The head action's priority, or <see cref="AiEventQueues.UnboundedPriorityCap"/> when nothing is
    /// pending. Note it is the <b>head</b>, not the entry that will actually run.
    /// </remarks>
    public int PriorityCap =>
        _actions.Count == 0 ? AiEventQueues.UnboundedPriorityCap : _actions[0].Priority;

    /// <summary>Puts an action at the head — <c>0x4AF910</c>, as <c>0x450E70</c> and <c>0x450F10</c> do.</summary>
    /// <param name="action">The action.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public void Push(AiPendingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Insert(0, action);
    }

    /// <summary>Appends an action at the tail — <c>0x4AF8F0</c>.</summary>
    /// <param name="action">The action.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public void Add(AiPendingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    /// <summary>Ends every action above the first pushable one — <c>0x450EE0</c>.</summary>
    /// <param name="ended">Called for each action removed, head first, to run its end function.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ended"/> is <see langword="null"/>.</exception>
    public void PopToPushable(Action<AiPendingAction> ended)
    {
        ArgumentNullException.ThrowIfNull(ended);
        while (_actions.Count != 0 && !_actions[0].Pushable)
        {
            AiPendingAction head = _actions[0];
            _actions.RemoveAt(0);
            ended(head);
        }
    }

    /// <summary>Removes every action.</summary>
    public void Clear() => _actions.Clear();

    /// <summary>Removes one exact action node.</summary>
    /// <param name="action">The node.</param>
    /// <returns><see langword="true"/> when the node was pending.</returns>
    public bool Remove(AiPendingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _actions.Remove(action);
    }

    /// <summary>Runs one tick of the list.</summary>
    /// <returns>The action that ran, or <see langword="null"/> when only delays were ticked.</returns>
    public AiPendingAction? Tick()
    {
        bool countedDown = false;

        foreach (AiPendingAction action in _actions)
        {
            if (action.Run is not null)
            {
                action.Run();
                return action;
            }

            if (action.Countdown != 0 && !countedDown)
            {
                countedDown = true;
                action.Countdown--;
            }
        }

        return null;
    }
}
