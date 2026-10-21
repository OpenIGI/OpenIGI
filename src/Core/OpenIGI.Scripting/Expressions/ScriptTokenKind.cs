namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// The class the tokenizer assigns to a token, numbered as the original numbers it.
/// </summary>
/// <remarks>
/// The parser switches on this before it looks at the operator, so the class decides whether a token
/// is an operand, an operator, or something that ends the expression. Values come from
/// <c>Expr_NextToken</c> (<c>0x4BFC70</c>).
/// </remarks>
public enum ScriptTokenKind
{
    /// <summary>Punctuation that terminates an operand: <c>)</c>, <c>;</c>, <c>{</c>, <c>}</c>.</summary>
    Terminator = 0,

    /// <summary>A binary operator, or a <c>+</c>/<c>-</c> that may still turn out to be unary.</summary>
    BinaryOperator = 1,

    /// <summary>A strictly unary operator: <c>!</c> or <c>~</c>.</summary>
    UnaryOperator = 2,

    /// <summary>A ternary token: <c>?</c> or <c>:</c>.</summary>
    Ternary = 3,

    /// <summary>A quoted string literal.</summary>
    String = 4,

    /// <summary>An identifier that did not resolve to a registered constant.</summary>
    Identifier = 5,

    /// <summary>A numeric literal, parsed as a <see cref="double"/>.</summary>
    Number = 6,

    /// <summary>An identifier that resolved to a registered constant; its value is carried on the token.</summary>
    Constant = 7,

    /// <summary>End of input.</summary>
    End = 8,
}
