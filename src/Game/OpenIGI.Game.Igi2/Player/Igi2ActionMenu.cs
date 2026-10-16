namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The status screen's action menu, <c>actionmenuscreen.c</c>'s <c>ActionMenuScreen</c>: up to six things the player could do now
/// — the object it would activate, a weapon it could take in exchange for one it carries — in an order the zoom keys rotate, of
/// which only the first is done.
/// </summary>
/// <remarks>
/// <para>
/// Each tick what is on offer registers again (<see cref="Register"/>); <see cref="Update"/> then keeps the last tick's order for
/// what is still offered, adds what is new at the end and forgets what was not offered. The order a tick reads is the one the
/// tick before worked out, so a thing newly offered is first only on the second tick after it was.
/// </para>
/// <para>
/// While an activation runs, the menu is hidden for ten ticks (<see cref="SetNoDrawTimeout"/>), and nothing is first.
/// </para>
/// </remarks>
public sealed class Igi2ActionMenu
{
    /// <summary>The things the menu holds at once, six.</summary>
    public const int Capacity = 6;

    private readonly Slot[] _slots = [new(), new(), new(), new(), new(), new()];
    private readonly int[] _order = new int[Capacity];
    private readonly int[] _newOrder = new int[Capacity];
    private int _count;
    private int _newCount;
    private int _scroll;

    /// <summary>Gets the ticks the menu stays hidden, <c>nNoDrawTimeout</c>.</summary>
    public int NoDrawTimeout { get; private set; }

    /// <summary>Gets or sets the flow's tick count, which stamps what is registered.</summary>
    public Func<int> FlowTicks { get; set; } = static () => 0;

    /// <summary>Gets or sets whether a thing still exists, as its reference's object does; a gone thing frees its slot.</summary>
    public Func<object, bool> IsAlive { get; set; } = static _ => true;

    /// <summary>Gets the things on the menu in the order the player sees them, with what each would be done with.</summary>
    public IEnumerable<(object Item, object? Context)> Items
    {
        get
        {
            for (int i = 0; i < _count; i++)
            {
                Slot slot = _slots[_order[i]];
                if (slot.Item is { } item)
                {
                    yield return (item, slot.Context);
                }
            }
        }
    }

    /// <summary>
    /// Offers a thing for this tick, <c>ActionMenuScreen_RegisterActionItem</c>: one offered already with the same context is marked
    /// offered again; a new one takes the first slot that holds nothing living, or is dropped when all six do.
    /// </summary>
    /// <param name="item">What would be done.</param>
    /// <param name="context">What it would be done with (a weapon to give up, for a weapon taken), or <see langword="null"/>.</param>
    public void Register(object item, object? context)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (Slot slot in _slots)
        {
            if (slot.IsUsed && ReferenceEquals(slot.Item, item) && ReferenceEquals(slot.Context, context))
            {
                slot.IsOffered = true;
                return;
            }
        }

        for (int i = 0; i < Capacity; i++)
        {
            Slot slot = _slots[i];
            if (slot.IsUsed && slot.Item is { } held && IsAlive(held))
            {
                continue;
            }

            slot.IsUsed = true;
            slot.IsOffered = true;
            slot.Item = item;
            slot.Context = context;
            slot.Tick = FlowTicks();
            return;
        }
    }

    /// <summary>Whether something offered this tick answers a test, as a caller that walks the slots asks.</summary>
    /// <param name="match">The test, given a thing and its context.</param>
    /// <returns>Whether one does.</returns>
    public bool IsOffered(Func<object, object?, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        foreach (Slot slot in _slots)
        {
            if (slot.IsUsed && slot.IsOffered && slot.Item is { } item && match(item, slot.Context))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The menu's tick, <c>ActionMenuScreen_Update</c>: the order worked out last tick becomes the one shown, a scroll asked for
    /// rotates it, and this tick's offers make the next: those already in order keep their places, new ones follow; a slot not
    /// offered is freed. The hidden ticks count down.
    /// </summary>
    public void Update()
    {
        Array.Copy(_newOrder, _order, _newCount);
        _count = _newCount;
        _newCount = 0;
        if (_scroll != 0)
        {
            Scroll(_scroll);
        }

        _scroll = 0;
        for (int i = 0; i < _count; i++)
        {
            Slot slot = _slots[_order[i]];
            if (slot.IsOffered)
            {
                _newOrder[_newCount++] = _order[i];
                slot.IsListed = true;
            }
        }

        for (int i = 0; i < Capacity; i++)
        {
            Slot slot = _slots[i];
            if (slot.IsOffered)
            {
                if (!slot.IsListed)
                {
                    _newOrder[_newCount++] = i;
                    slot.IsListed = true;
                }
            }
            else
            {
                slot.IsUsed = false;
            }
        }

        foreach (Slot slot in _slots)
        {
            slot.IsOffered = false;
            slot.IsListed = false;
        }

        NoDrawTimeout = NoDrawTimeout - 1 < 0 ? 0 : NoDrawTimeout - 1;
    }

    /// <summary>
    /// Whether a thing, with a context, is the one the menu would do, <c>ActionMenuScreen_IsFirstActionItem</c>: the menu holds
    /// something, is not hidden, and that thing is first.
    /// </summary>
    /// <param name="item">The thing.</param>
    /// <param name="context">Its context.</param>
    /// <returns>Whether it is first.</returns>
    public bool IsFirst(object item, object? context)
    {
        if (_count == 0 || NoDrawTimeout != 0)
        {
            return false;
        }

        Slot first = _slots[_order[0]];
        return ReferenceEquals(first.Item, item) && ReferenceEquals(first.Context, context);
    }

    /// <summary>Asks the next update to rotate the order, <c>ActionMenuScreen_Scroll</c>: one forward, minus one back.</summary>
    /// <param name="direction">The direction.</param>
    public void RequestScroll(int direction) => _scroll = direction;

    /// <summary>Hides the menu for some ticks, <c>ActionMenuScreen_SetNoDrawTimeout</c>.</summary>
    /// <param name="ticks">The ticks.</param>
    public void SetNoDrawTimeout(int ticks) => NoDrawTimeout = ticks;

    /// <summary>
    /// Forgets the order and what each slot held, <c>ActionMenuScreen_ClearHistory</c>, as a load or the player's going does; a slot's
    /// in-use mark is left, but it holds nothing living and is free.
    /// </summary>
    public void ClearHistory()
    {
        _newCount = 0;
        _count = 0;
        _scroll = 0;
        NoDrawTimeout = 0;
        foreach (Slot slot in _slots)
        {
            slot.Item = null;
            slot.Context = null;
        }
    }

    // ActionMenuScreen_DoScroll: one forward moves the first to the end; one back moves the last to the front.
    private void Scroll(int direction)
    {
        if (_count == 0)
        {
            return;
        }

        if (direction > 0)
        {
            int first = _order[0];
            Array.Copy(_order, 1, _order, 0, _count - 1);
            _order[_count - 1] = first;
        }
        else
        {
            int last = _order[_count - 1];
            Array.Copy(_order, 0, _order, 1, _count - 1);
            _order[0] = last;
        }
    }

    private sealed class Slot
    {
        public object? Item { get; set; }

        public object? Context { get; set; }

        public bool IsUsed { get; set; }

        public bool IsOffered { get; set; }

        public bool IsListed { get; set; }

        public int Tick { get; set; }
    }
}
