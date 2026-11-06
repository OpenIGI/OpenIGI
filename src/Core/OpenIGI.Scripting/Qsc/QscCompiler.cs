using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Scripting.Qsc;

/// <summary>
/// Compiles a QSC syntax tree to a QVM image, emitting exactly the shapes the original compiler did.
/// </summary>
/// <remarks>
/// <para>
/// The rules that make retail scripts come out byte for byte:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Integers take the shortest push: <c>PUSH0</c>, <c>PUSH1</c>, <c>PUSHB</c>, <c>PUSHW</c>, then <c>PUSH</c>.
/// </description></item>
/// <item><description>
/// Symbols and strings are interned in the order the code first mentions them, and their table indices take
/// the shortest push as well.
/// </description></item>
/// <item><description>There is no constant folding: <c>-1</c> is <c>PUSH1</c> then <c>MINUS</c>.</description></item>
/// </list>
/// <para>See <see cref="QscDecompiler"/> for the control-flow shapes.</para>
/// </remarks>
public static class QscCompiler
{
    /// <summary>Compiles a script.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The complete QVM image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is <see langword="null"/>.</exception>
    public static byte[] Compile(QscScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        QvmWriter writer = new(script.Version)
        {
            StackSizeBytes = script.RequestedStackSizeBytes,
            MaxLocals = script.RequestedMaxLocals,
            FileInfo = script.FileInfo,
        };

        EmitStatements(writer, script.Statements);
        writer.Code.Emit(QvmOpcode.Brk);
        return writer.Build();
    }

    /// <summary>Emits one expression into a program being assembled.</summary>
    /// <param name="writer">The program.</param>
    /// <param name="expression">The expression; its value is left on the stack.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void EmitExpression(QvmWriter writer, QscExpression expression)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(expression);
        Emit(writer, expression);
    }

    private static void EmitStatements(QvmWriter writer, IEnumerable<QscStatement> statements)
    {
        foreach (QscStatement statement in statements)
        {
            EmitStatement(writer, statement);
        }
    }

    private static void EmitStatement(QvmWriter writer, QscStatement statement)
    {
        QvmCodeWriter code = writer.Code;
        switch (statement)
        {
            case QscExpressionStatement expression:
                Emit(writer, expression.Expression);
                code.Emit(QvmOpcode.Pop);
                break;

            case QscIf conditional:
            {
                QvmLabel otherwise = code.CreateLabel();
                Emit(writer, conditional.Condition);
                code.Branch(QvmOpcode.Bf, otherwise);
                EmitStatements(writer, conditional.Then);
                if (conditional.Else is null)
                {
                    code.MarkLabel(otherwise);
                    break;
                }

                QvmLabel end = code.CreateLabel();
                code.Branch(QvmOpcode.Bra, end);
                code.MarkLabel(otherwise);
                EmitStatements(writer, conditional.Else);
                code.MarkLabel(end);
                break;
            }

            case QscWhile loop:
            {
                QvmLabel top = code.CreateLabel();
                QvmLabel end = code.CreateLabel();
                code.MarkLabel(top);
                Emit(writer, loop.Condition);
                code.Branch(QvmOpcode.Bf, end);
                EmitStatements(writer, loop.Body);
                code.Branch(QvmOpcode.Bra, top);
                code.MarkLabel(end);
                break;
            }

            default:
                throw new ArgumentException($"Unknown statement node {statement.GetType().Name}.", nameof(statement));
        }
    }

    private static void Emit(QvmWriter writer, QscExpression expression)
    {
        QvmCodeWriter code = writer.Code;
        switch (expression)
        {
            case QscIntLiteral literal:
                EmitInteger(code, literal.Value);
                break;
            case QscRealLiteral literal:
                code.PushF(literal.Value);
                break;
            case QscStringLiteral literal:
                writer.PushString(literal.Value);
                break;
            case QscSymbol symbol:
                writer.PushSymbol(symbol.Name);
                break;
            case QscCall call:
                EmitCall(writer, call);
                break;
            case QscUnary unary:
                Emit(writer, unary.Operand);
                code.Emit(Opcode(unary.Operator));
                break;
            case QscBinary binary:
                Emit(writer, binary.Left);
                Emit(writer, binary.Right);
                code.Emit(Opcode(binary.Operator));
                break;
            case QscAssignment assignment:
                Emit(writer, assignment.Target);
                Emit(writer, assignment.Value);
                code.Emit(QvmOpcode.Assign);
                break;
            default:
                throw new ArgumentException($"Unknown expression node {expression.GetType().Name}.", nameof(expression));
        }
    }

    private static void EmitCall(QvmWriter writer, QscCall call)
    {
        QvmCodeWriter code = writer.Code;
        QvmLabel[] blocks = new QvmLabel[call.Arguments.Count];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = code.CreateLabel();
        }

        QvmLabel after = code.CreateLabel();
        writer.PushSymbol(call.Name);
        code.Call(blocks);
        code.Branch(QvmOpcode.Bra, after);

        // The callee jumps into whichever argument block it decides to evaluate; each hands control back with BRK.
        for (int i = 0; i < blocks.Length; i++)
        {
            code.MarkLabel(blocks[i]);
            Emit(writer, call.Arguments[i]);
            code.Emit(QvmOpcode.Brk);
        }

        code.MarkLabel(after);
    }

    private static void EmitInteger(QvmCodeWriter code, int value)
    {
        switch (value)
        {
            case 0:
                code.Emit(QvmOpcode.Push0);
                break;
            case 1:
                code.Emit(QvmOpcode.Push1);
                break;
            case >= sbyte.MinValue and <= sbyte.MaxValue:
                code.PushB((sbyte)value);
                break;
            case >= short.MinValue and <= short.MaxValue:
                code.PushW((short)value);
                break;
            default:
                code.Push(value);
                break;
        }
    }

    private static QvmOpcode Opcode(QscUnaryOperator op) => op switch
    {
        QscUnaryOperator.Plus => QvmOpcode.Plus,
        QscUnaryOperator.Minus => QvmOpcode.Minus,
        QscUnaryOperator.BitwiseNot => QvmOpcode.Inv,
        QscUnaryOperator.LogicalNot => QvmOpcode.Not,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    private static QvmOpcode Opcode(QscBinaryOperator op) => op switch
    {
        QscBinaryOperator.Add => QvmOpcode.Add,
        QscBinaryOperator.Subtract => QvmOpcode.Sub,
        QscBinaryOperator.Multiply => QvmOpcode.Mul,
        QscBinaryOperator.Divide => QvmOpcode.Div,
        QscBinaryOperator.ShiftLeft => QvmOpcode.Shl,
        QscBinaryOperator.ShiftRight => QvmOpcode.Shr,
        QscBinaryOperator.BitwiseAnd => QvmOpcode.And,
        QscBinaryOperator.BitwiseOr => QvmOpcode.Or,
        QscBinaryOperator.BitwiseXor => QvmOpcode.Xor,
        QscBinaryOperator.LogicalAnd => QvmOpcode.LAnd,
        QscBinaryOperator.LogicalOr => QvmOpcode.LOr,
        QscBinaryOperator.Equal => QvmOpcode.Eq,
        QscBinaryOperator.NotEqual => QvmOpcode.Ne,
        QscBinaryOperator.Less => QvmOpcode.Lt,
        QscBinaryOperator.LessOrEqual => QvmOpcode.Le,
        QscBinaryOperator.Greater => QvmOpcode.Gt,
        QscBinaryOperator.GreaterOrEqual => QvmOpcode.Ge,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };
}
