using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// An expression in a script written in code: a literal, a symbol, or a call whose arguments are themselves expressions.
/// </summary>
/// <remarks>
/// Level data is one deeply nested tree of <c>Task_New</c> calls, and the QVM encodes each argument as
/// a separate code block the callee jumps into on demand. Writing that by hand per test is unreadable,
/// so this builds the encoding from an expression tree instead.
/// <para>
/// A terse factory over the QSC syntax tree: <see cref="Syntax"/> is the node, and every program is compiled
/// by <see cref="QscCompiler"/>, so there is one emitter for scripts written in code and scripts written as
/// source.
/// </para>
/// </remarks>
public sealed class QvmExpression
{
    private QvmExpression(QscExpression syntax) => Syntax = syntax;

    /// <summary>Gets the syntax node this expression stands for.</summary>
    public QscExpression Syntax { get; }

    /// <summary>Creates an integer literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Int(int value) => new(new QscIntLiteral(value));

    /// <summary>Creates a floating-point literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Real(float value) => new(new QscRealLiteral(value));

    /// <summary>Creates a string literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Text(string value) => new(new QscStringLiteral(value));

    /// <summary>Creates a symbol's value: a constant or a variable the script's namespace resolves by name.</summary>
    /// <param name="name">The symbol's script name.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Symbol(string name) => new(new QscSymbol(name));

    /// <summary>Creates a call.</summary>
    /// <param name="name">The native's script name.</param>
    /// <param name="arguments">The arguments, in order.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Call(string name, params QvmExpression[] arguments) => Call(name, (IEnumerable<QvmExpression>)arguments);

    /// <summary>Creates a call whose arguments come from a sequence.</summary>
    /// <param name="name">The native's script name.</param>
    /// <param name="arguments">The arguments, in order.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Call(string name, IEnumerable<QvmExpression> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new(new QscCall(name, [.. arguments.Select(static a => a.Syntax)]));
    }

    /// <summary>Assembles a program that evaluates the given expressions in order and halts.</summary>
    /// <param name="statements">The top-level expressions.</param>
    /// <returns>The complete QVM image.</returns>
    public static byte[] BuildProgram(params QvmExpression[] statements) => BuildProgram((IEnumerable<QvmExpression>)statements);

    /// <summary>Assembles a program from a sequence of statements.</summary>
    /// <param name="statements">The top-level expressions.</param>
    /// <returns>The complete QVM image.</returns>
    public static byte[] BuildProgram(IEnumerable<QvmExpression> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        QscStatement[] body = [.. statements.Select(static s => new QscExpressionStatement(s.Syntax))];
        return QscCompiler.Compile(new QscScript(body, QvmProgram.IgiVersion));
    }

    /// <summary>Emits the expression's code into a writer, leaving its value on the stack.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void EmitTo(QvmWriter writer) => QscCompiler.EmitExpression(writer, Syntax);
}
