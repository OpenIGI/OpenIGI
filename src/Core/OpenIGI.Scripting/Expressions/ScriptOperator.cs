namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// An operator in the runtime expression language, numbered exactly as the original's tokenizer
/// numbers it.
/// </summary>
/// <remarks>
/// <para>
/// The values are not arbitrary: <c>Expr_Precedence</c> (<c>0x4BA3D0</c>) switches on them directly and
/// fatals on anything it does not know, so the numbering is part of the contract between the tokenizer
/// and the parser.
/// </para>
/// <para>
/// Ids 7 and 26 are never produced by the tokenizer. 26 has a precedence entry and 7 does not, so 26
/// is a real operator the retail scripts simply never use; both are kept out of this enum rather than
/// guessed at.
/// </para>
/// </remarks>
public enum ScriptOperator
{
    /// <summary>No operator.</summary>
    None = 0,

    /// <summary>Binary addition, <c>+</c>.</summary>
    Add = 1,

    /// <summary>Binary subtraction, <c>-</c>.</summary>
    Subtract = 2,

    /// <summary>Unary plus. The parser rewrites <see cref="Add"/> to this in prefix position.</summary>
    UnaryPlus = 3,

    /// <summary>Unary minus. The parser rewrites <see cref="Subtract"/> to this in prefix position.</summary>
    UnaryMinus = 4,

    /// <summary>Multiplication, <c>*</c>.</summary>
    Multiply = 5,

    /// <summary>Division, <c>/</c>.</summary>
    Divide = 6,

    /// <summary>Bitwise or, <c>|</c>.</summary>
    BitwiseOr = 8,

    /// <summary>Bitwise exclusive or, <c>^</c>.</summary>
    BitwiseXor = 9,

    /// <summary>Bitwise and, <c>&amp;</c>.</summary>
    BitwiseAnd = 10,

    /// <summary>Left shift, <c>&lt;&lt;</c>.</summary>
    ShiftLeft = 11,

    /// <summary>Right shift, <c>&gt;&gt;</c>.</summary>
    ShiftRight = 12,

    /// <summary>Bitwise complement, <c>~</c>.</summary>
    BitwiseNot = 13,

    /// <summary>Equality, <c>==</c>.</summary>
    Equal = 14,

    /// <summary>Inequality, <c>!=</c>.</summary>
    NotEqual = 15,

    /// <summary>Less than, <c>&lt;</c>.</summary>
    Less = 16,

    /// <summary>Greater than, <c>&gt;</c>.</summary>
    Greater = 17,

    /// <summary>Less than or equal, <c>&lt;=</c>.</summary>
    LessOrEqual = 18,

    /// <summary>Greater than or equal, <c>&gt;=</c>.</summary>
    GreaterOrEqual = 19,

    /// <summary>Logical not, <c>!</c>.</summary>
    Not = 20,

    /// <summary>Opening parenthesis, <c>(</c>.</summary>
    OpenParen = 21,

    /// <summary>Closing parenthesis, <c>)</c>.</summary>
    CloseParen = 22,

    /// <summary>Statement terminator, <c>;</c>.</summary>
    Semicolon = 23,

    /// <summary>Argument and sequence separator, <c>,</c>.</summary>
    Comma = 24,

    /// <summary>Assignment, <c>=</c>.</summary>
    Assign = 25,

    /// <summary>Logical or, <c>||</c>.</summary>
    LogicalOr = 27,

    /// <summary>Logical and, <c>&amp;&amp;</c>.</summary>
    LogicalAnd = 28,

    /// <summary>Ternary condition, <c>?</c>.</summary>
    Question = 29,

    /// <summary>Ternary separator, <c>:</c>.</summary>
    Colon = 30,

    /// <summary>Block open, <c>{</c>.</summary>
    OpenBrace = 31,

    /// <summary>Block close, <c>}</c>.</summary>
    CloseBrace = 32,
}
