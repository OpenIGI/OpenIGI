namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Sends task events: to one task, through a subtree, or through a subtree to the tasks of one type.
/// </summary>
/// <remarks>
/// <para>
/// The original's four ways of triggering an event, the same in both games: the handler of one task called through the
/// table; <c>Task_Broadcast</c> (<c>0x401D80</c>, <c>QTask_TriggerQTaskEventRecursively</c>); <c>Task_BroadcastChildren</c>
/// (<c>0x401E30</c>, <c>QTask_TriggerQTaskEventChildrenRecursively</c>); and <c>Task_BroadcastType</c> (<c>0x401E70</c>,
/// <c>QTask_TriggerQTaskEventRecursivelySpecificQTaskType</c>).
/// </para>
/// <para>
/// A task whose type links no handler for the event is passed over, and the walk goes on beneath it: an event is not stopped
/// by a task that does not take it. Every walk is the tree's cursor-safe one, so a handler may delete what it likes.
/// </para>
/// </remarks>
public static class TaskEvents
{
    /// <summary>Sends an event to one task.</summary>
    /// <typeparam name="TEvent">The event.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="context">The event's context.</param>
    /// <returns>Whether the task's type handles the event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static bool Trigger<TEvent>(this GameTask task, ref TEvent context)
        where TEvent : struct, ITaskEvent
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task is ITaskEventTarget<TEvent> handler)
        {
            handler.Handle(ref context);
            return true;
        }

        return false;
    }

    /// <summary>Sends an event to a task and, depth-first, to every task beneath it.</summary>
    /// <typeparam name="TEvent">The event.</typeparam>
    /// <param name="task">The subtree's root.</param>
    /// <param name="context">The event's context, the one every handler is given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static void TriggerRecursively<TEvent>(this GameTask task, ref TEvent context)
        where TEvent : struct, ITaskEvent
    {
        ArgumentNullException.ThrowIfNull(task);
        EventVisitor<TEvent> visitor = new(ref context);
        task.Tree.Walker.Visit(task, ref visitor);
    }

    /// <summary>Sends an event to everything beneath a task, but not to the task.</summary>
    /// <typeparam name="TEvent">The event.</typeparam>
    /// <param name="task">The task whose subtrees are sent it.</param>
    /// <param name="context">The event's context, the one every handler is given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public static void TriggerDescendants<TEvent>(this GameTask task, ref TEvent context)
        where TEvent : struct, ITaskEvent
    {
        ArgumentNullException.ThrowIfNull(task);
        EventVisitor<TEvent> visitor = new(ref context);
        task.Tree.Walker.VisitDescendants(task, ref visitor);
    }

    /// <summary>Sends an event through a subtree to the tasks of one type, and of that type exactly.</summary>
    /// <typeparam name="TEvent">The event.</typeparam>
    /// <param name="task">The subtree's root.</param>
    /// <param name="type">The type whose tasks are sent it; a task of a type derived from it is not.</param>
    /// <param name="context">The event's context, the one every handler is given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> or <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <remarks>The original compares the task's type with the one asked for and does not climb to its parents.</remarks>
    public static void TriggerRecursively<TEvent>(this GameTask task, TaskType type, ref TEvent context)
        where TEvent : struct, ITaskEvent
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(type);
        TypedEventVisitor<TEvent> visitor = new(type, ref context);
        task.Tree.Walker.Visit(task, ref visitor);
    }

    private ref struct EventVisitor<TEvent> : ITaskVisitor
        where TEvent : struct, ITaskEvent
    {
        private readonly ref TEvent _context;

        public EventVisitor(ref TEvent context) => _context = ref context;

        public readonly void Visit(GameTask task)
        {
            if (task is ITaskEventTarget<TEvent> handler)
            {
                handler.Handle(ref _context);
            }
        }
    }

    private ref struct TypedEventVisitor<TEvent> : ITaskVisitor
        where TEvent : struct, ITaskEvent
    {
        private readonly TaskType _type;
        private readonly ref TEvent _context;

        public TypedEventVisitor(TaskType type, ref TEvent context)
        {
            _type = type;
            _context = ref context;
        }

        public readonly void Visit(GameTask task)
        {
            if (task.HasType && ReferenceEquals(task.Type, _type) && task is ITaskEventTarget<TEvent> handler)
            {
                handler.Handle(ref _context);
            }
        }
    }
}
