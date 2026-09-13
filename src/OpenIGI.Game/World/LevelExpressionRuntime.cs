using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>Parses and evaluates level-task conditions against their live exported variables.</summary>
/// <remarks>
/// An authored <c>this.field</c> name resolves against the owner supplied for that evaluation. The
/// parsed expression cache is shared, but each evaluation receives an immutable owner context so a
/// cached or reentrant evaluation cannot replace another evaluation's <c>this</c> task.
/// </remarks>
public sealed class LevelExpressionRuntime
{
    private readonly ScriptVarRegistry _variables;
    private readonly Func<string, ScriptArguments, QvmValue?>? _invoke;
    private readonly Dictionary<string, ScriptExpression?> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates an evaluator over a mission's script variables.</summary>
    public LevelExpressionRuntime(
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? invoke = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = variables;
        _invoke = invoke;
    }

    /// <summary>The suffix the original's preprocessor answers before any variable is consulted.</summary>
    /// <remarks>
    /// <c>0x4F1574</c> is the line after the <c>this.</c> rewrite: any token CONTAINING
    /// <c>.isExists</c> is replaced outright with the literal <c>1</c>, so the name never reaches the
    /// variable table and the answer is always true. It is a <c>strstr</c> in the original rather than
    /// a suffix test, and this matches that.
    /// <para>
    /// No shipped level uses it — the string appears nowhere in the retail mission data, only in the
    /// executable — so this changes nothing about the campaign. It is here so a level that does use it
    /// reads the way the original would answer, rather than silently resolving to zero, which is the
    /// opposite of what the engine says.
    /// </para>
    /// </remarks>
    public const string AlwaysTrueField = ".isExists";

    /// <summary>Evaluates an authored condition; an empty condition uses the caller's default.</summary>
    public bool EvaluateCondition(string? source, bool emptyValue, GameTask? owner = null)
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
            && CreateEvaluator(owner).EvaluateReal(expression) != 0.0;
    }

    /// <summary>Evaluates an authored integer expression; an empty expression uses the caller's default.</summary>
    public int EvaluateInt32(string? source, int emptyValue = 0, GameTask? owner = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        ScriptExpression? expression = Parse(source);
        return expression is null
            ? 0
            : CreateEvaluator(owner).Evaluate(expression);
    }

    /// <summary>Evaluates an authored numeric expression as a single-precision value.</summary>
    public float EvaluateSingle(string? source, float emptyValue = 0f, GameTask? owner = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return emptyValue;
        }

        ScriptExpression? expression = Parse(source);
        return expression is null
            ? 0f
            : (float)CreateEvaluator(owner).EvaluateReal(expression);
    }

    /// <summary>Evaluates an authored expression for its side effects and returns its integer result.</summary>
    public int Execute(string? source, GameTask? owner = null) => EvaluateInt32(source, 0, owner);

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

    private ScriptExpressionEvaluator CreateEvaluator(GameTask? owner) =>
        new(new Context(_variables, _invoke, owner));

    private sealed class Context(
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? invoke,
        GameTask? owner) : IScriptExpressionContext
    {
        public bool TryGetValue(string name, out QvmValue value)
        {
            string resolved = Resolve(name);
            if (resolved.Contains(AlwaysTrueField, StringComparison.Ordinal))
            {
                value = QvmValue.FromInt32(1);
                return true;
            }

            return variables.TryRead(resolved, out value);
        }

        public QvmValueKind GetValueKind(string name)
        {
            string resolved = Resolve(name);
            return resolved.Contains(AlwaysTrueField, StringComparison.Ordinal)
                ? QvmValueKind.Integer
                : variables.GetValueKind(resolved);
        }
        public bool TrySetValue(string name, QvmValue value) => variables.TryWrite(Resolve(name), value);
        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            QvmValue? invoked = invoke?.Invoke(name, arguments);
            result = invoked ?? QvmValue.Zero;
            return invoked.HasValue;
        }

        private string Resolve(string name) => owner is not null && name.StartsWith("this.", StringComparison.Ordinal)
            ? string.Concat(owner.Name, name.AsSpan("this".Length))
            : name;
    }
}
