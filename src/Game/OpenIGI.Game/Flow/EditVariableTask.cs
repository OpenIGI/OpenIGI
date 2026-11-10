using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.Saves;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Flow;

/// <summary>A counter the level's scripts read and steer: the engine's <c>EditVariable</c>, exporting <c>nValue</c>.</summary>
/// <remarks>
/// <para>
/// One type in both games, declared under <c>EditorRigidObj</c> with the same four parameters and the same handlers
/// (Project I.G.I. <c>0x488800</c>; I.G.I. 2 <c>EditVariable_Open</c>). Its run handler (<c>0x4889B0</c>,
/// <c>EditVariable_RunHandler</c>) adds the <c>Add</c> slot's value to the counter and then takes the <c>Sub</c> slot's
/// off, each evaluated in the float walk and cut to a whole number. It runs no children.
/// </para>
/// <para>
/// The counter starts at the level's <c>Initial value</c>, which is the parameter's own storage. What differs between the
/// titles is only how a slot's text is compiled, which is the level run's to say (<see cref="LevelRun.Compile"/>).
/// </para>
/// </remarks>
public sealed partial class EditVariableTask : TickedTask, IExchangedState
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "EditVariable";

    private LevelExpression? _add;
    private LevelExpression? _sub;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the counter, <c>nValue</c>, which level data gives as its <c>Initial value</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Initial value")]
    public int Value { get; set; }

    /// <summary>Gets or sets the script whose value is added each tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "Add")]
    public string Add { get; set; } = string.Empty;

    /// <summary>Gets or sets the script whose value is taken off each tick.</summary>
    [ScriptParam(ScriptParamType.VarString, "Sub")]
    public string Sub { get; set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks>A replica's counter is its server's, and is left as it was sent; a run with no variables steps none.</remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.IsReplica || !level.EvaluatesExpressions)
        {
            return;
        }

        _add ??= level.Compile(Add, this);
        _sub ??= level.Compile(Sub, this);
        if (!_add.IsEmpty)
        {
            Value = unchecked(Value + (int)LevelExpression.Truncate(level.Evaluate(_add)));
        }

        if (!_sub.IsEmpty)
        {
            Value = unchecked(Value - (int)LevelExpression.Truncate(level.Evaluate(_sub)));
        }
    }

    /// <inheritdoc/>
    /// <remarks>What a save keeps of it is the counter (I.G.I. 2 <c>EditVariable_ptSerializeParameterList</c>).</remarks>
    public void Exchange(StateExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Value = exchange.Int(Value);
    }

    /// <inheritdoc/>
    /// <remarks>Project I.G.I. <c>0x488AD0</c>; I.G.I. 2 <c>EditVariable_RegisterScriptSymbolsHandler</c>.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nValue", () => Value, value => Value = value);
    }
}
