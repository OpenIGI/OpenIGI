namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// A native's argument list, evaluated on demand in the type the native asks for.
/// </summary>
/// <remarks>
/// <para>
/// This is the original's argument-access family — <c>Arg_GetInt</c> (<c>0x4B8A20</c>),
/// <c>Arg_GetFloat</c> (<c>0x4B8A50</c>) and <c>Arg_GetString</c> (<c>0x4B8A80</c>) — each of which
/// runs the matching evaluator over the argument's node at the moment the native asks.
/// </para>
/// <para>
/// Nothing is evaluated up front, which is not an optimisation: a native is registered with an arity
/// and no parameter types, so there is no type to evaluate an argument *as* until the callee names
/// one, and a native that reads its arguments out of order runs their side effects out of order.
/// Reading the same argument twice evaluates it twice, exactly as the original does.
/// </para>
/// <para>
/// An index outside the list is not an error: the original indexes its node array unguarded, so this
/// yields the type's zero rather than throwing, and the arity check at parse time is what is supposed
/// to have prevented it.
/// </para>
/// </remarks>
public sealed class ScriptArguments
{
    private static readonly ScriptExpression[] NoArguments = [];

    private readonly ScriptExpressionEvaluator _evaluator;
    private readonly IReadOnlyList<ScriptExpression> _nodes;

    /// <summary>Initializes a new instance of the <see cref="ScriptArguments"/> class.</summary>
    /// <param name="evaluator">The evaluator to run an argument through.</param>
    /// <param name="nodes">The unevaluated argument nodes, in source order.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ScriptArguments(ScriptExpressionEvaluator evaluator, IReadOnlyList<ScriptExpression> nodes)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(nodes);

        _evaluator = evaluator;
        _nodes = nodes;
    }

    /// <summary>Gets an empty argument list, for a name used without a call.</summary>
    /// <param name="evaluator">The evaluator the list would run against.</param>
    /// <returns>An empty list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="evaluator"/> is <see langword="null"/>.</exception>
    public static ScriptArguments Empty(ScriptExpressionEvaluator evaluator) => new(evaluator, NoArguments);

    /// <summary>Gets the number of arguments the call site supplied.</summary>
    public int Count => _nodes.Count;

    /// <summary>Evaluates an argument as an integer.</summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <returns>The value, or zero when there is no such argument.</returns>
    public int GetInt32(int index) => At(index) is { } node ? _evaluator.EvaluateInt32(node) : 0;

    /// <summary>Evaluates an argument as a <see cref="bool"/>, which the original stores as one byte.</summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <returns><see langword="true"/> when the integer value is non-zero.</returns>
    public bool GetBoolean(int index) => GetInt32(index) != 0;

    /// <summary>Evaluates an argument as a <see cref="double"/>.</summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <returns>The value, or zero when there is no such argument.</returns>
    public double GetReal(int index) => At(index) is { } node ? _evaluator.EvaluateReal(node) : 0.0;

    /// <summary>Evaluates an argument as text.</summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <returns>The value, or an empty string when there is no such argument.</returns>
    public string GetText(int index) => At(index) is { } node ? _evaluator.EvaluateText(node) : string.Empty;

    private ScriptExpression? At(int index) => index >= 0 && index < _nodes.Count ? _nodes[index] : null;
}
