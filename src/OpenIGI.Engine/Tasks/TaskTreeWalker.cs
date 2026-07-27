namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Broadcasts a message depth-first through a task tree, keeping an explicit cursor stack so that a task
/// may delete itself, a sibling or a whole subtree from inside its own handler.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism that makes the original's object model work at all: update handlers routinely
/// delete things, and a naive <c>foreach</c> over a child collection either throws or walks freed memory.
/// The original keeps one global stack (<c>0xAFA6E0</c>, 64 slots) alongside a depth counter
/// (<c>0xAFA7E0</c>) and has <c>Task_Delete</c> reach into it; the walk is <c>0x401D80</c>.
/// </para>
/// <para>
/// Each stack slot serves two purposes in turn. On entry at depth <em>d</em> the walker writes the task
/// it is about to message into slot <em>d</em>; while iterating that task's children it writes the
/// <em>next child still to visit</em> into slot <em>d+1</em>. <see cref="NotifyDeleted"/> rewrites
/// whichever slot names a dying task, which is what keeps both roles correct:
/// </para>
/// <list type="bullet">
/// <item>
/// A task that deletes <strong>itself</strong> has its own entry slot overwritten, so the equality check
/// after its handler fails and the walker skips recursing into its now-destroyed children.
/// </item>
/// <item>
/// A task that deletes a <strong>later sibling</strong> has the parent's pending cursor advanced past it,
/// so the loop resumes at the right place instead of following a dead link.
/// </item>
/// </list>
/// <para>
/// A task that deletes an <em>earlier</em> sibling or one of its own children needs no patch: the walk
/// has already passed the former, and unlinking removes the latter before it is reached.
/// </para>
/// </remarks>
public sealed class TaskTreeWalker
{
    /// <summary>The recursion depth the original's fixed 64-slot cursor stack allows.</summary>
    /// <remarks>
    /// Not enforced. The original would corrupt adjacent globals past this depth; retail data nests
    /// nowhere near it. Kept as documentation of where the original's cliff is.
    /// </remarks>
    public const int OriginalMaxDepth = 64;

    private readonly List<GameTask?> _cursors = [];

    /// <summary>Gets the current recursion depth; zero when no walk is in progress.</summary>
    public int Depth { get; private set; }

    /// <summary>Gets a value indicating whether a walk is currently in progress.</summary>
    public bool IsWalking => Depth > 0;

    /// <summary>Sends a message to a task and, depth-first, to every task beneath it.</summary>
    /// <param name="task">The subtree root to message.</param>
    /// <param name="message">The message to deliver.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public void Send(GameTask task, TaskMessage message)
    {
        ArgumentNullException.ThrowIfNull(task);
        Dispatch(task, message);
    }

    /// <summary>Patches any pending cursor that names a task about to be deleted.</summary>
    /// <param name="task">The task being deleted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Must be called <em>before</em> the task is unlinked, because the replacement cursor is the task's
    /// own next sibling. Only the first matching slot is patched, exactly as <c>0x4015F0</c> does.
    /// </remarks>
    public void NotifyDeleted(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        for (int i = 0; i < Depth; i++)
        {
            if (ReferenceEquals(_cursors[i], task))
            {
                _cursors[i] = task.NextSibling;
                return;
            }
        }
    }

    private void Dispatch(GameTask task, TaskMessage message)
    {
        int entry = Depth;
        SetCursor(entry, task);
        Depth = entry + 1;

        task.Receive(message);

        // Re-read rather than reuse: a handler may have run nested walks of its own.
        int childSlot = Depth;

        if (ReferenceEquals(task, _cursors[childSlot - 1]))
        {
            GameTask? child = task.FirstChild;
            while (child is not null)
            {
                // Publish the successor before recursing, so a handler deleting it can patch this slot.
                SetCursor(childSlot, child.NextSibling);
                Depth = childSlot + 1;

                Dispatch(child, message);

                childSlot = Depth - 1;
                Depth = childSlot;
                child = _cursors[childSlot];
            }
        }

        Depth = childSlot - 1;
    }

    private void SetCursor(int slot, GameTask? task)
    {
        while (_cursors.Count <= slot)
        {
            _cursors.Add(null);
        }

        _cursors[slot] = task;
    }
}
