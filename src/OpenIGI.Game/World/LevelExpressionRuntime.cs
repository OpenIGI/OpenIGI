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
    public LevelExpressionRuntime(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _context = new Context(variables);
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

        return expression is not null && new ScriptExpressionEvaluator(_context).EvaluateCondition(expression);
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

    private sealed class Context(ScriptVarRegistry variables) : IScriptExpressionContext
    {
        public bool TryGetValue(string name, out QvmValue value) => variables.TryRead(name, out value);
        public QvmValueKind GetValueKind(string name) => variables.GetValueKind(name);
        public bool TrySetValue(string name, QvmValue value) => variables.TryWrite(name, value);
        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            result = QvmValue.Zero;
            return false;
        }
    }
}
