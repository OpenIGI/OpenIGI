using OpenIGI.Scripting.Qvm;

namespace OpenIGI.TestSupport;

/// <summary>
/// An expression in a synthetic level script: a literal, or a call whose arguments are themselves
/// expressions.
/// </summary>
/// <remarks>
/// Level data is one deeply nested tree of <c>Task_New</c> calls, and the QVM encodes each argument as
/// a separate code block the callee jumps into on demand. Writing that by hand per test is unreadable,
/// so this builds the encoding from an expression tree instead.
/// </remarks>
public abstract class QvmExpression
{
    /// <summary>Creates an integer literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Int(int value) => new IntLiteral(value);

    /// <summary>Creates a floating-point literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Real(float value) => new RealLiteral(value);

    /// <summary>Creates a string literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Text(string value) => new TextLiteral(value);

    /// <summary>Creates a call.</summary>
    /// <param name="name">The native's script name.</param>
    /// <param name="arguments">The arguments, in order.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Call(string name, params QvmExpression[] arguments) => new CallExpression(name, arguments);

    /// <summary>Creates a call whose arguments come from a sequence.</summary>
    /// <param name="name">The native's script name.</param>
    /// <param name="arguments">The arguments, in order.</param>
    /// <returns>The expression.</returns>
    public static QvmExpression Call(string name, IEnumerable<QvmExpression> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new CallExpression(name, [.. arguments]);
    }

    /// <summary>Assembles a program that evaluates the given expressions in order and halts.</summary>
    /// <param name="statements">The top-level expressions.</param>
    /// <returns>The complete QVM image.</returns>
    public static byte[] BuildProgram(params QvmExpression[] statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        QvmWriter writer = new();
        foreach (QvmExpression statement in statements)
        {
            statement.Emit(writer);

            // Each statement leaves its result behind; drop it so the stack does not grow without bound.
            writer.Code.Emit(QvmOpcode.Pop);
        }

        writer.Code.Emit(QvmOpcode.Brk);
        return writer.Build();
    }

    /// <summary>Assembles a program from a sequence of statements.</summary>
    /// <param name="statements">The top-level expressions.</param>
    /// <returns>The complete QVM image.</returns>
    public static byte[] BuildProgram(IEnumerable<QvmExpression> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        return BuildProgram([.. statements]);
    }

    internal abstract void Emit(QvmWriter writer);

    private sealed class IntLiteral(int value) : QvmExpression
    {
        internal override void Emit(QvmWriter writer) => writer.Code.Push(value);
    }

    private sealed class RealLiteral(float value) : QvmExpression
    {
        internal override void Emit(QvmWriter writer) => writer.Code.PushF(value);
    }

    private sealed class TextLiteral(string value) : QvmExpression
    {
        internal override void Emit(QvmWriter writer) => writer.PushString(value);
    }

    private sealed class CallExpression(string name, QvmExpression[] arguments) : QvmExpression
    {
        internal override void Emit(QvmWriter writer)
        {
            QvmLabel[] blocks = new QvmLabel[arguments.Length];
            for (int i = 0; i < blocks.Length; i++)
            {
                blocks[i] = writer.Code.CreateLabel();
            }

            QvmLabel after = writer.Code.CreateLabel();

            writer.PushSymbol(name);
            writer.Code.Call(blocks);
            writer.Code.Branch(QvmOpcode.Bra, after);

            // Arguments live as separate blocks after the call site; the callee jumps into whichever it
            // decides to evaluate, and each returns control with a BRK.
            for (int i = 0; i < arguments.Length; i++)
            {
                writer.Code.MarkLabel(blocks[i]);
                arguments[i].Emit(writer);
                writer.Code.Emit(QvmOpcode.Brk);
            }

            writer.Code.MarkLabel(after);
        }
    }
}
