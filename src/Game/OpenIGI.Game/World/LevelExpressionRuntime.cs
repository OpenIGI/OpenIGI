using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>Parses and evaluates level-task conditions against their live exported variables.</summary>
/// <remarks>
/// Each slot is a <see cref="LevelExpression"/> through Project I.G.I.'s compile (<see cref="LevelExpressionDialect.Igi1"/>),
/// cached by its text and its owner: an authored <c>this.field</c> name is compiled to the owner the expression was made
/// for, so a cached or reentrant evaluation cannot answer for another evaluation's <c>this</c> task, and the words naming
/// tasks are looked up in the owner's tree.
/// </remarks>
public sealed class LevelExpressionRuntime
{
    private readonly Dictionary<(string Source, GameTask? Owner), LevelExpression> _cache = [];
    private readonly Context _context;

    /// <summary>Creates an evaluator over a mission's script variables.</summary>
    public LevelExpressionRuntime(
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? invoke = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _context = new Context(variables, invoke);
    }

    /// <summary>Evaluates an authored condition; an empty condition uses the caller's default.</summary>
    public bool EvaluateCondition(string? source, bool emptyValue, GameTask? owner = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        LevelExpression expression = Parse(source, owner);

        // Task condition slots are evaluated by sub_4F16E0, which calls Expr_EvalFloat
        // (IGI.exe 0x4F1725 -> 0x4B9660). Using the integer walk here truncates a departing
        // elevator's fractional vFloor to its old floor and immediately reopens that landing door.
        return !expression.IsEmpty
            && expression.Evaluate(_context) != 0.0;
    }

    /// <summary>Evaluates an authored integer expression; an empty expression uses the caller's default.</summary>
    public int EvaluateInt32(string? source, int emptyValue = 0, GameTask? owner = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        return Parse(source, owner).EvaluateInt32(_context);
    }

    /// <summary>Evaluates an authored numeric expression as a single-precision value.</summary>
    public float EvaluateSingle(string? source, float emptyValue = 0f, GameTask? owner = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        return (float)Parse(source, owner).Evaluate(_context);
    }

    /// <summary>Evaluates an authored expression that is present, as the original's task slots do.</summary>
    /// <param name="source">The authored expression.</param>
    /// <param name="owner">The task <c>this.</c> resolves against.</param>
    /// <param name="value">Receives the result when the expression is present.</param>
    /// <returns><see langword="false"/> when the slot is empty or did not compile.</returns>
    /// <remarks>
    /// <c>sub_4F16C0</c> answers whether a slot holds a non-empty source with compiled code; callers
    /// such as LevelFlow's update (<c>0x489290</c>) leave their field untouched when it does not, and
    /// otherwise store <c>sub_4F16E0</c>'s floating-point result.
    /// </remarks>
    public bool TryEvaluateReal(string? source, GameTask? owner, out double value)
    {
        value = 0.0;
        if (string.IsNullOrWhiteSpace(source) || Parse(source, owner) is not { IsEmpty: false } expression)
        {
            return false;
        }

        value = expression.Evaluate(_context);
        return true;
    }

    /// <summary>Evaluates an authored expression for its side effects and returns its integer result.</summary>
    public int Execute(string? source, GameTask? owner = null) => EvaluateInt32(source, 0, owner);

    /// <summary>Compiles one of a task's slots as Project I.G.I. does.</summary>
    /// <param name="source">The slot's text.</param>
    /// <param name="owner">The task it belongs to, whose name <c>this.</c> stands for and whose tree holds the tasks its
    /// words name; <see langword="null"/> for none, which takes every named task as present.</param>
    /// <returns>The expression.</returns>
    public static LevelExpression Create(string? source, GameTask? owner) =>
        new(source, owner?.Name, Constant, LevelExpressionDialect.Igi1, owner is { HasTree: true } ? owner.Tree : null);

    private LevelExpression Parse(string source, GameTask? owner)
    {
        if (!_cache.TryGetValue((source, owner), out LevelExpression? cached))
        {
            cached = Create(source, owner);
            _cache[(source, owner)] = cached;
        }

        return cached;
    }

    /// <summary>Gets the integer constants a level expression's parse folds.</summary>
    /// <param name="name">The name as written.</param>
    /// <returns>Its value, or <see langword="null"/> for a name that is not a constant.</returns>
    public static int? Constant(string name) => name switch
    {
        "TRUE" => 1,
        "FALSE" => 0,
        "GAME_FREQUENCY" => 30,
        _ => null,
    };

    // The mission's variables and natives. The compile has already answered this. and .isExists.
    private sealed class Context(
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? invoke) : IScriptExpressionContext
    {
        public bool TryGetValue(string name, out QvmValue value) => variables.TryRead(name, out value);

        public QvmValueKind GetValueKind(string name) => variables.GetValueKind(name);

        public bool TrySetValue(string name, QvmValue value) => variables.TryWrite(name, value);

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            QvmValue? invoked = invoke?.Invoke(name, arguments);
            result = invoked ?? QvmValue.Zero;
            return invoked.HasValue;
        }
    }
}
