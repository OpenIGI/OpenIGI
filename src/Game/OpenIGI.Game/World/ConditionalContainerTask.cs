using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's <c>ConditionalContainer</c>, in either title: a container whose children are in the world, and run, only while
/// its <c>Condition</c> holds, exporting <c>isRun</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both games run the same handler, Project I.G.I.'s <c>0x4874E0</c> and I.G.I. 2's <c>ConditionalContainer_RunHandler</c>
/// (<c>igi2 0x520EF0</c>): a condition, when there is one, is run in the float walk and chopped into the <c>isRun</c> byte;
/// with none, <c>isRun</c> keeps what it had. While the container runs, children taken out of the world are put back
/// (<c>ConditionalContainer_RegisterChildren</c>, <c>igi2 0x520D20</c>) and then run; otherwise children still in the world
/// are taken out (<c>ConditionalContainer_RemoveChildren</c>, <c>igi2 0x520DC0</c>), each setting the in-world byte after
/// it has told the children. Last, the tick it starts its <c>Run at start</c> script runs, and the tick it stops its
/// <c>Run at stop</c>. What the titles decide differently, the level run's <see cref="IConditionalContainerRules"/> says.
/// </para>
/// <para>
/// The three parameters are the ones level data declares
/// (<c>Task_DeclareParameters("ConditionalContainer", "Condition", "VarString", "Run at start", "VarString", "Run at stop",
/// "VarString")</c>). The children stay tasks throughout, so their exported variables keep resolving while they are out of
/// the world.
/// </para>
/// <para>
/// <b>Why Project I.G.I. needs this.</b> Level 1 places its intro and outro cut-scene actors inside containers gated on
/// cut-scene state that is false during ordinary play (<c>ConditionalContainer_4001</c> "Intro cutscene", <c>4007</c> "Outro
/// Cutscene"), among them a second David Jones on the player's own model. The player and the real guards sit under
/// <c>ConditionalContainer_1495</c>, whose condition <c>!4001.isRun &amp;&amp; !4007.isRun</c> holds during play.
/// <see cref="ConditionalContainerGate"/> settles the first game's containers before the first tick, and render, spawn, AI
/// and collision paths read <see cref="IsActive"/>.
/// </para>
/// </remarks>
public partial class ConditionalContainerTask : TickedTask
{
    /// <summary>The script name the level data instantiates this type under.</summary>
    public const string TypeName = "ConditionalContainer";

    private byte? _isRun;
    private bool _resolving;

    /// <summary>The expression that decides whether the subtree is in the world — <c>Condition</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>The expression run once when the container starts — <c>Run at start</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run at start")]
    public string RunAtStart { get; set; } = string.Empty;

    /// <summary>The expression run once when the container stops — <c>Run at stop</c>.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run at stop")]
    public string RunAtStop { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how Project I.G.I.'s gate reads the condition before the container's first tick: whether it holds, or
    /// <see langword="null"/> for an empty condition. With none set, an unsettled container runs, so a tree no gate
    /// prepared is not hidden.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="ConditionalContainerGate.Prepare"/>. It is a closure rather than a stored value because a
    /// container's condition can name another container's <c>isRun</c> (<c>1495</c> reads <c>4001</c> and <c>4007</c>), and
    /// settling one pulls the others through their exported variables.
    /// </remarks>
    internal Func<ConditionalContainerTask, bool?>? ConditionResolver { get; set; }

    /// <summary>Gets a value indicating whether the container runs — the original's <c>isRun</c> byte.</summary>
    /// <remarks>
    /// Before its first tick a container Project I.G.I.'s gate prepared settles here, through
    /// <see cref="ConditionResolver"/>. One whose condition transitively names itself reads its own <see cref="IsRun"/> as
    /// <see langword="false"/> mid-settling, as the original starts every container un-run.
    /// </remarks>
    public bool IsRun
    {
        get
        {
            if (_isRun is { } latched)
            {
                return latched != 0;
            }

            if (ConditionResolver is null)
            {
                return true;
            }

            if (_resolving)
            {
                return false;
            }

            _resolving = true;
            try
            {
                _isRun = ConditionResolver(this) == true ? (byte)1 : (byte)0;
            }
            finally
            {
                _resolving = false;
            }

            return _isRun.Value != 0;
        }
    }

    /// <summary>
    /// Gets or sets whether the container's children are in the world — the original's <c>active</c> byte, distinct from
    /// <see cref="IsRun"/>.
    /// </summary>
    /// <remarks>
    /// A level's children are in the world as it loads (I.G.I. 2's <c>ConditionalContainer_CreateHandler</c>,
    /// <c>igi2 0x520E90</c>, sets it), so a container whose condition fails on its first tick takes them out then. In
    /// Project I.G.I. the children run once more on the tick the condition fails, so this drops a tick after
    /// <see cref="IsRun"/>.
    /// </remarks>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether Project I.G.I.'s gate and cut-scene runtime settle this container outside the tick.
    /// I.G.I. 2's container answers <see langword="false"/>: it is settled only by its own run.
    /// </summary>
    public virtual bool SettlesOutsideTheTick => true;

    /// <summary>Gets the <c>isRun</c> byte, nought until the container settles.</summary>
    protected byte RunByte => _isRun ?? 0;

    /// <summary>Sets the <c>isRun</c> byte, as a script write or a save does.</summary>
    /// <param name="value">The byte.</param>
    protected void SetRunByte(byte value) => _isRun = value;

    /// <inheritdoc/>
    /// <remarks>The shared run handler; a level run with no <see cref="IConditionalContainerRules"/> runs the children as a plain container.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level is not IConditionalContainerRules rules)
        {
            level.RunChildren(this);
            return;
        }

        bool wasRun = _isRun is { } before && before != 0;
        if (rules.Condition(this) is { } holds)
        {
            _isRun = holds ? (byte)1 : (byte)0;
        }

        bool run = _isRun is { } now && now != 0;
        _isRun ??= 0;
        if (run || (wasRun && rules.RunsChildrenTheTickItStops))
        {
            if (!IsActive)
            {
                rules.PutChildrenInWorld(this);
                IsActive = true;
            }

            level.RunChildren(this);
        }
        else if (IsActive)
        {
            rules.TakeChildrenOutOfWorld(this);
            IsActive = false;
        }

        if (run && !wasRun)
        {
            rules.RunScript(this, RunAtStart);
        }
        else if (!run && wasRun)
        {
            rules.RunScript(this, RunAtStop);
        }
    }

    /// <summary>Settles the condition again outside the tick and returns whether <see cref="IsRun"/> changed.</summary>
    /// <param name="started"><see langword="true"/> when this change started the container.</param>
    /// <returns>Whether it changed.</returns>
    internal bool Refresh(out bool started)
    {
        bool before = _isRun is { } latched && latched != 0;
        bool after = ConditionResolver is null ? true : ConditionResolver(this) ?? before;
        _isRun = after ? (byte)1 : (byte)0;
        started = !before && after;
        return before != after;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Both games bind the byte writable (I.G.I. 2's <c>ConditionalContainer_RegisterScriptSymbolsHandler</c>,
    /// <c>igi2 0x521210</c>). Other tasks read it: Project I.G.I.'s <c>LevelTimer_999</c> gates on
    /// <c>ConditionalContainer_4001.isRun</c>.
    /// </remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isRun", ReadRunByte, value => _isRun = (byte)value, sizeof(byte));
    }

    // A read settles the container first; one no gate prepared reads as running.
    private int ReadRunByte()
    {
        bool run = IsRun;
        return _isRun ?? (run ? 1 : 0);
    }
}
