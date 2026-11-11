using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;

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
    private readonly List<Action> _tickEnds = [];

    /// <summary>Initializes a new instance of the <see cref="LevelRun"/> class.</summary>
    /// <param name="root">The level's root task.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    protected LevelRun(GameTask root) => Root = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>Gets the criteria the level's areas count by, which the title's humans and vehicles register.</summary>
    public Flow.AreaCriteria Areas { get; } = new();

    /// <summary>Gets the level's root task.</summary>
    public GameTask Root { get; }

    /// <summary>Gets how many ticks the level has run.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets the level's clock: the ticks it has run, in milliseconds.</summary>
    public long TimeMilliseconds { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this run is the level's own. One that is not only passes over the tree: it reaches no
    /// typed task's run handler and walks such a task's children as it would a plain container's.
    /// </summary>
    /// <remarks>
    /// Not retail. The host still walks the level of a title that runs itself with the first game's walk, which finds nothing
    /// of its own there; a task of either title's must not be run twice a tick, nor by the other title's rules.
    /// </remarks>
    public bool OwnsLevel { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this level's running state is another machine's: a co-op client's counters,
    /// verdicts and messages are its server's, so the tasks that work them out leave them as they were sent.
    /// </summary>
    /// <remarks>Not retail, whose levels each ran on one machine.</remarks>
    public bool IsReplica { get; set; }

    /// <summary>
    /// Gets a value indicating whether this run has the level's variables to evaluate a slot against. A task whose work is an
    /// expression does nothing under one that has not.
    /// </summary>
    /// <remarks>Not retail: a walk made over a level's tree alone, to play its cutscenes or to test a part of it, has none.</remarks>
    public virtual bool EvaluatesExpressions => true;

    /// <summary>Compiles the script text one of a task's slots carries, as the title's engine compiles it.</summary>
    /// <param name="source">The slot's text, or <see langword="null"/> for none.</param>
    /// <param name="owner">The task the slot belongs to, which <c>this.</c> stands for.</param>
    /// <returns>The expression, empty when there is no text or it does not parse.</returns>
    /// <remarks>
    /// Project I.G.I.'s <c>0x4F1400</c>, I.G.I. 2's <c>Level_CompileLevelExpression</c>: the same rewrite, a word at a time,
    /// in each title's <see cref="LevelExpressionDialect"/>. A task compiles a slot once and keeps the expression.
    /// </remarks>
    public abstract LevelExpression Compile(string? source, GameTask owner);

    /// <summary>Evaluates a compiled slot in the float walk, against the level's variables and natives.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>Its value, or nought when it is empty.</returns>
    /// <remarks>Project I.G.I.'s <c>sub_4F16E0</c>, I.G.I. 2's <c>Level_RunLevelExpression</c>; a caller that wants a whole number truncates it.</remarks>
    public abstract double Evaluate(LevelExpression expression);

    /// <summary>Evaluates a line of script as it stands, against the level's constants, variables and natives.</summary>
    /// <param name="source">The line.</param>
    /// <returns>Its value.</returns>
    /// <remarks>
    /// Project I.G.I.'s <c>Script_EvalExpr</c>, I.G.I. 2's <c>Script_ParseLineInt32</c>: what a parameter that is plain script
    /// and not a task's expression slot is read by, such as an area's criteria. No word of it is taken for a task's name.
    /// </remarks>
    public abstract double EvaluateLine(string source);

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

    /// <summary>Gets the work bound to run a task, for a binding that extends it as a derived type extends its parent's update.</summary>
    /// <param name="task">The task.</param>
    /// <param name="run">Receives the work, or <see langword="null"/>.</param>
    /// <returns>Whether the task has bound work.</returns>
    public bool TryGetBound(GameTask task, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Action<GameTask>? run) =>
        _bound.TryGetValue(task, out run);

    /// <summary>Binds the work that runs a task in place of its own.</summary>
    /// <param name="task">The task.</param>
    /// <param name="run">What runs it.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Bind(GameTask task, Action run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Bind(task, _ => run());
    }

    /// <summary>Adds work the level does once a tick, after its tree has run and the tick is counted.</summary>
    /// <param name="work">The work, run in the order it was added.</param>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The level counts its tick after its children (<c>Level_LevelRunHandler</c>); what is added here runs then. Another
    /// game's level, whose tasks this level's walk runs, finishes its own tick here.
    /// </remarks>
    public void AddTickEnd(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _tickEnds.Add(work);
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
            if (OwnsLevel)
            {
                typed.Run(this);
            }
            else
            {
                RunChildren(task);
            }

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
        foreach (Action work in _tickEnds)
        {
            work();
        }
    }
}
