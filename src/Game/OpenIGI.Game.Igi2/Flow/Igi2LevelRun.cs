using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Expressions;

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
/// A task whose behaviour lives outside the tree, the player's, is bound to its node (<see cref="LevelRun.Bind(GameTask, Action)"/>)
/// and runs there, in the tree's order and under its containers' conditions. The tick rate is the shared
/// <see cref="LevelRun.Frequency"/>, <c>GAME_FREQUENCY</c>.
/// </para>
/// </remarks>
public sealed class Igi2LevelRun : LevelRun, IConditionalContainerRules
{
    private readonly List<Action> _delayed = [];

    // A conditional container's scripts, each compiled the first time it runs.
    private readonly Dictionary<(GameTask Owner, string Source), LevelExpression> _containerScripts = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2LevelRun"/> class.</summary>
    /// <param name="root">The level's root task.</param>
    /// <param name="natives">The namespace the level's scripts resolved against, with its tasks' variables.</param>
    /// <param name="functions">The natives the level's script text may call.</param>
    public Igi2LevelRun(GameTask root, NativeRegistry natives, Igi2ScriptFunctions functions)
        : base(root)
    {
        Natives = natives ?? throw new ArgumentNullException(nameof(natives));
        Functions = functions ?? throw new ArgumentNullException(nameof(functions));
        LevelFlow = root.Children.OfType<Igi2LevelFlowTask>().FirstOrDefault();
        functions.Level = root.Tree;
    }

    /// <summary>Gets the level's flow, <c>LevelFlow_ptLevelFlow</c>, whose tick switches time their presses by.</summary>
    public Igi2LevelFlowTask? LevelFlow { get; }

    /// <summary>Gets the namespace the level's expressions resolve in.</summary>
    public NativeRegistry Natives { get; }

    /// <summary>Gets the natives the level's script text may call.</summary>
    public Igi2ScriptFunctions Functions { get; }

    /// <summary>Gets or sets what runs after the level's own objects under <c>Dynamic</c>, or <see langword="null"/>.</summary>
    public World.Igi2DynamicTasks? Dynamic { get; set; }

    /// <summary>Gets a value indicating whether a cutscene ran this tick, <c>CutScene_isRunning</c>.</summary>
    public bool IsCutsceneRunning { get; private set; }

    /// <summary>Gets or sets a value indicating whether the cutscene break key is down, as <c>LevelFlow_GetBreakCutSceneKey</c> reads it.</summary>
    public bool IsBreakKeyDown { get; set; }

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

    /// <summary>Gets or sets which of the level's sector portals its doors leave open, which the sector walk reads.</summary>
    public World.Igi2SectorPortals? SectorPortals { get; set; }

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

    /// <inheritdoc/>
    /// <remarks>
    /// The level counts the tick after its children have run (<c>Level_LevelRunHandler</c>); <c>Dynamic</c> runs the level's
    /// objects, then what was added after them, then the delayed pass (<c>Level_DynamicRunHandler</c>). Any other type with no
    /// class of its own has no run handler, and the pass stops beneath it.
    /// </remarks>
    protected override void RunUntyped(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        switch (task.Type.Name)
        {
            case EngineTaskTypes.Level:
                RunChildren(task);
                CountTick();
                break;

            case EngineTaskTypes.Dynamic:
                RunChildren(task);
                Dynamic?.Run();
                RunDelayed();
                break;
        }
    }

    /// <summary>Runs an expression and keeps its value.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>Its value.</returns>
    public double Evaluate(LevelExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.Run(Natives, Functions);
    }

    /// <inheritdoc/>
    /// <remarks><c>ConditionalContainer_RunHandler</c> runs the children only while the container runs.</remarks>
    bool IConditionalContainerRules.RunsChildrenTheTickItStops => false;

    /// <inheritdoc/>
    bool? IConditionalContainerRules.Condition(ConditionalContainerTask container)
    {
        LevelExpression condition = ContainerScript(container, container.Condition);
        return condition.IsEmpty ? null : LevelExpression.Flag(Evaluate(condition));
    }

    /// <inheritdoc/>
    void IConditionalContainerRules.RunScript(ConditionalContainerTask container, string script)
    {
        LevelExpression expression = ContainerScript(container, script);
        if (!expression.IsEmpty)
        {
            Evaluate(expression);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Nothing of the title's world needs telling yet; the container's flag is enough.</remarks>
    void IConditionalContainerRules.PutChildrenInWorld(ConditionalContainerTask container)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Every task under the container that is in the world is told, in tree order.</remarks>
    void IConditionalContainerRules.TakeChildrenOutOfWorld(ConditionalContainerTask container) => RemoveFromWorld(container);

    // Tells every task under a task that it is taken out of the world, in tree order.
    private static void RemoveFromWorld(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is World.IIgi2WorldMember member)
            {
                member.RemoveFromWorld();
            }

            RemoveFromWorld(child);
        }
    }

    private LevelExpression ContainerScript(ConditionalContainerTask container, string source)
    {
        if (!_containerScripts.TryGetValue((container, source), out LevelExpression? expression))
        {
            expression = Igi2LevelExpressions.Create(source, container.Name, Natives);
            _containerScripts[(container, source)] = expression;
        }

        return expression;
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
