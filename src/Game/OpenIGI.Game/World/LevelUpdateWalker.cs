using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

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
/// It is I.G.I.'s <see cref="LevelRun"/>. Subsystems bind the update of the tasks they own; a task nobody bound behaves as a plain
/// container and walks its children -- except <c>Static</c> and <c>Idle</c>, whose update is a no-op in the
/// original, freezing everything beneath them. A <see cref="ConditionalContainerTask"/> runs the shared handler under the
/// first game's rules (<see cref="IConditionalContainerRules"/>). Unlike I.G.I. 2's run, the walk does not pass over a task
/// already marked deleted.
/// </para>
/// </remarks>
public sealed class LevelUpdateWalker : LevelRun, IConditionalContainerRules
{
    private readonly Dictionary<GameTask, Action<GameTask>> _deactivations = [];

    /// <summary>Initializes a new instance of the <see cref="LevelUpdateWalker"/> class.</summary>
    /// <param name="root">The level's root task.</param>
    public LevelUpdateWalker(GameTask root)
        : base(root)
    {
    }

    /// <summary>Gets or sets what runs a conditional container's start or stop script, after its children ran.</summary>
    public Action<ConditionalContainerTask, string>? ContainerScript { get; set; }

    /// <summary>Gets or sets a container whose run state is held rather than re-evaluated this tick.</summary>
    public Func<ConditionalContainerTask, bool>? HoldContainer { get; set; }

    /// <summary>Registers what a task does when its conditional container takes it out of the world.</summary>
    /// <param name="task">The task.</param>
    /// <param name="deactivate">Its deactivation.</param>
    public void RegisterDeactivation(GameTask task, Action<GameTask> deactivate)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(deactivate);
        _deactivations[task] = deactivate;
    }

    /// <summary>Runs one tick: the root's children in order, and the tick counted.</summary>
    public void Tick()
    {
        RunChildren(Root);
        CountTick();
    }

    /// <inheritdoc/>
    /// <remarks>Project I.G.I.'s handler (<c>0x4874E0</c>) runs the children while the container runs or ran last tick.</remarks>
    bool IConditionalContainerRules.RunsChildrenTheTickItStops => true;

    /// <inheritdoc/>
    /// <remarks>A held container keeps its state; one no gate prepared runs.</remarks>
    bool? IConditionalContainerRules.Condition(ConditionalContainerTask container)
    {
        if (HoldContainer?.Invoke(container) == true)
        {
            return null;
        }

        return container.ConditionResolver is { } resolve ? resolve(container) : true;
    }

    /// <inheritdoc/>
    void IConditionalContainerRules.RunScript(ConditionalContainerTask container, string script) =>
        ContainerScript?.Invoke(container, script);

    /// <inheritdoc/>
    void IConditionalContainerRules.PutChildrenInWorld(ConditionalContainerTask container) => Activate(container);

    /// <inheritdoc/>
    void IConditionalContainerRules.TakeChildrenOutOfWorld(ConditionalContainerTask container) => Deactivate(container);

    /// <inheritdoc/>
    /// <remarks><c>Static</c> and <c>Idle</c> do nothing; anything else walks its children.</remarks>
    protected override void RunUntyped(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Type.Name is "Static" or "Idle")
        {
            return;
        }

        RunChildren(task);
    }

    /// <inheritdoc/>
    /// <remarks>I.G.I.'s walk reaches every linked child, deleted or not.</remarks>
    protected override bool Skips(GameTask child) => false;

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
