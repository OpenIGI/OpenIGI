using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>A counter the level's scripts read and steer, I.G.I. 2's <c>EditVariable</c>, exporting <c>nValue</c>.</summary>
/// <remarks>
/// <c>EditVariable_RunHandler</c> (<c>igi2 0x4488C0</c>): each tick the <c>Add</c> script's value, truncated, is added to it
/// and the <c>Sub</c> script's taken off. It starts at its <c>Initial value</c>, which is the parameter's own storage.
/// </remarks>
public sealed partial class Igi2EditVariableTask : GameTask, IIgi2RunTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "EditVariable";

    private Igi2LevelExpression? _add;
    private Igi2LevelExpression? _sub;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the value, <c>nValue</c>, which level data gives as its <c>Initial value</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Initial value")]
    public int Value { get; set; }

    /// <summary>Gets or sets the script whose value is added each tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "Add")]
    public string Add { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is taken off each tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "Sub")]
    public string Sub { get; set; } = string.Empty;

    /// <inheritdoc/>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _add ??= new Igi2LevelExpression(Add, Name, level.Natives);
        _sub ??= new Igi2LevelExpression(Sub, Name, level.Natives);
        if (!_add.IsEmpty)
        {
            Value = unchecked(Value + (int)Igi2LevelExpression.Truncate(level.Evaluate(_add)));
        }

        if (!_sub.IsEmpty)
        {
            Value = unchecked(Value - (int)Igi2LevelExpression.Truncate(level.Evaluate(_sub)));
        }
    }

    /// <inheritdoc/>
    /// <remarks><c>EditVariable_RegisterScriptSymbolsHandler</c> (<c>igi2 0x448A20</c>).</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nValue", () => Value, value => Value = value);
    }
}
