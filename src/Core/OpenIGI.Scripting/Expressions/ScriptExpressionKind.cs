namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// The kind of an expression tree node, numbered as the original's node constructor numbers it.
/// </summary>
/// <remarks>
/// Values come from <c>Expr_NewNode</c> (<c>0x4BA880</c>). Kind 4 is accepted by that constructor but
/// never produced by the parser, so it has no name here.
/// </remarks>
public enum ScriptExpressionKind
{
    /// <summary>A registered constant's value.</summary>
    Constant = 1,

    /// <summary>A string literal.</summary>
    String = 2,

    /// <summary>A numeric literal.</summary>
    Number = 3,

    /// <summary>A name: a variable, a member path, or a function when it carries arguments.</summary>
    Identifier = 5,

    /// <summary>A parenthesised sub-expression.</summary>
    Group = 6,

    /// <summary>A ternary conditional.</summary>
    Conditional = 7,

    /// <summary>A unary or binary operator.</summary>
    Operator = 8,
}
