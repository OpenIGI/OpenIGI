using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.Saves;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Flow;

/// <summary>A clock a level's scripts start, stop and reset: the engine's <c>LevelTimer</c>, exporting <c>isRun</c> and <c>nTick</c>.</summary>
/// <remarks>
/// <para>
/// One type in both games, declared under <c>EditorRigidObj</c> with the same five parameters and the same handlers
/// (Project I.G.I. <c>LevelTimer_Open</c>, run handler <c>0x51BE10</c>; I.G.I. 2 <c>LevelTimer_RunHandler</c>).
/// </para>
/// <para>
/// Its run handler, in this order: its children run; its <c>On</c> slot, where it has one, is cut to a byte and stored as
/// whether it runs; its <c>Reset</c> slot, where it has one and is not nought, puts the count back to nought; and, running,
/// it counts the tick. With no <c>On</c> slot the byte keeps what it holds: its <c>Initial run</c>, or what a script wrote
/// to <c>isRun</c>.
/// </para>
/// </remarks>
public sealed partial class LevelTimerTask : TickedTask, IExchangedState
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LevelTimer";

    private LevelExpression? _on;
    private LevelExpression? _reset;
    private byte _isRun = 1;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation, which nothing reads.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the script that says whether it runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On")]
    public string OnScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that resets it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Reset")]
    public string ResetScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it runs from the start: the parameter is <c>isRun</c>'s own storage, set at creation.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Initial run")]
    public bool InitialRun
    {
        get => _isRun != 0;
        set => _isRun = value ? (byte)1 : (byte)0;
    }

    /// <summary>Gets a value indicating whether it runs, <c>isRun</c>.</summary>
    public bool IsRun => _isRun != 0;

    /// <summary>Gets the ticks it has run since it was last reset, <c>nTick</c>.</summary>
    public int Tick { get; private set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);

        if (level.EvaluatesExpressions)
        {
            _on ??= level.Compile(OnScript, this);
            if (!_on.IsEmpty)
            {
                _isRun = unchecked((byte)LevelExpression.Truncate(level.Evaluate(_on)));
            }

            _reset ??= level.Compile(ResetScript, this);
            if (!_reset.IsEmpty)
            {
                // Above or below nought: a value that is not a number resets nothing, as the original's comparison has it.
                double reset = level.Evaluate(_reset);
                if (reset < 0.0 || reset > 0.0)
                {
                    Tick = 0;
                }
            }
        }

        if (_isRun != 0)
        {
            Tick++;
        }
    }

    /// <inheritdoc/>
    /// <remarks>What a save keeps of it (I.G.I. 2 <c>LevelTimer_ptSerializeParameterList</c>): whether it runs, and its count.</remarks>
    public void Exchange(StateExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        _isRun = exchange.Bool(_isRun != 0) ? (byte)1 : (byte)0;
        Tick = exchange.Int(Tick);
    }

    /// <inheritdoc/>
    /// <remarks>Project I.G.I. <c>LevelTimer_Register</c> (<c>0x51BCC0</c>); I.G.I. 2 <c>LevelTimer_RegisterScriptSymbolsHandler</c>.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isRun", () => _isRun, value => _isRun = unchecked((byte)value), sizeof(byte));
        variables.BindInt(this, "nTick", () => Tick, value => Tick = value);
    }
}
