namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>What an action does at a moment of its life: given the soldier and the action itself.</summary>
/// <param name="ai">The soldier's mind.</param>
/// <param name="item">The action.</param>
public delegate void Igi2AiActionCallback(Igi2HumanAi ai, Igi2AiActionItem item);

/// <summary>A sub-behaviour an action runs inside itself, returning <see langword="true"/> when it is done.</summary>
/// <param name="ai">The soldier's mind.</param>
/// <param name="item">The action.</param>
/// <returns><see langword="true"/> when done.</returns>
public delegate bool Igi2AiChildCallback(Igi2HumanAi ai, Igi2AiActionItem item);

/// <summary>
/// One action on a soldier's stack, <c>HumanAIActionStackItem_s</c> (56 bytes): its callbacks, which an action may swap as it
/// goes, its rank and its state.
/// </summary>
public sealed class Igi2AiActionItem
{
    /// <summary>Gets or sets the squad order that made it, <c>eSquadOrder</c>.</summary>
    public int SquadOrder { get; set; }

    /// <summary>Gets or sets its rank: the rank of the event that made it, lower more important (<c>nPriority</c>).</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets the ticks before it runs, <c>iDelay</c>.</summary>
    public int Delay { get; set; }

    /// <summary>Gets or sets its flags, <c>bFlags</c>: bit 1 is <c>AIACTIONFLAG_PUSHABLE</c>.</summary>
    public int Flags { get; set; }

    /// <summary>Gets or sets what it does as it is pushed, <c>pfInitCallback</c>.</summary>
    public Igi2AiActionCallback? Init { get; set; }

    /// <summary>Gets or sets what it does each tick it runs, <c>pfRunCallback</c>.</summary>
    public Igi2AiActionCallback? Run { get; set; }

    /// <summary>Gets or sets what its Init leaves to run first, <c>pfRunInitCallback</c> (written, never read).</summary>
    public Igi2AiActionCallback? RunInit { get; set; }

    /// <summary>Gets or sets what it does as it is popped, <c>pfDeleteCallback</c>.</summary>
    public Igi2AiActionCallback? Delete { get; set; }

    /// <summary>Gets or sets what it does when another is pushed over it while pushable, <c>pfPushedFromTopCallback</c>.</summary>
    public Igi2AiActionCallback? PushedFromTop { get; set; }

    /// <summary>Gets or sets the sub-behaviour it runs, <c>pfChildRunCallback</c>, or <see langword="null"/>.</summary>
    public Igi2AiChildCallback? ChildRun { get; set; }

    /// <summary>Gets or sets its state, <c>ptContext</c>: an action-specific object, or <see langword="null"/>.</summary>
    public object? Context { get; set; }

    /// <summary>Gets the state as its action's own type.</summary>
    /// <typeparam name="T">The state's type.</typeparam>
    /// <returns>The state.</returns>
    public T ContextAs<T>()
        where T : class => (T)Context!;
}

/// <summary>
/// A soldier's actions, the top one running (<c>HumanAI_s.tActionStack</c> over <c>atActionStackItemList[32]</c> and 32 contexts).
/// </summary>
/// <remarks>More than 32 actions at once is fatal, as in the original (<c>"ActionStack full"</c>).</remarks>
public sealed class Igi2AiActionList
{
    /// <summary>The most actions a soldier holds.</summary>
    public const int Capacity = 32;

    /// <summary>The most action states a soldier holds; an action made with none left has no state.</summary>
    public const int ContextCapacity = 32;

    private readonly LinkedList<Igi2AiActionItem> _items = new();
    private int _taken;
    private int _contexts;

    /// <summary>Gets the actions, top first.</summary>
    public IEnumerable<Igi2AiActionItem> Items => _items;

    /// <summary>Gets the top action, or <see langword="null"/> (<c>HumanAI_GetTopActionStackItem</c>).</summary>
    public Igi2AiActionItem? Top => _items.First?.Value;

    /// <summary>Gets a value indicating whether nothing is on the stack.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Takes a fresh action, <c>HumanAI_GetFreeActionStackItem</c>.</summary>
    /// <returns>The action.</returns>
    /// <exception cref="OpenIGI.Core.Diagnostics.EngineFatalException">All 32 are in use.</exception>
    public Igi2AiActionItem Take()
    {
        if (_taken >= Capacity)
        {
            throw new OpenIGI.Core.Diagnostics.EngineFatalException("ActionStack full");
        }

        _taken++;
        return new Igi2AiActionItem();
    }

    /// <summary>Takes a state slot for an action, <c>HumanAI_CreateActionContext</c>.</summary>
    /// <returns><see langword="true"/> when one was free.</returns>
    public bool TakeContext()
    {
        if (_contexts >= ContextCapacity)
        {
            return false;
        }

        _contexts++;
        return true;
    }

    /// <summary>Gives a state slot back unused, <c>HumanAI_RemoveActionContext</c>.</summary>
    public void ReleaseContext() => _contexts--;

    /// <summary>Gives back an action that was taken but never pushed, freeing its state.</summary>
    /// <param name="item">The action.</param>
    public void Discard(Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _taken--;
        if (item.Context is not null)
        {
            _contexts--;
            item.Context = null;
        }
    }

    /// <summary>Puts an action on top.</summary>
    /// <param name="item">The action.</param>
    public void AddTop(Igi2AiActionItem item) => _items.AddFirst(item);

    /// <summary>Pops an action wherever it is: its Delete, then it and its state go (<c>HumanAI_PopActionStackItem</c>).</summary>
    /// <param name="ai">The soldier's mind.</param>
    /// <param name="item">The action.</param>
    public void Pop(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Delete?.Invoke(ai, item);
        Remove(item);
    }

    /// <summary>Takes an action off the stack and frees it and its state, without its Delete.</summary>
    /// <param name="item">The action.</param>
    public void Remove(Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_items.Remove(item))
        {
            _taken--;
        }

        if (item.Context is not null)
        {
            _contexts--;
            item.Context = null;
        }
    }

    /// <summary>Pops from the top until a pushable action, each with its Delete (<c>HumanAI_PopTopActionStackItems</c>).</summary>
    /// <param name="ai">The soldier's mind.</param>
    public void PopUntilPushable(Igi2HumanAi ai)
    {
        while (Top is { } top && (top.Flags & 1) == 0)
        {
            Pop(ai, top);
        }
    }

    /// <summary>Pops everything, each with its Delete (<c>HumanAI_ClearActionStack</c>).</summary>
    /// <param name="ai">The soldier's mind.</param>
    public void Clear(Igi2HumanAi ai)
    {
        while (Top is { } top)
        {
            Pop(ai, top);
        }
    }

    /// <summary>Drops everything without Deletes, as a deleted soldier lets its stack go (<c>HumanAI_DeleteHandler</c>).</summary>
    public void Abandon()
    {
        while (Top is { } top)
        {
            Remove(top);
        }
    }
}
