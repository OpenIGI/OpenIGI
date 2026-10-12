using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// A container whose children run only while a condition holds, I.G.I. 2's <c>ConditionalContainer</c>, exporting
/// <c>isRun</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConditionalContainer_RunHandler</c> (<c>igi2 0x520EF0</c>): the condition, when there is one, is run and its value
/// truncated into <c>isRun</c>; with none, <c>isRun</c> keeps what it had, which is nought until something writes it. While it
/// runs, children taken out of the world are put back (<c>ConditionalContainer_RegisterChildren</c>, <c>0x520D20</c>) and run;
/// while it does not, they are taken out (<c>ConditionalContainer_RemoveChildren</c>, <c>0x520DC0</c>). The tick it starts its
/// <c>Run at start</c> script runs, and the tick it stops its <c>Run at stop</c>.
/// </para>
/// <para>
/// A level's children are in the world as it loads (<c>ConditionalContainer_CreateHandler</c>, <c>0x520E90</c>, sets the flag),
/// so a container whose condition fails on its first tick takes them out then.
/// </para>
/// </remarks>
public sealed partial class Igi2ConditionalContainerTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ConditionalContainer";

    private Igi2LevelExpression? _condition;
    private Igi2LevelExpression? _start;
    private Igi2LevelExpression? _stop;
    private byte _isRun;

    /// <summary>Gets or sets the condition, script text.</summary>
    [ScriptParam(ScriptParamType.VarString, "Condition")]
    public string Condition { get; set; } = string.Empty;

    /// <summary>Gets or sets the script run the tick the condition starts holding.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run at start")]
    public string RunAtStart { get; set; } = string.Empty;

    /// <summary>Gets or sets the script run the tick the condition stops holding.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run at stop")]
    public string RunAtStop { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the children run, <c>isRun</c>.</summary>
    public bool IsRun => _isRun != 0;

    /// <summary>Gets a value indicating whether the children are in the world.</summary>
    public bool AreChildrenInWorld { get; private set; } = true;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _condition ??= new Igi2LevelExpression(Condition, Name, level.Natives);
        _start ??= new Igi2LevelExpression(RunAtStart, Name, level.Natives);
        _stop ??= new Igi2LevelExpression(RunAtStop, Name, level.Natives);

        bool wasRun = IsRun;
        if (!_condition.IsEmpty)
        {
            _isRun = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_condition));
        }

        if (IsRun)
        {
            AreChildrenInWorld = true;
            level.RunChildren(this);
            if (!wasRun && !_start.IsEmpty)
            {
                level.Evaluate(_start);
            }
        }
        else
        {
            AreChildrenInWorld = false;
            if (wasRun && !_stop.IsEmpty)
            {
                level.Evaluate(_stop);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks><c>ConditionalContainer_RegisterScriptSymbolsHandler</c> (<c>igi2 0x521210</c>): a byte, writable.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isRun", () => _isRun, value => _isRun = (byte)value, sizeof(byte));
    }
}
