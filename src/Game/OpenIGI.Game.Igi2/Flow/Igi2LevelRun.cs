using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The level's tick: its task tree run from the root, every task by its own run handler, as <c>Flow_RunChildren</c>
/// (<c>igi2 0x401A50</c>) runs the game's tasks.
/// </summary>
/// <remarks>
/// <para>
/// The run pass is not a walk of the tree. A task's children run only if its run handler runs them, as <c>Container</c>'s
/// does (<c>Container_RunHandler</c>, <c>0x5C6C60</c>) and as a <c>ConditionalContainer</c>'s does only while its
/// condition holds; a type with no run handler stops the pass beneath it.
/// </para>
/// <para>
/// The root is the <c>Level</c> (<c>Level_LevelRunHandler</c>, <c>0x543400</c>), which runs its children and then counts
/// the tick. Its <c>Static</c> child does nothing (<c>Level_StaticRunHandler</c>, <c>0x5434A0</c>); its <c>Dynamic</c> child
/// (<c>Level_DynamicRunHandler</c>, <c>0x5435F0</c>) runs the level's own objects, then what has been added after them —
/// grenades, explosions and what they leave (<see cref="World.Igi2DynamicTasks"/>) — then the delayed pass
/// (<c>RunDelayed_Run</c>, <c>0x58FDB0</c>).
/// </para>
/// <para>
/// A task whose behaviour lives outside the tree, the player's, is bound to its node (<see cref="Bind"/>) and runs there,
/// in the tree's order and under its containers' conditions.
/// </para>
/// </remarks>
public sealed class Igi2LevelRun
{
    /// <summary>The ticks a game second holds, <c>GAME_FREQUENCY</c> and <c>CutScene_nFrequency</c>.</summary>
    public const int Frequency = 30;

    /// <summary>The milliseconds a tick lasts, <c>nMilisecondsPerTick</c>.</summary>
    public const int MillisecondsPerTick = 1000 / Frequency;

    private readonly Dictionary<GameTask, Action> _bound = [];
    private readonly List<Action> _delayed = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2LevelRun"/> class.</summary>
    /// <param name="root">The level's root task.</param>
    /// <param name="natives">The namespace the level's scripts resolved against, with its tasks' variables.</param>
    /// <param name="functions">The natives the level's script text may call.</param>
    public Igi2LevelRun(GameTask root, NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Natives = natives ?? throw new ArgumentNullException(nameof(natives));
        Functions = functions ?? throw new ArgumentNullException(nameof(functions));
        LevelFlow = root.Children.OfType<Igi2LevelFlowTask>().FirstOrDefault();
        functions.Level = root.Tree;
    }

    /// <summary>Gets the level's flow, <c>LevelFlow_ptLevelFlow</c>, whose tick switches time their presses by.</summary>
    public Igi2LevelFlowTask? LevelFlow { get; }

    /// <summary>Gets the level's root task.</summary>
    public GameTask Root { get; }

    /// <summary>Gets the namespace the level's expressions resolve in.</summary>
    public NativeRegistry Natives { get; }

    /// <summary>Gets the natives the level's script text may call.</summary>
    public Igi2ScriptFunctions Functions { get; }

    /// <summary>Gets or sets what runs after the level's own objects under <c>Dynamic</c>, or <see langword="null"/>.</summary>
    public World.Igi2DynamicTasks? Dynamic { get; set; }

    /// <summary>Gets the ticks the level has run, <c>nTicks</c>.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets the level's time, <c>nTime</c>: a tick's milliseconds for every tick run.</summary>
    public long TimeMilliseconds { get; private set; }

    /// <summary>Gets a value indicating whether a cutscene ran this tick, <c>CutScene_isRunning</c>.</summary>
    public bool IsCutsceneRunning { get; private set; }

    /// <summary>Gets or sets a value indicating whether the cutscene break key is down, as <c>LevelFlow_GetBreakCutSceneKey</c> reads it.</summary>
    public bool IsBreakKeyDown { get; set; }

    /// <summary>Binds a task whose behaviour lives outside the tree to its node.</summary>
    /// <param name="task">The task.</param>
    /// <param name="run">What its run handler does.</param>
    public void Bind(GameTask task, Action run)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(run);
        _bound[task] = run;
    }

    /// <summary>Queues work for the delayed pass at the end of <c>Dynamic</c>'s run (<c>RunDelayed_Add</c>).</summary>
    /// <param name="work">The work.</param>
    public void Delay(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _delayed.Add(work);
    }

    /// <summary>Notes that a cutscene ran this tick (<c>CutScene_isRunning |= isRun</c>).</summary>
    public void MarkCutsceneRunning() => IsCutsceneRunning = true;

    /// <summary>Gets the camera the last cutscene to place one set this tick, or <see langword="null"/> when none did.</summary>
    /// <remarks>Its <c>QCamera_Set</c> is at a priority of 1000, over the player's 300, so it is what the level shows.</remarks>
    public Igi2CutsceneView? CutsceneView { get; private set; }

    /// <summary>Gets or sets the random stream cutscene cameras shake by, the session's; none draws nothing.</summary>
    public Igi2CutsceneShake? CutsceneShake { get; set; }

    /// <summary>Sets the camera a cutscene placed this tick.</summary>
    /// <param name="view">The camera.</param>
    public void SetCutsceneView(Igi2CutsceneView view) => CutsceneView = view ?? throw new ArgumentNullException(nameof(view));

    /// <summary>Runs one tick from the root.</summary>
    public void Tick()
    {
        IsCutsceneRunning = false;
        CutsceneView = null;
        Run(Root);
    }

    /// <summary>Runs one task by its run handler.</summary>
    /// <param name="task">The task.</param>
    public void Run(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_bound.TryGetValue(task, out Action? bound))
        {
            bound();
            return;
        }

        if (task is Igi2Task typed)
        {
            typed.Run(this);
            return;
        }

        switch (task.Type.Name)
        {
            case EngineTaskTypes.Level:
                RunChildren(task);
                TimeMilliseconds += MillisecondsPerTick;
                Ticks++;
                break;

            case EngineTaskTypes.Dynamic:
                RunChildren(task);
                Dynamic?.Run();
                RunDelayed();
                break;

        }
    }

    /// <summary>Runs a task's children in order, each by its run handler, as every container's run handler does.</summary>
    /// <param name="task">The task.</param>
    /// <remarks>The next child is taken before a child runs, as the protected task stack does, so a child may delete itself.</remarks>
    public void RunChildren(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        GameTask? child = task.FirstChild;
        while (child is not null)
        {
            GameTask? next = child.NextSibling;
            if (!child.IsDeleted)
            {
                Run(child);
            }

            child = next;
        }
    }

    /// <summary>Runs an expression and keeps its value.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>Its value.</returns>
    public double Evaluate(Igi2LevelExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.Run(Natives, Functions);
    }

    private void RunDelayed()
    {
        for (int i = 0; i < _delayed.Count; i++)
        {
            _delayed[i]();
        }

        _delayed.Clear();
    }
}
