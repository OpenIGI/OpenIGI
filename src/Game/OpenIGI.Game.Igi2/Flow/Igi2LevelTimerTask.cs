using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>A clock a level's scripts start, stop and reset, I.G.I. 2's <c>LevelTimer</c>, exporting <c>isRun</c> and <c>nTick</c>.</summary>
/// <remarks>
/// Its tick (<c>LevelTimer_RunHandler</c>, <c>igi2 0x564580</c>): its children run first; then its <c>On</c> script, where it has
/// one, says whether it runs; its <c>Reset</c> script, where it has one and is not nought, puts it back to nought; and running,
/// it counts the tick.
/// </remarks>
public sealed partial class Igi2LevelTimerTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LevelTimer";

    private LevelExpression? _on;
    private LevelExpression? _reset;
    private byte _isRun = 1;
    private int _tick;

    /// <summary>Gets or sets where it is.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the script that says whether it runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "On")]
    public string OnScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that resets it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Reset")]
    public string ResetScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it runs from the start, its <c>isRun</c> as read; it runs unless authored.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Initial run")]
    public bool InitialRun
    {
        get => _isRun != 0;
        set => _isRun = value ? (byte)1 : (byte)0;
    }

    /// <summary>Gets a value indicating whether it runs, <c>isRun</c>.</summary>
    public bool IsRun => _isRun != 0;

    /// <summary>Gets the ticks it has run since it was last reset, <c>nTick</c>.</summary>
    public int Tick => _tick;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);

        _on ??= Igi2LevelExpressions.Create(OnScript, Name, level.Natives);
        if (!_on.IsEmpty)
        {
            _isRun = (byte)LevelExpression.Truncate(level.Evaluate(_on));
        }

        _reset ??= Igi2LevelExpressions.Create(ResetScript, Name, level.Natives);
        if (!_reset.IsEmpty && level.Evaluate(_reset) != 0.0)
        {
            _tick = 0;
        }

        if (_isRun != 0)
        {
            _tick++;
        }
    }

    /// <summary>Exports <c>isRun</c> and <c>nTick</c> (<c>LevelTimer_RegisterScriptSymbolsHandler</c>, <c>igi2 0x5643F0</c>).</summary>
    /// <param name="variables">The level's variables.</param>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isRun", () => _isRun, value => _isRun = (byte)value, sizeof(byte));
        variables.BindInt(this, "nTick", () => _tick, value => _tick = value);
    }
}
