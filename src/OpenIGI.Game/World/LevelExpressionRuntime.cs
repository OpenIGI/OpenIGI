using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>Parses and evaluates level-task conditions against their live exported variables.</summary>
public sealed class LevelExpressionRuntime
{
    private readonly Context _context;
    private readonly Dictionary<string, ScriptExpression?> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates an evaluator over a mission's script variables.</summary>
    public LevelExpressionRuntime(
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? invoke = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _context = new Context(variables, invoke);
    }

    /// <summary>Evaluates an authored condition; an empty condition uses the caller's default.</summary>
    public bool EvaluateCondition(string? source, bool emptyValue)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        if (!_cache.TryGetValue(source, out ScriptExpression? expression))
        {
            try
            {
                expression = ScriptExpressionParser.Parse(source, ResolveConstant);
            }
            catch (ScriptExpressionException)
            {
                expression = null;
            }
            _cache[source] = expression;
        }

        // Task condition slots are evaluated by sub_4F16E0, which calls Expr_EvalFloat
        // (IGI.exe 0x4F1725 -> 0x4B9660). Using the integer walk here truncates a departing
        // elevator's fractional vFloor to its old floor and immediately reopens that landing door.
        return expression is not null
            && new ScriptExpressionEvaluator(_context).EvaluateReal(expression) != 0.0;
    }

    /// <summary>Evaluates an authored integer expression; an empty expression uses the caller's default.</summary>
    public int EvaluateInt32(string? source, int emptyValue = 0)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        ScriptExpression? expression = Parse(source);
        return expression is null ? 0 : new ScriptExpressionEvaluator(_context).Evaluate(expression);
    }

    /// <summary>Evaluates an authored numeric expression as a single-precision value.</summary>
    public float EvaluateSingle(string? source, float emptyValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        ScriptExpression? expression = Parse(source);
        return expression is null
            ? 0f
            : (float)new ScriptExpressionEvaluator(_context).EvaluateReal(expression);
    }

    /// <summary>Evaluates an authored expression for its side effects and returns its integer result.</summary>
    public int Execute(string? source) => EvaluateInt32(source, 0);

    private ScriptExpression? Parse(string source)
    {
        if (_cache.TryGetValue(source, out ScriptExpression? cached))
        {
            return cached;
        }

        try
        {
            cached = ScriptExpressionParser.Parse(source, ResolveConstant);
        }
        catch (ScriptExpressionException)
        {
            cached = null;
        }
        _cache[source] = cached;
        return cached;
    }

    private static int? ResolveConstant(string name) => name switch
    {
        "TRUE" => 1,
        "FALSE" => 0,
        "GAME_FREQUENCY" => 30,
        _ => null,
    };

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
