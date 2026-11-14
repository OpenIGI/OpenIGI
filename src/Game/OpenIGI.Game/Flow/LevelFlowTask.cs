using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.Saves;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Flow;

/// <summary>
/// What decides whether a level has been completed or failed, and counts its ticks: the engine's <c>LevelFlow</c>, exporting
/// <c>isComplete</c>, <c>isFailed</c>, <c>nTick</c> and <c>eDifficulty</c>.
/// </summary>
/// <remarks>
/// <para>
/// One type in both games, declared under <c>EditorRigidObj</c> with the same seven parameters (Project I.G.I.
/// <c>LevelFlow_Init</c>, run handler <c>0x489290</c>; I.G.I. 2 <c>LevelFlow_RunHandler</c>).
/// </para>
/// <para>
/// Its run handler, in this order: its children run, so what hangs beneath it reads the verdicts of the tick before; its
/// <c>Complete</c> and <c>Failed</c> slots, where it has them, are each cut to a byte and stored; a failure clears
/// completion, whichever of the two wrote it; and the tick is counted. A slot that is empty or does not compile leaves its
/// byte alone, so a script's own write to it stands.
/// </para>
/// </remarks>
public partial class LevelFlowTask : TickedTask, IExchangedState
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LevelFlow";

    private LevelExpression? _complete;
    private LevelExpression? _failed;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation, which nothing reads.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the hour of the day the level starts at.</summary>
    [ScriptParam(ScriptParamType.Real32, "Start time")]
    public float StartTime { get; set; }

    /// <summary>Gets or sets the script that says the level is complete.</summary>
    [ScriptParam(ScriptParamType.VarString, "Complete")]
    public string CompleteScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that says the level has failed.</summary>
    [ScriptParam(ScriptParamType.VarString, "Failed")]
    public string FailedScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the status screen shows the time left.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Interface timer enabled")]
    public bool InterfaceTimerEnabled { get; set; }

    /// <summary>Gets or sets the longest the level may be played, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max level play time")]
    public float MaxPlayTime { get; set; }

    /// <summary>Gets or sets the completion byte, <c>isComplete</c>, read back sign-extended.</summary>
    public sbyte Complete { get; set; }

    /// <summary>Gets or sets the failure byte, <c>isFailed</c>.</summary>
    public sbyte Failed { get; set; }

    /// <summary>Gets a value indicating whether the level is complete.</summary>
    public bool IsComplete => Complete != 0;

    /// <summary>Gets a value indicating whether the level has failed.</summary>
    public bool IsFailed => Failed != 0;

    /// <summary>Gets or sets the ticks the level has run, <c>nTick</c>.</summary>
    public int Tick { get; set; }

    /// <summary>Gets or sets the difficulty, <c>eDifficulty</c>: the active profile's, or 1 when there is none.</summary>
    public int Difficulty { get; set; } = 1;

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Conclude(level);
    }

    /// <summary>The run handler's own work, once its children have run: the verdicts, then the tick counted.</summary>
    /// <param name="level">The level's run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    /// <remarks>A replica's verdicts are its server's: it only counts the tick.</remarks>
    public void Conclude(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!level.IsReplica && level.EvaluatesExpressions)
        {
            _complete ??= level.Compile(CompleteScript, this);
            _failed ??= level.Compile(FailedScript, this);
            if (!_complete.IsEmpty)
            {
                Complete = unchecked((sbyte)LevelExpression.Truncate(level.Evaluate(_complete)));
            }

            if (!_failed.IsEmpty)
            {
                Failed = unchecked((sbyte)LevelExpression.Truncate(level.Evaluate(_failed)));
            }

            if (Failed != 0)
            {
                Complete = 0;
            }
        }

        Tick++;
        OnTickCounted();
    }

    /// <inheritdoc/>
    /// <remarks>Not retail: Project I.G.I. kept no save games. What this port's keep of it is its verdicts and its clock.</remarks>
    public virtual void Exchange(StateExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Complete = (sbyte)exchange.Int(Complete);
        Failed = (sbyte)exchange.Int(Failed);
        Tick = exchange.Int(Tick);
    }

    /// <summary>Called once the tick has been counted, for what a title counts beside it.</summary>
    protected virtual void OnTickCounted()
    {
    }

    /// <inheritdoc/>
    /// <remarks>Project I.G.I. <c>LevelFlow_Activate</c> (<c>0x488F90</c>); I.G.I. 2's register-symbols handler.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isComplete", () => Complete, value => Complete = unchecked((sbyte)value), size: 1);
        variables.BindInt(this, "isFailed", () => Failed, value => Failed = unchecked((sbyte)value), size: 1);
        variables.BindInt(this, "nTick", () => Tick, value => Tick = value);
        variables.BindInt(this, "eDifficulty", () => Difficulty, value => Difficulty = value);
    }
}
