using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Level;

/// <summary>
/// One tick of a level's task tree: every task reached is run, by the work bound to it, by its own <see cref="TickedTask.Run"/>,
/// or by the title's rule for a task of no class of its own.
/// </summary>
/// <remarks>
/// <para>
/// The original's update walk is the same in both games: <c>Task_Update</c> runs a task's run handler, and a container's
/// handler runs its children in order. What differs between titles is only what the many task types with no class of their
/// own do -- I.G.I. walks their children unless they are <c>Static</c> or <c>Idle</c>; I.G.I. 2's level and dynamic
/// containers count the tick and run their deferred work -- which is <see cref="RunUntyped"/>.
/// </para>
/// <para>
/// A task is run by the first of: the work bound to it (<see cref="Bind(GameTask, Action)"/>), which is how a system that owns a
/// task drives it; its own <see cref="TickedTask.Run"/>; <see cref="RunUntyped"/>. Children are run in order, each sibling
/// read before its predecessor runs, so a task may delete itself as it runs.
/// </para>
/// </remarks>
public abstract class LevelRun
{
    /// <summary>The simulation's rate, in ticks a second.</summary>
    public const int Frequency = 30;

    /// <summary>The length of one tick, in whole milliseconds.</summary>
    public const int MillisecondsPerTick = 1000 / Frequency;

    private readonly Dictionary<GameTask, Action<GameTask>> _bound = [];

    /// <summary>Initializes a new instance of the <see cref="LevelRun"/> class.</summary>
    /// <param name="root">The level's root task.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    protected LevelRun(GameTask root) => Root = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>Gets the level's root task.</summary>
    public GameTask Root { get; }

    /// <summary>Gets how many ticks the level has run.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets the level's clock: the ticks it has run, in milliseconds.</summary>
    public long TimeMilliseconds { get; private set; }

    /// <summary>Binds the work that runs a task in place of its own, given the task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="run">What runs it.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Bind(GameTask task, Action<GameTask> run)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(run);
        _bound[task] = run;
    }

    /// <summary>Binds the work that runs a task in place of its own.</summary>
    /// <param name="task">The task.</param>
    /// <param name="run">What runs it.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Bind(GameTask task, Action run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Bind(task, _ => run());
    }

    /// <summary>Runs one task for this tick.</summary>
    /// <param name="task">The task.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public void Run(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_bound.TryGetValue(task, out Action<GameTask>? bound))
        {
            bound(task);
            return;
        }

        if (task is TickedTask typed)
        {
            typed.Run(this);
            return;
        }

        RunUntyped(task);
    }

    /// <summary>Runs a task's children in order, as a container's run handler does.</summary>
    /// <param name="task">The parent.</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    public void RunChildren(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        GameTask? child = task.FirstChild;
        while (child is not null)
        {
            GameTask? next = child.NextSibling;
            if (!Skips(child))
            {
                Run(child);
            }

            child = next;
        }
    }

    /// <summary>Runs a task of no class of its own and with no work bound to it, by the title's rule.</summary>
    /// <param name="task">The task.</param>
    protected abstract void RunUntyped(GameTask task);

    /// <summary>Gets whether a child is passed over by <see cref="RunChildren"/>.</summary>
    /// <param name="child">The child.</param>
    /// <returns><see langword="true"/> for a task already deleted, by default.</returns>
    protected virtual bool Skips(GameTask child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.IsDeleted;
    }

    /// <summary>Counts one tick of the level's clock.</summary>
    protected void CountTick()
    {
        TimeMilliseconds += MillisecondsPerTick;
        Ticks++;
    }
}
