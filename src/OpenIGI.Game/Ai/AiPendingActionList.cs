namespace OpenIGI.Game.Ai;

/// <summary>One entry of an AI's pending-action list.</summary>
/// <remarks>
/// The engine's record is a list node whose <c>+8</c> is the priority arbitration reads, <c>+12</c> a
/// countdown and <c>+24</c> the function that runs it.
/// </remarks>
public sealed class AiPendingAction
{
    /// <summary>Initializes a new instance of the <see cref="AiPendingAction"/> class.</summary>
    /// <param name="priority">The position in the event priority order below which events may interrupt it.</param>
    /// <param name="countdown">Ticks left before it becomes runnable.</param>
    /// <param name="run">What running it does, or <see langword="null"/> when it has nothing to run.</param>
    public AiPendingAction(int priority, int countdown, Action? run)
    {
        Priority = priority;
        Countdown = countdown;
        Run = run;
    }

    /// <summary>Gets the priority at <c>+8</c> — the cap arbitration uses when this is the head action.</summary>
    public int Priority { get; }

    /// <summary>Gets or sets the countdown at <c>+12</c>.</summary>
    public int Countdown { get; set; }

    /// <summary>Gets what running the action does, <see langword="null"/> when <c>+24</c> is empty.</summary>
    public Action? Run { get; }
}

/// <summary>
/// The list of actions an AI has outstanding, and the one rule that decides what happens to it each tick.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x44FEC0</c>, and it is stranger than it looks. The list is walked from the head, and:
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
/// <para>
/// So an entry with no run function is a <em>delay</em>, and an AI's pending list is a little program of
/// waits followed by one thing to do.
/// </para>
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

    /// <summary>Appends an action.</summary>
    /// <param name="action">The action.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public void Add(AiPendingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions.Add(action);
    }

    /// <summary>Removes every action.</summary>
    public void Clear() => _actions.Clear();

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
