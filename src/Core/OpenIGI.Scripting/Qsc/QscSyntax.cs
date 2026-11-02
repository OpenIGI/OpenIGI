using System.Collections.ObjectModel;

namespace OpenIGI.Scripting.Qsc;

/// <summary>A binary operator of the QSC script language, one per QVM operator opcode.</summary>
public enum QscBinaryOperator
{
    /// <summary><c>+</c>, opcode <c>ADD</c>.</summary>
    Add,

    /// <summary><c>-</c>, opcode <c>SUB</c>.</summary>
    Subtract,

    /// <summary><c>*</c>, opcode <c>MUL</c>.</summary>
    Multiply,

    /// <summary><c>/</c>, opcode <c>DIV</c>.</summary>
    Divide,

    /// <summary><c>&lt;&lt;</c>, opcode <c>SHL</c>.</summary>
    ShiftLeft,

    /// <summary><c>&gt;&gt;</c>, opcode <c>SHR</c>.</summary>
    ShiftRight,

    /// <summary><c>&amp;</c>, opcode <c>AND</c>.</summary>
    BitwiseAnd,

    /// <summary><c>|</c>, opcode <c>OR</c>.</summary>
    BitwiseOr,

    /// <summary><c>^</c>, opcode <c>XOR</c>.</summary>
    BitwiseXor,

    /// <summary><c>&amp;&amp;</c>, opcode <c>LAND</c>. Both sides are always evaluated: the VM has no short circuit.</summary>
    LogicalAnd,

    /// <summary><c>||</c>, opcode <c>LOR</c>. Both sides are always evaluated: the VM has no short circuit.</summary>
    LogicalOr,

    /// <summary><c>==</c>, opcode <c>EQ</c>.</summary>
    Equal,

    /// <summary><c>!=</c>, opcode <c>NE</c>.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>, opcode <c>LT</c>.</summary>
    Less,

    /// <summary><c>&lt;=</c>, opcode <c>LE</c>.</summary>
    LessOrEqual,

    /// <summary><c>&gt;</c>, opcode <c>GT</c>.</summary>
    Greater,

    /// <summary><c>&gt;=</c>, opcode <c>GE</c>.</summary>
    GreaterOrEqual,
}

/// <summary>A unary operator of the QSC script language.</summary>
public enum QscUnaryOperator
{
    /// <summary>Prefix <c>+</c>, opcode <c>PLUS</c>.</summary>
    Plus,

    /// <summary>Prefix <c>-</c>, opcode <c>MINUS</c>. A negative literal is this applied to a positive one.</summary>
    Minus,

    /// <summary>Prefix <c>~</c>, opcode <c>INV</c>.</summary>
    BitwiseNot,

    /// <summary>Prefix <c>!</c>, opcode <c>NOT</c>.</summary>
    LogicalNot,
}

/// <summary>A node of a QSC script's syntax tree.</summary>
/// <remarks>
/// The tree is the one representation every script tool shares: the decompiler builds it from a QVM image,
/// the parser from QSC source, and the compiler and printer turn it back into either. It records nothing an
/// image does not carry, so a retail script survives image → tree → image byte for byte.
/// </remarks>
public abstract class QscNode
{
    private protected QscNode()
    {
    }
}

/// <summary>A QSC expression.</summary>
public abstract class QscExpression : QscNode
{
    private protected QscExpression()
    {
    }
}

/// <summary>An integer literal.</summary>
/// <param name="value">The value; the compiler picks the shortest push that holds it.</param>
public sealed class QscIntLiteral(int value) : QscExpression
{
    /// <summary>Gets the value.</summary>
    public int Value { get; } = value;
}

/// <summary>A real (32-bit float) literal, written with a decimal point in source.</summary>
/// <param name="value">The value.</param>
public sealed class QscRealLiteral(float value) : QscExpression
{
    /// <summary>Gets the value.</summary>
    public float Value { get; } = value;
}

/// <summary>A string literal, kept in the program's string table.</summary>
/// <param name="value">The text.</param>
public sealed class QscStringLiteral(string value) : QscExpression
{
    /// <summary>Gets the text.</summary>
    public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
}

/// <summary>A name resolved against the script namespace when the program runs: a constant, variable or field.</summary>
/// <param name="name">The name, which may contain dots (<c>HumanSoldier_404.isDead</c>).</param>
public sealed class QscSymbol(string name) : QscExpression
{
    /// <summary>Gets the name.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
}

/// <summary>A call to a native function.</summary>
/// <param name="name">The function's name in the script namespace.</param>
/// <param name="arguments">The arguments, which the callee evaluates lazily and in any order it likes.</param>
public sealed class QscCall(string name, IList<QscExpression> arguments) : QscExpression
{
    /// <summary>Gets the function's name.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>Gets the arguments.</summary>
    public ReadOnlyCollection<QscExpression> Arguments { get; } =
        new([.. arguments ?? throw new ArgumentNullException(nameof(arguments))]);
}

/// <summary>A unary operation.</summary>
/// <param name="operator">The operator.</param>
/// <param name="operand">The operand.</param>
public sealed class QscUnary(QscUnaryOperator @operator, QscExpression operand) : QscExpression
{
    /// <summary>Gets the operator.</summary>
    public QscUnaryOperator Operator { get; } = @operator;

    /// <summary>Gets the operand.</summary>
    public QscExpression Operand { get; } = operand ?? throw new ArgumentNullException(nameof(operand));
}

/// <summary>A binary operation.</summary>
/// <param name="operator">The operator.</param>
/// <param name="left">The left operand, evaluated first.</param>
/// <param name="right">The right operand.</param>
public sealed class QscBinary(QscBinaryOperator @operator, QscExpression left, QscExpression right) : QscExpression
{
    /// <summary>Gets the operator.</summary>
    public QscBinaryOperator Operator { get; } = @operator;

    /// <summary>Gets the left operand.</summary>
    public QscExpression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));

    /// <summary>Gets the right operand.</summary>
    public QscExpression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));
}

/// <summary>An assignment, whose value is the value assigned.</summary>
/// <param name="target">What is assigned to; the VM requires a variable.</param>
/// <param name="value">The value.</param>
public sealed class QscAssignment(QscExpression target, QscExpression value) : QscExpression
{
    /// <summary>Gets what is assigned to.</summary>
    public QscExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));

    /// <summary>Gets the value.</summary>
    public QscExpression Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
}

/// <summary>A QSC statement.</summary>
public abstract class QscStatement : QscNode
{
    private protected QscStatement()
    {
    }
}

/// <summary>An expression evaluated for its effect, its result discarded.</summary>
/// <param name="expression">The expression.</param>
public sealed class QscExpressionStatement(QscExpression expression) : QscStatement
{
    /// <summary>Gets the expression.</summary>
    public QscExpression Expression { get; } = expression ?? throw new ArgumentNullException(nameof(expression));
}

/// <summary>An <c>if</c>, with or without an <c>else</c>.</summary>
/// <param name="condition">The condition.</param>
/// <param name="then">The statements run when it holds.</param>
/// <param name="else">The statements run when it does not, or <see langword="null"/> for no <c>else</c>.</param>
/// <remarks>
/// An empty <c>else { }</c> is not the same program as no <c>else</c>: it still compiles to a jump, so the tree
/// keeps the difference.
/// </remarks>
public sealed class QscIf(QscExpression condition, IList<QscStatement> then, IList<QscStatement>? @else) : QscStatement
{
    /// <summary>Gets the condition.</summary>
    public QscExpression Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));

    /// <summary>Gets the statements run when the condition holds.</summary>
    public ReadOnlyCollection<QscStatement> Then { get; } =
        new([.. then ?? throw new ArgumentNullException(nameof(then))]);

    /// <summary>Gets the statements run when it does not, or <see langword="null"/> when there is no <c>else</c>.</summary>
    public ReadOnlyCollection<QscStatement>? Else { get; } = @else is null ? null : new([.. @else]);
}

/// <summary>A <c>while</c> loop.</summary>
/// <param name="condition">The condition, tested before each pass.</param>
/// <param name="body">The loop body.</param>
public sealed class QscWhile(QscExpression condition, IList<QscStatement> body) : QscStatement
{
    /// <summary>Gets the condition.</summary>
    public QscExpression Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));

    /// <summary>Gets the loop body.</summary>
    public ReadOnlyCollection<QscStatement> Body { get; } =
        new([.. body ?? throw new ArgumentNullException(nameof(body))]);
}

/// <summary>A whole script: its statements and the image settings that are not part of the source.</summary>
/// <param name="statements">The top-level statements, in order.</param>
/// <param name="version">The bytecode version: 5 for I.G.I., 7 for I.G.I. 2.</param>
public sealed class QscScript(IList<QscStatement> statements, int version) : QscNode
{
    /// <summary>Gets the top-level statements.</summary>
    public ReadOnlyCollection<QscStatement> Statements { get; } =
        new([.. statements ?? throw new ArgumentNullException(nameof(statements))]);

    /// <summary>Gets the bytecode version the script compiles to.</summary>
    public int Version { get; } = version;

    /// <summary>Gets the stack size the image header asks for; zero asks for the VM default.</summary>
    public int RequestedStackSizeBytes { get; init; }

    /// <summary>Gets the local-slot count the image header asks for; zero asks for the VM default.</summary>
    public int RequestedMaxLocals { get; init; }

    /// <summary>Gets the version 7 file-info block carried after the code, or empty for none.</summary>
    public ReadOnlyMemory<byte> FileInfo { get; init; }
}
