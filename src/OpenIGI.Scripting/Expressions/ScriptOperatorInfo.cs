namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// The precedence and associativity table the expression parser binds by.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>Expr_Precedence</c> (<c>0x4BA3D0</c>), <c>Expr_IsUnary</c> (<c>0x4BA340</c>) and
/// <c>Expr_IsLeftAssociative</c> (<c>0x4BA380</c>). <b>Lower binds tighter</b> — the original's numbers
/// are ranks, not binding powers, and the parser's comparison is
/// <c>leftAssoc + precedence(incoming) &lt;= precedence(top)</c>.
/// </para>
/// <para>
/// The ordering is exactly C's, which is worth stating because it means an expression from a retail
/// script means what a C programmer would expect it to mean.
/// </para>
/// </remarks>
public static class ScriptOperatorInfo
{
    /// <summary>Gets the precedence rank of an operator. Lower binds tighter.</summary>
    /// <param name="op">The operator.</param>
    /// <returns>The rank, 1 through 15.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operator has no precedence — it is punctuation, not an operator.</exception>
    /// <remarks>The original fatals here with <c>"Script: Internal Error: Unknown opcode: %02X."</c>.</remarks>
    public static int GetPrecedence(ScriptOperator op) => op switch
    {
        ScriptOperator.UnaryPlus or ScriptOperator.UnaryMinus
            or ScriptOperator.BitwiseNot or ScriptOperator.Not => 2,
        ScriptOperator.Multiply or ScriptOperator.Divide => 3,
        ScriptOperator.Add or ScriptOperator.Subtract => 4,
        ScriptOperator.ShiftLeft or ScriptOperator.ShiftRight => 5,
        ScriptOperator.Less or ScriptOperator.Greater
            or ScriptOperator.LessOrEqual or ScriptOperator.GreaterOrEqual => 6,
        ScriptOperator.Equal or ScriptOperator.NotEqual => 7,
        ScriptOperator.BitwiseAnd => 8,
        ScriptOperator.BitwiseXor => 9,
        ScriptOperator.BitwiseOr => 10,
        ScriptOperator.LogicalAnd => 11,
        ScriptOperator.LogicalOr => 12,
        ScriptOperator.Question => 13,
        ScriptOperator.Assign => 14,
        ScriptOperator.Comma => 15,
        _ => throw new ArgumentOutOfRangeException(
            nameof(op),
            op,
            "Operator has no precedence; the original fatals with \"Unknown opcode\" here."),
    };

    /// <summary>Determines whether an operator takes a single operand.</summary>
    /// <param name="op">The operator.</param>
    /// <returns><see langword="true"/> for <c>+x</c>, <c>-x</c>, <c>~x</c> and <c>!x</c>.</returns>
    /// <remarks>A unary operator's operand hangs off its <em>right</em> child; a binary one's first operand hangs off its left.</remarks>
    public static bool IsUnary(ScriptOperator op) =>
        op is ScriptOperator.UnaryPlus
            or ScriptOperator.UnaryMinus
            or ScriptOperator.BitwiseNot
            or ScriptOperator.Not;

    /// <summary>Determines whether an operator groups left to right.</summary>
    /// <param name="op">The operator.</param>
    /// <returns><see langword="false"/> for the unary operators, assignment and the ternary; <see langword="true"/> otherwise.</returns>
    /// <remarks>
    /// The original spells this as a 0/1 added to the incoming operator's precedence before comparing,
    /// which turns the comparison into a strict one for right-associative operators. The set is exactly
    /// C's: unary, <c>=</c> and <c>?:</c> associate right.
    /// </remarks>
    public static bool IsLeftAssociative(ScriptOperator op) =>
        !(IsUnary(op) || op is ScriptOperator.Assign or ScriptOperator.Question);
}
