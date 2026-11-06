using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Scripting.Qsc;

/// <summary>
/// Rebuilds a QSC syntax tree from a compiled QVM program.
/// </summary>
/// <remarks>
/// <para>
/// The original compiler emits a small, fixed set of shapes, and every retail script is made of them:
/// </para>
/// <list type="bullet">
/// <item><description>An expression statement is the expression followed by <c>POP</c>.</description></item>
/// <item><description>
/// A call pushes the callee's symbol, then <c>CALL argc, offsets…</c>, then a <c>BRA</c> over the argument
/// blocks, each of which is an expression ending in <c>BRK</c>.
/// </description></item>
/// <item><description>
/// <c>if</c> is the condition, <c>BF</c> past the then-block, and, when there is an <c>else</c>, a forward
/// <c>BRA</c> at the end of the then-block over the else-block. <c>while</c> is the same with a backward
/// <c>BRA</c> to the condition instead.
/// </description></item>
/// <item><description>The program ends with one <c>BRK</c>.</description></item>
/// </list>
/// <para>
/// Anything else is reported as a <see cref="QscDecompileException"/> naming the offset, rather than guessed
/// at: a tree that does not compile back to the same bytes would be worse than no tree.
/// </para>
/// </remarks>
public static class QscDecompiler
{
    /// <summary>Decompiles a QVM image.</summary>
    /// <param name="image">The complete QVM file.</param>
    /// <returns>The script.</returns>
    /// <exception cref="QvmFormatException">The image is not a valid QVM program.</exception>
    /// <exception cref="QscDecompileException">The code holds a shape the original compiler never emits.</exception>
    public static QscScript Decompile(ReadOnlyMemory<byte> image) => Decompile(QvmProgram.Read(image));

    /// <summary>Decompiles a loaded QVM program.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="program"/> is <see langword="null"/>.</exception>
    /// <exception cref="QscDecompileException">The code holds a shape the original compiler never emits.</exception>
    public static QscScript Decompile(QvmProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        Reader reader = new(program);
        List<QscStatement> statements = reader.ReadProgram();
        return new QscScript(statements, program.Version)
        {
            RequestedStackSizeBytes = program.RequestedStackSizeBytes,
            RequestedMaxLocals = program.RequestedMaxLocals,
            FileInfo = program.FileInfo,
        };
    }

    private sealed class Reader
    {
        private readonly QvmProgram _program;
        private readonly QvmInstruction[] _instructions;
        private readonly Dictionary<int, int> _indexByOffset = [];
        private readonly int _codeLength;

        public Reader(QvmProgram program)
        {
            _program = program;
            _instructions = [.. QvmDisassembler.Decode(program)];
            _codeLength = program.Code.Length;
            for (int i = 0; i < _instructions.Length; i++)
            {
                _indexByOffset[_instructions[i].Offset] = i;
            }
        }

        public List<QscStatement> ReadProgram()
        {
            if (_instructions.Length == 0 || _instructions[^1].Opcode != QvmOpcode.Brk)
            {
                throw new QscDecompileException(_codeLength, "the program does not end with BRK");
            }

            return ReadStatements(0, _instructions[^1].Offset);
        }

        private List<QscStatement> ReadStatements(int start, int end)
        {
            List<QscStatement> statements = [];
            Stack<(QscExpression Expression, int Start)> stack = new();
            int offset = start;

            while (offset < end)
            {
                QvmInstruction instruction = At(offset);
                switch (instruction.Opcode)
                {
                    case QvmOpcode.Pop:
                        statements.Add(new QscExpressionStatement(PopOnly(stack, offset)));
                        offset = instruction.End();
                        break;

                    case QvmOpcode.Bf:
                        offset = ReadConditional(instruction, PopEntry(stack, offset), end, statements);
                        RequireEmpty(stack, offset);
                        break;

                    default:
                        offset = ReadOperation(instruction, stack, end);
                        break;
                }
            }

            if (offset != end)
            {
                throw new QscDecompileException(offset, "a statement runs past the end of its block");
            }

            RequireEmpty(stack, end);
            return statements;
        }

        private int ReadConditional(
            QvmInstruction branch,
            (QscExpression Expression, int Start) condition,
            int end,
            List<QscStatement> statements)
        {
            int bodyStart = branch.End();
            int target = BranchTarget(branch);
            if (target < bodyStart || target > end)
            {
                throw new QscDecompileException(branch.Offset, "a BF leaves its block");
            }

            QvmInstruction? last = target > bodyStart ? Before(target) : null;
            if (last is { Opcode: QvmOpcode.Bra } jump)
            {
                int jumpTarget = BranchTarget(jump);
                if (jumpTarget == condition.Start)
                {
                    statements.Add(new QscWhile(condition.Expression, ReadStatements(bodyStart, jump.Offset)));
                    return target;
                }

                if (jumpTarget >= target && jumpTarget <= end)
                {
                    List<QscStatement> then = ReadStatements(bodyStart, jump.Offset);
                    List<QscStatement> @else = ReadStatements(target, jumpTarget);
                    statements.Add(new QscIf(condition.Expression, then, @else));
                    return jumpTarget;
                }
            }

            statements.Add(new QscIf(condition.Expression, ReadStatements(bodyStart, target), null));
            return target;
        }

        private QscExpression ReadArgument(int start, int end)
        {
            Stack<(QscExpression Expression, int Start)> stack = new();
            int offset = start;
            while (true)
            {
                if (offset >= end)
                {
                    throw new QscDecompileException(offset, "a call argument does not end with BRK");
                }

                QvmInstruction instruction = At(offset);
                if (instruction.Opcode == QvmOpcode.Brk)
                {
                    if (instruction.End() != end)
                    {
                        throw new QscDecompileException(offset, "a call argument block holds more than one expression");
                    }

                    QscExpression argument = PopOnly(stack, offset);
                    return argument;
                }

                offset = ReadOperation(instruction, stack, end);
            }
        }

        private int ReadOperation(QvmInstruction instruction, Stack<(QscExpression Expression, int Start)> stack, int end)
        {
            int start = instruction.Offset;
            switch (instruction.Opcode)
            {
                case QvmOpcode.Push0:
                    stack.Push((new QscIntLiteral(0), start));
                    break;
                case QvmOpcode.Push1:
                    stack.Push((new QscIntLiteral(1), start));
                    break;
                case QvmOpcode.PushB or QvmOpcode.PushW or QvmOpcode.Push:
                    stack.Push((new QscIntLiteral(instruction.Integer), start));
                    break;
                case QvmOpcode.PushF:
                    stack.Push((new QscRealLiteral(instruction.Single), start));
                    break;
                case QvmOpcode.PushSI or QvmOpcode.PushSIB or QvmOpcode.PushSIW:
                    stack.Push((new QscStringLiteral(TableEntry(_program.Strings, instruction, "string")), start));
                    break;
                case QvmOpcode.PushII or QvmOpcode.PushIIB or QvmOpcode.PushIIW:
                    stack.Push((new QscSymbol(TableEntry(_program.SymbolNames, instruction, "symbol")), start));
                    break;
                case QvmOpcode.Call:
                    return ReadCall(instruction, stack, end);
                case QvmOpcode.Assign:
                {
                    (QscExpression value, _) = PopEntry(stack, start);
                    (QscExpression target, int targetStart) = PopEntry(stack, start);
                    stack.Push((new QscAssignment(target, value), targetStart));
                    break;
                }

                default:
                    if (UnaryOperator(instruction.Opcode) is { } unary)
                    {
                        (QscExpression operand, int operandStart) = PopEntry(stack, start);
                        stack.Push((new QscUnary(unary, operand), operandStart));
                        break;
                    }

                    if (BinaryOperator(instruction.Opcode) is { } binary)
                    {
                        (QscExpression right, _) = PopEntry(stack, start);
                        (QscExpression left, int leftStart) = PopEntry(stack, start);
                        stack.Push((new QscBinary(binary, left, right), leftStart));
                        break;
                    }

                    throw new QscDecompileException(start, $"{instruction.Opcode} is not part of any shape the compiler emits");
            }

            return instruction.End();
        }

        private int ReadCall(QvmInstruction call, Stack<(QscExpression Expression, int Start)> stack, int end)
        {
            (QscExpression callee, int calleeStart) = PopEntry(stack, call.Offset);
            if (callee is not QscSymbol symbol)
            {
                throw new QscDecompileException(call.Offset, "a CALL whose callee is not a symbol");
            }

            QvmInstruction skip = At(call.End());
            if (skip.Opcode != QvmOpcode.Bra)
            {
                throw new QscDecompileException(skip.Offset, "a CALL is not followed by a BRA over its arguments");
            }

            int after = BranchTarget(skip);
            if (after < skip.End() || after > end)
            {
                throw new QscDecompileException(skip.Offset, "a call's BRA leaves the enclosing block");
            }

            IReadOnlyList<int> offsets = call.ArgumentOffsets;
            QscExpression[] arguments = new QscExpression[offsets.Count];
            int expected = skip.End();
            for (int i = 0; i < offsets.Count; i++)
            {
                if (offsets[i] != expected)
                {
                    throw new QscDecompileException(call.Offset, $"argument {i} is not where the previous one ended");
                }

                int argumentEnd = i + 1 < offsets.Count ? offsets[i + 1] : after;
                arguments[i] = ReadArgument(offsets[i], argumentEnd);
                expected = argumentEnd;
            }

            if (expected != after)
            {
                throw new QscDecompileException(skip.Offset, "a call's BRA does not land just past its arguments");
            }

            stack.Push((new QscCall(symbol.Name, arguments), calleeStart));
            return after;
        }

        private QvmInstruction At(int offset) =>
            _indexByOffset.TryGetValue(offset, out int index)
                ? _instructions[index]
                : throw new QscDecompileException(offset, "control reaches an offset that is not an instruction boundary");

        private QvmInstruction Before(int offset) =>
            _indexByOffset.TryGetValue(offset, out int index) && index > 0
                ? _instructions[index - 1]
                : throw new QscDecompileException(offset, "a branch target is not an instruction boundary");

        private static int BranchTarget(QvmInstruction branch) => branch.Offset + branch.Length + branch.Integer;

        private static string TableEntry(IReadOnlyList<string?> table, QvmInstruction instruction, string what)
        {
            int index = instruction.Integer;
            return index >= 0 && index < table.Count && table[index] is { } value
                ? value
                : throw new QscDecompileException(instruction.Offset, $"{what} index {index} is outside the table");
        }

        private static (QscExpression Expression, int Start) PopEntry(
            Stack<(QscExpression Expression, int Start)> stack,
            int offset) =>
            stack.Count > 0 ? stack.Pop() : throw new QscDecompileException(offset, "the operand stack underflows");

        private static QscExpression PopOnly(Stack<(QscExpression Expression, int Start)> stack, int offset)
        {
            QscExpression expression = PopEntry(stack, offset).Expression;
            RequireEmpty(stack, offset);
            return expression;
        }

        private static void RequireEmpty(Stack<(QscExpression Expression, int Start)> stack, int offset)
        {
            if (stack.Count != 0)
            {
                throw new QscDecompileException(offset, "a value is left on the stack between statements");
            }
        }
    }

    private static int End(this QvmInstruction instruction) => instruction.Offset + instruction.Length;

    internal static QscUnaryOperator? UnaryOperator(QvmOpcode opcode) => opcode switch
    {
        QvmOpcode.Plus => QscUnaryOperator.Plus,
        QvmOpcode.Minus => QscUnaryOperator.Minus,
        QvmOpcode.Inv => QscUnaryOperator.BitwiseNot,
        QvmOpcode.Not => QscUnaryOperator.LogicalNot,
        _ => null,
    };

    internal static QscBinaryOperator? BinaryOperator(QvmOpcode opcode) => opcode switch
    {
        QvmOpcode.Add => QscBinaryOperator.Add,
        QvmOpcode.Sub => QscBinaryOperator.Subtract,
        QvmOpcode.Mul => QscBinaryOperator.Multiply,
        QvmOpcode.Div => QscBinaryOperator.Divide,
        QvmOpcode.Shl => QscBinaryOperator.ShiftLeft,
        QvmOpcode.Shr => QscBinaryOperator.ShiftRight,
        QvmOpcode.And => QscBinaryOperator.BitwiseAnd,
        QvmOpcode.Or => QscBinaryOperator.BitwiseOr,
        QvmOpcode.Xor => QscBinaryOperator.BitwiseXor,
        QvmOpcode.LAnd => QscBinaryOperator.LogicalAnd,
        QvmOpcode.LOr => QscBinaryOperator.LogicalOr,
        QvmOpcode.Eq => QscBinaryOperator.Equal,
        QvmOpcode.Ne => QscBinaryOperator.NotEqual,
        QvmOpcode.Lt => QscBinaryOperator.Less,
        QvmOpcode.Le => QscBinaryOperator.LessOrEqual,
        QvmOpcode.Gt => QscBinaryOperator.Greater,
        QvmOpcode.Ge => QscBinaryOperator.GreaterOrEqual,
        _ => null,
    };
}
