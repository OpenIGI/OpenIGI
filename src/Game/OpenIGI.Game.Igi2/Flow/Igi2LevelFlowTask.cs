using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The level's own clock and its end, I.G.I. 2's <c>LevelFlow</c>, exporting <c>isComplete</c>, <c>isFailed</c>, <c>nTick</c>,
/// <c>nTimeOfDayTick</c> and <c>eDifficulty</c>.
/// </summary>
/// <remarks>
/// <c>LevelFlow_RunHandler</c> (<c>igi2 0x521ED0</c>): its children run; its <c>Complete</c> script's value is <c>isComplete</c>,
/// and when it holds the game is completed; its <c>Failed</c> script's value is <c>isFailed</c>, and when it holds the game is
/// failed and not completed. Then both clocks count the tick.
/// </remarks>
public sealed partial class Igi2LevelFlowTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LevelFlow";

    private Igi2LevelExpression? _complete;
    private Igi2LevelExpression? _failed;
    private byte _isComplete;
    private byte _isFailed;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation in the editor.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the time of day the level starts at.</summary>
    [ScriptParam(ScriptParamType.Real32, "Start time")]
    public float StartTime { get; set; }

    /// <summary>Gets or sets the script whose holding completes the level.</summary>
    [ScriptParam(ScriptParamType.VarString, "Complete")]
    public string CompleteScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose holding fails it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Failed")]
    public string FailedScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the interface shows a countdown.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Interface timer enabled")]
    public bool InterfaceTimerEnabled { get; set; }

    /// <summary>Gets or sets the longest the level may be played, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max level play time")]
    public float MaxPlayTime { get; set; }

    /// <summary>Gets a value indicating whether the level is completed, <c>isComplete</c>.</summary>
    public bool IsComplete => _isComplete != 0;

    /// <summary>Gets a value indicating whether the level is failed, <c>isFailed</c>.</summary>
    public bool IsFailed => _isFailed != 0;

    /// <summary>Gets the ticks it has run, <c>nTick</c>.</summary>
    public int Tick { get; private set; }

    /// <summary>Gets the time-of-day clock, <c>nTimeOfDayTick</c>.</summary>
    public int TimeOfDayTick { get; private set; }

    /// <summary>The ticks the flow runs a second, <c>Flow_ptFlow-&gt;nFrequency</c>.</summary>
    public const int Frequency = 30;

    /// <summary>Sets the clock to an hour of the day, <c>LevelFlow_SetTimeOfDay</c>: the hour times the ticks of an hour.</summary>
    /// <param name="hours">The hour.</param>
    public void SetTimeOfDay(double hours) => TimeOfDayTick = Fpu.ToInt32((Fpu)hours * (Frequency * 3600.0));

    /// <summary>Gets or sets the profile's difficulty when the level was made, <c>eDifficultySetting</c>.</summary>
    public int Difficulty { get; set; } = 1;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _complete ??= new Igi2LevelExpression(CompleteScript, Name, level.Natives);
        _failed ??= new Igi2LevelExpression(FailedScript, Name, level.Natives);

        level.RunChildren(this);
        if (!_complete.IsEmpty)
        {
            _isComplete = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_complete));
        }

        if (!_failed.IsEmpty)
        {
            _isFailed = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_failed));
        }

        if (IsFailed)
        {
            _isComplete = 0;
        }

        Tick++;
        TimeOfDayTick++;
    }

    /// <inheritdoc/>
    /// <remarks><c>LevelFlow_RegisterScriptSymbolsHandler</c> (<c>igi2 0x521AC0</c>).</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isComplete", () => _isComplete, value => _isComplete = (byte)value, sizeof(byte));
        variables.BindInt(this, "isFailed", () => _isFailed, value => _isFailed = (byte)value, sizeof(byte));
        variables.BindInt(this, "nTick", () => Tick, value => Tick = value);
        variables.BindInt(this, "nTimeOfDayTick", () => TimeOfDayTick, value => TimeOfDayTick = value);
        variables.BindInt(this, "eDifficulty", () => Difficulty, value => Difficulty = value);
    }
}
