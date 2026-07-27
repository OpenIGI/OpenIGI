using System.Globalization;

namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// A node of a parsed expression tree.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the 40-byte node the original allocates in <c>Expr_NewNode</c> (<c>0x4BA880</c>): a kind, an
/// operator, a precedence, two children and a parent. The parent link is what its in-place
/// precedence-climbing parser walks; here the tree is built by recursion instead, which produces the
/// same shape without the pointer surgery — see <see cref="ScriptExpressionParser"/>.
/// </para>
/// <para>
/// A node is immutable once built, so a compiled expression can be cached on a task parameter and
/// evaluated repeatedly, which is what the menu system does with its action strings.
/// </para>
/// </remarks>
public sealed class ScriptExpression
{
    private readonly ScriptExpression[] _arguments;

    private ScriptExpression(
        ScriptExpressionKind kind,
        ScriptOperator op,
        string text,
        double number,
        int constant,
        int line,
        ScriptExpression? left,
        ScriptExpression? right,
        ScriptExpression? extra,
        ScriptExpression[]? arguments)
    {
        Kind = kind;
        Operator = op;
        Text = text;
        Number = number;
        Constant = constant;
        Line = line;
        Left = left;
        Right = right;
        Extra = extra;
        _arguments = arguments ?? [];
    }

    /// <summary>Gets the node kind.</summary>
    public ScriptExpressionKind Kind { get; }

    /// <summary>Gets the operator, for an <see cref="ScriptExpressionKind.Operator"/> node.</summary>
    public ScriptOperator Operator { get; }

    /// <summary>Gets the identifier name or string-literal contents.</summary>
    public string Text { get; }

    /// <summary>Gets the value of a numeric literal.</summary>
    public double Number { get; }

    /// <summary>Gets the value of a constant.</summary>
    public int Constant { get; }

    /// <summary>Gets the one-based source line this node was parsed from.</summary>
    public int Line { get; }

    /// <summary>Gets the left operand, the ternary's condition, or the group's contents.</summary>
    public ScriptExpression? Left { get; }

    /// <summary>Gets the right operand, a unary operator's operand, or the ternary's "then" branch.</summary>
    public ScriptExpression? Right { get; }

    /// <summary>Gets the ternary's "else" branch.</summary>
    public ScriptExpression? Extra { get; }

    /// <summary>Gets the call arguments, when this identifier is called.</summary>
    public IReadOnlyList<ScriptExpression> Arguments => _arguments;

    /// <summary>Gets a value indicating whether this identifier is a call rather than a bare name.</summary>
    public bool IsCall { get; private init; }

    /// <summary>Gets a value indicating whether this node can appear on the left of an assignment.</summary>
    /// <remarks>
    /// Only a bare name can. The original rejects anything else with
    /// <c>"Expression: Cannot assign to constant"</c>.
    /// </remarks>
    public bool IsAssignable => Kind == ScriptExpressionKind.Identifier && !IsCall;

    /// <summary>Creates a constant node.</summary>
    /// <param name="value">The constant's value.</param>
    /// <param name="name">The name it was written as.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromConstant(int value, string name, int line) =>
        new(ScriptExpressionKind.Constant, ScriptOperator.None, name, value, value, line, null, null, null, null);

    /// <summary>Creates a string-literal node.</summary>
    /// <param name="text">The literal's contents.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromString(string text, int line) =>
        new(ScriptExpressionKind.String, ScriptOperator.None, text, 0.0, 0, line, null, null, null, null);

    /// <summary>Creates a numeric-literal node.</summary>
    /// <param name="value">The literal's value.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromNumber(double value, int line) =>
        new(ScriptExpressionKind.Number, ScriptOperator.None, string.Empty, value, 0, line, null, null, null, null);

    /// <summary>Creates a name node.</summary>
    /// <param name="name">The name, which may be a dotted member path.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromIdentifier(string name, int line) =>
        new(ScriptExpressionKind.Identifier, ScriptOperator.None, name, 0.0, 0, line, null, null, null, null);

    /// <summary>Creates a call node.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The argument expressions.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromCall(string name, ScriptExpression[] arguments, int line) =>
        new(ScriptExpressionKind.Identifier, ScriptOperator.None, name, 0.0, 0, line, null, null, null, arguments)
        {
            IsCall = true,
        };

    /// <summary>Creates a parenthesised-group node.</summary>
    /// <param name="inner">The contents.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromGroup(ScriptExpression inner, int line) =>
        new(ScriptExpressionKind.Group, ScriptOperator.None, string.Empty, 0.0, 0, line, inner, null, null, null);

    /// <summary>Creates a unary-operator node.</summary>
    /// <param name="op">The operator.</param>
    /// <param name="operand">The operand.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    /// <remarks>A unary operand hangs off the <em>right</em> child, as it does in the original.</remarks>
    public static ScriptExpression FromUnary(ScriptOperator op, ScriptExpression operand, int line) =>
        new(ScriptExpressionKind.Operator, op, string.Empty, 0.0, 0, line, null, operand, null, null);

    /// <summary>Creates a binary-operator node.</summary>
    /// <param name="op">The operator.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromBinary(ScriptOperator op, ScriptExpression left, ScriptExpression right, int line) =>
        new(ScriptExpressionKind.Operator, op, string.Empty, 0.0, 0, line, left, right, null, null);

    /// <summary>Creates a ternary node.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="whenTrue">The value when the condition holds.</param>
    /// <param name="whenFalse">The value otherwise.</param>
    /// <param name="line">The source line.</param>
    /// <returns>The node.</returns>
    public static ScriptExpression FromConditional(
        ScriptExpression condition,
        ScriptExpression whenTrue,
        ScriptExpression whenFalse,
        int line) =>
        new(
            ScriptExpressionKind.Conditional,
            ScriptOperator.Question,
            string.Empty,
            0.0,
            0,
            line,
            condition,
            whenTrue,
            whenFalse,
            null);

    /// <summary>Renders the tree back to source, for diagnostics and round-trip tests.</summary>
    /// <returns>A parenthesised rendering that makes the tree's shape explicit.</returns>
    public override string ToString() => Kind switch
    {
        ScriptExpressionKind.Constant => Text.Length > 0 ? Text : Constant.ToString(CultureInfo.InvariantCulture),
        ScriptExpressionKind.String => $"\"{Text}\"",
        ScriptExpressionKind.Number => Number.ToString("0.################", CultureInfo.InvariantCulture),
        ScriptExpressionKind.Identifier => IsCall
            ? $"{Text}({string.Join(", ", _arguments.Select(a => a.ToString()))})"
            : Text,
        ScriptExpressionKind.Group => $"({Left})",
        ScriptExpressionKind.Conditional => $"({Left} ? {Right} : {Extra})",
        ScriptExpressionKind.Operator => ScriptOperatorInfo.IsUnary(Operator)
            ? $"({Symbol(Operator)}{Right})"
            : $"({Left} {Symbol(Operator)} {Right})",
        _ => base.ToString() ?? string.Empty,
    };

    private static string Symbol(ScriptOperator op) => op switch
    {
        ScriptOperator.Add or ScriptOperator.UnaryPlus => "+",
        ScriptOperator.Subtract or ScriptOperator.UnaryMinus => "-",
        ScriptOperator.Multiply => "*",
        ScriptOperator.Divide => "/",
        ScriptOperator.BitwiseOr => "|",
        ScriptOperator.BitwiseXor => "^",
        ScriptOperator.BitwiseAnd => "&",
        ScriptOperator.ShiftLeft => "<<",
        ScriptOperator.ShiftRight => ">>",
        ScriptOperator.BitwiseNot => "~",
        ScriptOperator.Equal => "==",
        ScriptOperator.NotEqual => "!=",
        ScriptOperator.Less => "<",
        ScriptOperator.Greater => ">",
        ScriptOperator.LessOrEqual => "<=",
        ScriptOperator.GreaterOrEqual => ">=",
        ScriptOperator.Not => "!",
        ScriptOperator.Assign => "=",
        ScriptOperator.LogicalOr => "||",
        ScriptOperator.LogicalAnd => "&&",
        ScriptOperator.Comma => ",",
        _ => op.ToString(),
    };
}
