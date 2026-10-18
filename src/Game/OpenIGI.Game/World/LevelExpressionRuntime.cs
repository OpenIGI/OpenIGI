using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>Parses and evaluates level-task conditions against their live exported variables.</summary>
/// <remarks>
/// Each slot is a <see cref="LevelExpression"/> in the <see cref="LevelExpressionDialect.AsWritten"/> dialect, cached by
/// its text and its owner: an authored <c>this.field</c> name resolves against the owner the expression was made for, so
/// a cached or reentrant evaluation cannot answer for another evaluation's <c>this</c> task.
/// </remarks>
public sealed class LevelExpressionRuntime
{
    private readonly Dictionary<(string Source, string? Owner), LevelExpression> _cache = [];
    private readonly Context _context;

    /// <summary>Creates an evaluator over a mission's script variables.</summary>
    public LevelExpressionRuntime(
        ScriptVarRegistry variables,
        Func<string, ScriptArguments, QvmValue?>? invoke = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _context = new Context(variables, invoke);
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
    public const string AlwaysTrueField = LevelExpressionNames.ExistsField;

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

    private LevelExpression Parse(string source, GameTask? owner)
    {
        string? ownerName = owner?.Name;
        if (!_cache.TryGetValue((source, ownerName), out LevelExpression? cached))
        {
            cached = new LevelExpression(source, ownerName, ResolveConstant, LevelExpressionDialect.AsWritten);
            _cache[(source, ownerName)] = cached;
        }

        return cached;
    }

    private static int? ResolveConstant(string name) => name switch
    {
        "TRUE" => 1,
        "FALSE" => 0,
        "GAME_FREQUENCY" => 30,
        _ => null,
    };

    // The mission's variables and natives. The expression itself answers this. and .isExists.
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
