using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// One simulation tick of the level as the original runs it: a single depth-first walk of the task tree in
/// authored order, each task running its own update -- which decides whether, and when, its children run.
/// </summary>
/// <remarks>
/// <para>
/// the original. The original has no grouped passes: <c>Flow_Update</c> walks the
/// root's children, and a type that wants its subtree to tick walks its own children from inside its update
/// (<c>Task_UpdateChildren</c>, <c>0x488700</c>), before, after or in the middle of its own work. So a task
/// reads this tick's state from everything before it in the tree and last tick's from everything after.
/// </para>
/// <para>
/// Subsystems register the update of the tasks they own; a task nobody registered behaves as a plain
/// container and walks its children -- except <c>Static</c> and <c>Idle</c>, whose update is a no-op in the
/// original, freezing everything beneath them, and <see cref="ConditionalContainerTask"/>, which runs its
/// own rules (<see cref="ConditionalContainerTask.Update"/>).
/// </para>
/// </remarks>
public sealed class LevelUpdateWalker
{
    private readonly Dictionary<GameTask, Action<GameTask>> _updates = [];
    private readonly Dictionary<GameTask, Action<GameTask>> _deactivations = [];

    /// <summary>Gets or sets what runs when a conditional container starts or stops, after its children ran.</summary>
    public Action<ConditionalContainerTask, bool>? ContainerTransition { get; set; }

    /// <summary>Gets or sets a container whose run state is held rather than re-evaluated this tick.</summary>
    public Func<ConditionalContainerTask, bool>? HoldContainer { get; set; }

    /// <summary>Registers a task's update. The update walks the task's children itself, through <see cref="WalkChildren"/>.</summary>
    /// <param name="task">The task.</param>
    /// <param name="update">Its update.</param>
    public void Register(GameTask task, Action<GameTask> update)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(update);
        _updates[task] = update;
    }

    /// <summary>Registers what a task does when its conditional container takes it out of the world.</summary>
    /// <param name="task">The task.</param>
    /// <param name="deactivate">Its deactivation.</param>
    public void RegisterDeactivation(GameTask task, Action<GameTask> deactivate)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(deactivate);
        _deactivations[task] = deactivate;
    }

    /// <summary>Runs one tick: the root's children, in order.</summary>
    /// <param name="root">The level root.</param>
    public void Walk(GameTask root)
    {
        ArgumentNullException.ThrowIfNull(root);
        WalkChildren(root);
    }

    /// <summary>Runs a task's children in order (<c>0x488700</c>): the successor is taken before each update.</summary>
    /// <param name="parent">The task whose children run.</param>
    public void WalkChildren(GameTask parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        GameTask? child = parent.FirstChild;
        while (child is not null)
        {
            GameTask? next = child.NextSibling;
            Update(child);
            child = next;
        }
    }

    /// <summary>Runs one task's update.</summary>
    /// <param name="task">The task.</param>
    public void Update(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_updates.TryGetValue(task, out Action<GameTask>? update))
        {
            update(task);
            return;
        }

        if (task is ConditionalContainerTask container)
        {
            container.Update(this);
            return;
        }

        if (task.Type.Name is "Static" or "Idle")
        {
            return;
        }

        WalkChildren(task);
    }

    /// <summary>Takes a subtree out of the world: every registered deactivation beneath a container, in order.</summary>
    /// <param name="parent">The container whose children are deactivated.</param>
    /// <remarks>An inner container passes it on only while it is active (<c>0x487B20</c>).</remarks>
    internal void Deactivate(GameTask parent)
    {
        for (GameTask? child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is ConditionalContainerTask inner)
            {
                if (inner.IsActive)
                {
                    inner.IsActive = false;
                    Deactivate(inner);
                }

                continue;
            }

            if (_deactivations.TryGetValue(child, out Action<GameTask>? deactivate))
            {
                deactivate(child);
            }

            Deactivate(child);
        }
    }

    /// <summary>Brings a subtree into the world; an inner container passes it on only if it runs and is not active (<c>0x487A70</c>).</summary>
    /// <param name="parent">The container whose children are activated.</param>
    internal static void Activate(GameTask parent)
    {
        for (GameTask? child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is ConditionalContainerTask inner)
            {
                if (inner.IsRun && !inner.IsActive)
                {
                    inner.IsActive = true;
                    Activate(inner);
                }

                continue;
            }

            Activate(child);
        }
    }
}
