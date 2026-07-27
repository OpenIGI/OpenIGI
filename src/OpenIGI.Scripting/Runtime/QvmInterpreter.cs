using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Scripting.Runtime;

/// <summary>
/// Executes a <see cref="QvmProgram"/>.
/// </summary>
/// <remarks>
/// <para>
/// A stack machine with a twist: a call passes <em>code offsets</em> rather than values, and the
/// native decides which of them to evaluate and how often. See
/// <see cref="QvmCallContext"/> and the original.
/// </para>
/// <para>
/// Faults never throw. Execution stops and leaves a code in <see cref="Error"/>, exactly as the
/// original does — a script error takes the engine to its message handler, not down.
/// </para>
/// <para>
/// One interpreter runs one program and may be re-run; it is not thread-safe, and neither was the
/// single-threaded engine it models.
/// </para>
/// </remarks>
public sealed class QvmInterpreter
{
    private readonly QvmProgram _program;
    private readonly NativeRegistry _natives;
    private readonly QvmSymbol?[] _symbolCache;
    private readonly int _capacity;

    private QvmValue[] _values;
    private int _depth;
    private int _rollbackDepth;
    private int _programCounter;
    private bool _halted;
    private QvmError _error;
    private int _errorOffset;
    private string _errorIdentifier = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="QvmInterpreter"/> class.</summary>
    /// <param name="program">The program to execute.</param>
    /// <param name="natives">The namespace its identifiers resolve against.</param>
    public QvmInterpreter(QvmProgram program, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(natives);

        _program = program;
        _natives = natives;
        _symbolCache = new QvmSymbol?[program.SymbolNames.Count];
        _capacity = program.MaxLocals;
        _values = new QvmValue[Math.Min(_capacity, 64)];
    }

    /// <summary>Gets the program being executed.</summary>
    public QvmProgram Program => _program;

    /// <summary>Gets the namespace identifiers resolve against.</summary>
    public NativeRegistry Natives => _natives;

    /// <summary>Gets the fault that stopped execution, or <see cref="QvmError.None"/>.</summary>
    public QvmError Error => _error;

    /// <summary>Gets the code offset of the instruction that faulted.</summary>
    public int ErrorOffset => _errorOffset;

    /// <summary>Gets the identifier that failed to resolve, for <see cref="QvmError.UnknownIdentifier"/>.</summary>
    public string ErrorIdentifier => _errorIdentifier;

    /// <summary>Gets the current program counter: a byte offset into the code section.</summary>
    public int ProgramCounter => _programCounter;

    /// <summary>Gets the number of values on the stack.</summary>
    public int StackDepth => _depth;

    /// <summary>Gets the fault message, worded as the original's error decoder words it.</summary>
    public string ErrorMessage => _error switch
    {
        QvmError.None => string.Empty,
        QvmError.StackUnderflow => "Stack underflow",
        QvmError.StackOverflow => "Stack overflow",
        QvmError.InternalError => "Internal error (read: BUG!!!).",
        QvmError.IllegalInstruction => "Illegal instruction",
        QvmError.IllegalOperand => "Illegal operand(s) for instruction",
        QvmError.UnknownIdentifier => $"Unknown identifier: {_errorIdentifier}",
        QvmError.IdentifierNotVariable => "Identifier not a variable",
        QvmError.IdentifierNotFunction => "Identifier not a function",
        QvmError.NoFunctionOnStack => "No function on stack",
        QvmError.SyntaxError => "Syntax error",
        QvmError.ArgumentOverflow => string.Create(
            CultureInfo.InvariantCulture,
            $"Function argument overflow: {FaultingArgumentCount()} arguments."),
        QvmError.DatablockNotPresent => "Datablock not present",
        QvmError.IndexOutOfRange => "Index out of range",
        _ => "Undefined error",
    };

    /// <summary>Runs the program from its entry point to its terminating <see cref="QvmOpcode.Brk"/>.</summary>
    /// <returns>The fault that stopped it, or <see cref="QvmError.None"/> when it ran to completion.</returns>
    public QvmError Run()
    {
        _programCounter = 0;
        _depth = 0;
        _rollbackDepth = 0;
        _error = QvmError.None;
        _errorOffset = 0;
        _errorIdentifier = string.Empty;
        Execute();
        return _error;
    }

    /// <summary>Reads a value from the stack without removing it, for tests and diagnostics.</summary>
    /// <param name="depthFromTop">Zero for the top of the stack.</param>
    /// <returns>The value at that depth.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no value at that depth.</exception>
    public QvmValue Peek(int depthFromTop = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depthFromTop);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(depthFromTop, _depth);
        return _values[_depth - 1 - depthFromTop];
    }

    /// <summary>Evaluates one argument block of a call in progress.</summary>
    /// <param name="offset">The code offset of the block.</param>
    /// <param name="want">The representation the caller needs.</param>
    /// <param name="value">Receives the evaluated value.</param>
    /// <returns><see langword="false"/> when the block produced nothing, or nothing of that type.</returns>
    internal bool TryEvaluateArgument(int offset, QvmValueKind want, out QvmValue value)
    {
        int savedProgramCounter = _programCounter;
        int savedDepth = _depth;

        _programCounter = offset;
        Execute();

        bool evaluated = false;
        value = default;

        if (_depth > savedDepth && _error == QvmError.None)
        {
            switch (want)
            {
                case QvmValueKind.Float:
                    evaluated = TryPopNumber(out QvmValue number);
                    value = QvmValue.FromSingle(number.ToSingle());
                    break;

                case QvmValueKind.String:
                    evaluated = TryPopText(out string text);
                    value = QvmValue.FromString(text);
                    break;

                default:
                    evaluated = TryPopNumber(out QvmValue integer);
                    value = QvmValue.FromInt32(integer.ToInt32());
                    break;
            }
        }

        // The original restores the program counter and both stack pointers, so an argument block
        // leaves nothing behind however it ended. It deliberately does not clear the halt flag here;
        // the call site does that once the native returns.
        _programCounter = savedProgramCounter;
        _depth = savedDepth;
        return evaluated;
    }

    private void Execute()
    {
        _halted = false;
        while (_error == QvmError.None && !_halted)
        {
            Step();
        }
    }

    private void Step()
    {
        ReadOnlySpan<byte> code = _program.Code.Span;
        if ((uint)_programCounter >= (uint)code.Length)
        {
            // The original would run off the end of its allocation; refusing is the only sane answer
            // and only reachable for a program whose final BRK is missing.
            SetError(QvmError.IllegalInstruction);
            return;
        }

        _rollbackDepth = _depth;
        QvmOpcode opcode = (QvmOpcode)code[_programCounter];

        // The original reads its operands unchecked and would run past a truncated code section.
        if (QvmOpcodeInfo.TryGet(opcode, out QvmOpcodeInfo info)
            && code.Length - _programCounter < info.FixedLength)
        {
            SetError(QvmError.IllegalOperand);
            return;
        }

        switch (opcode)
        {
            case QvmOpcode.Brk:
                _halted = true;
                return;

            case QvmOpcode.Nop:
                _programCounter++;
                return;

            case QvmOpcode.Push:
                PushAndAdvance(QvmValue.FromInt32(ReadInt32(code, _programCounter + 1)), 5);
                return;

            case QvmOpcode.PushB:
                PushAndAdvance(QvmValue.FromInt32((sbyte)code[_programCounter + 1]), 2);
                return;

            case QvmOpcode.PushW:
                PushAndAdvance(QvmValue.FromInt32(BinaryPrimitives.ReadInt16LittleEndian(code[(_programCounter + 1)..])), 3);
                return;

            case QvmOpcode.PushF:
                PushAndAdvance(
                    QvmValue.FromSingle(BinaryPrimitives.ReadSingleLittleEndian(code[(_programCounter + 1)..])),
                    5);
                return;

            case QvmOpcode.PushA:
                PushAndAdvance(QvmValue.FromAddress(ReadInt32(code, _programCounter + 1)), 5);
                return;

            case QvmOpcode.PushS:
            case QvmOpcode.PushI:
            {
                string text = ReadInlineString(code, _programCounter + 1);
                PushAndAdvance(
                    opcode == QvmOpcode.PushS ? QvmValue.FromString(text) : QvmValue.FromIdentifier(text),
                    text.Length + 2);
                return;
            }

            case QvmOpcode.PushSI:
            case QvmOpcode.PushSIB:
            case QvmOpcode.PushSIW:
                PushStringConstant(code, opcode);
                return;

            case QvmOpcode.PushII:
            case QvmOpcode.PushIIB:
            case QvmOpcode.PushIIW:
                PushSymbol(code, opcode);
                return;

            case QvmOpcode.Push0:
                PushAndAdvance(QvmValue.FromInt32(0), 1);
                return;

            case QvmOpcode.Push1:
                PushAndAdvance(QvmValue.FromInt32(1), 1);
                return;

            case QvmOpcode.PushM:
                PushAndAdvance(QvmValue.FromInt32(-1), 1);
                return;

            case QvmOpcode.Pop:
                if (_depth == 0)
                {
                    Fail(QvmError.StackUnderflow);
                    return;
                }

                _depth--;
                _programCounter++;
                return;

            case QvmOpcode.Ret:
            case QvmOpcode.Jsr:
                // Neither is given a handler: the original leaves them on its "illegal" default.
                SetError(QvmError.IllegalInstruction);
                return;

            case QvmOpcode.Bra:
                _programCounter += 5 + ReadInt32(code, _programCounter + 1);
                return;

            case QvmOpcode.Bf:
            case QvmOpcode.Bt:
                Branch(code, opcode);
                return;

            case QvmOpcode.Call:
                Call(code);
                return;

            case >= QvmOpcode.Add and <= QvmOpcode.Ge:
                Binary(opcode);
                return;

            case QvmOpcode.Assign:
                Assign();
                return;

            case >= QvmOpcode.Plus and <= QvmOpcode.Not:
                Unary(opcode);
                return;

            case QvmOpcode.Blk:
                _programCounter += 5 + ReadInt32(code, _programCounter + 1);
                return;

            default:
                SetError(QvmError.IllegalInstruction);
                return;
        }
    }

    private void PushStringConstant(ReadOnlySpan<byte> code, QvmOpcode opcode)
    {
        (int index, int width) = ReadIndex(code, opcode);
        if (!_program.TryGetString(index, out string value))
        {
            SetError(QvmError.IndexOutOfRange);
            return;
        }

        PushAndAdvance(QvmValue.FromString(value), width + 1);
    }

    private void PushSymbol(ReadOnlySpan<byte> code, QvmOpcode opcode)
    {
        (int index, int width) = ReadIndex(code, opcode);

        // Resolution is cached per index, as the original caches it in the two per-program symbol
        // slot arrays it allocates beside the stack.
        QvmSymbol? symbol = (uint)index < (uint)_symbolCache.Length ? _symbolCache[index] : null;
        if (symbol is null)
        {
            if (!_program.TryGetSymbolName(index, out string name))
            {
                SetError(QvmError.IndexOutOfRange);
                return;
            }

            if (!_natives.TryResolve(name, out symbol))
            {
                _errorIdentifier = name;
                SetError(QvmError.UnknownIdentifier);
                return;
            }

            _symbolCache[index] = symbol;
        }

        PushAndAdvance(QvmValue.FromSymbol(symbol), width + 1);
    }

    private void Branch(ReadOnlySpan<byte> code, QvmOpcode opcode)
    {
        int displacement = ReadInt32(code, _programCounter + 1);

        if (!TryPopNumber(out QvmValue condition))
        {
            // The original overwrites whatever error the pop left with this one.
            SetError(QvmError.BranchOperandNotNumeric);
            _programCounter += 5;
            return;
        }

        bool isTrue = condition.Kind == QvmValueKind.Float
            ? condition.ToSingle() != 0f
            : condition.Integer != 0;

        bool taken = opcode == QvmOpcode.Bt ? isTrue : !isTrue;
        _programCounter += taken ? 5 + displacement : 5;
    }

    private void Call(ReadOnlySpan<byte> code)
    {
        if (!TryPopSymbol(out QvmSymbol? symbol))
        {
            if (_error == QvmError.None)
            {
                Fail(QvmError.NoFunctionOnStack);
            }

            return;
        }

        if (symbol.Kind != QvmSymbolKind.Function)
        {
            SetError(QvmError.IdentifierNotFunction);
            return;
        }

        int callOffset = _programCounter;
        int argumentCount = ReadInt32(code, callOffset + 1);
        if (argumentCount < 0 || (5L + (4L * argumentCount)) > code.Length - callOffset)
        {
            SetError(QvmError.IllegalOperand);
            return;
        }

        QvmInstruction instruction = QvmDisassembler.DecodeAt(code, callOffset);
        int savedDepth = _depth;

        QvmValue result = symbol.Invoke(new QvmCallContext(this, symbol, instruction.ArgumentOffsets));
        if (_error != QvmError.None)
        {
            return;
        }

        // Evaluating arguments left the halt flag set and the stack wherever the native stopped
        // reading; the call site is what puts both back.
        _halted = false;
        _depth = savedDepth;
        _programCounter = callOffset + instruction.Length;

        Push(symbol.ValueType switch
        {
            QvmValueKind.Float => QvmValue.FromSingle(result.ToSingle()),
            QvmValueKind.String => QvmValue.FromString(result.ToText()),
            _ => QvmValue.FromInt32(result.ToInt32()),
        });
    }

    private void Binary(QvmOpcode opcode)
    {
        if (TryPopTwoNumbers(out QvmValue left, out QvmValue right, out bool isFloat))
        {
            if (isFloat)
            {
                BinaryFloat(opcode, left.ToSingle(), right.ToSingle());
            }
            else
            {
                BinaryInteger(opcode, left.Integer, right.Integer);
            }

            return;
        }

        BinaryText(opcode);
    }

    private void BinaryInteger(QvmOpcode opcode, int left, int right)
    {
        switch (opcode)
        {
            case QvmOpcode.Div when right == 0:
                // The original divides with IDIV and would take a processor fault.
                Fail(QvmError.InternalError);
                return;

            case QvmOpcode.Add:
                Push(QvmValue.FromInt32(unchecked(left + right)));
                break;
            case QvmOpcode.Sub:
                Push(QvmValue.FromInt32(unchecked(left - right)));
                break;
            case QvmOpcode.Mul:
                Push(QvmValue.FromInt32(unchecked(left * right)));
                break;
            case QvmOpcode.Div:
                Push(QvmValue.FromInt32(left / right));
                break;
            case QvmOpcode.Shl:
                Push(QvmValue.FromInt32(left << right));
                break;
            case QvmOpcode.Shr:
                Push(QvmValue.FromInt32(left >> right));
                break;
            case QvmOpcode.And:
                Push(QvmValue.FromInt32(left & right));
                break;
            case QvmOpcode.Or:
                Push(QvmValue.FromInt32(left | right));
                break;
            case QvmOpcode.Xor:
                Push(QvmValue.FromInt32(left ^ right));
                break;
            case QvmOpcode.LAnd:
                PushBoolean(left != 0 && right != 0);
                break;
            case QvmOpcode.LOr:
                PushBoolean(left != 0 || right != 0);
                break;
            case QvmOpcode.Eq:
                PushBoolean(left == right);
                break;
            case QvmOpcode.Ne:
                PushBoolean(left != right);
                break;
            case QvmOpcode.Lt:
                PushBoolean(left < right);
                break;
            case QvmOpcode.Le:
                PushBoolean(left <= right);
                break;
            case QvmOpcode.Gt:
                PushBoolean(left > right);
                break;
            default:
                PushBoolean(left >= right);
                break;
        }

        _programCounter++;
    }

    private void BinaryFloat(QvmOpcode opcode, float left, float right)
    {
        switch (opcode)
        {
            case QvmOpcode.Add:
                Push(QvmValue.FromSingle(left + right));
                break;
            case QvmOpcode.Sub:
                Push(QvmValue.FromSingle(left - right));
                break;
            case QvmOpcode.Mul:
                Push(QvmValue.FromSingle(left * right));
                break;
            case QvmOpcode.Div:
                Push(QvmValue.FromSingle(left / right));
                break;
            case QvmOpcode.LAnd:
                PushBoolean(left != 0f && right != 0f);
                break;
            case QvmOpcode.LOr:
                PushBoolean(left != 0f || right != 0f);
                break;
            case QvmOpcode.Eq:
                PushBoolean(left == right);
                break;
            case QvmOpcode.Ne:
                PushBoolean(left != right);
                break;
            case QvmOpcode.Lt:
                PushBoolean(left < right);
                break;
            case QvmOpcode.Le:
                PushBoolean(left <= right);
                break;
            case QvmOpcode.Gt:
                PushBoolean(left > right);
                break;
            case QvmOpcode.Ge:
                PushBoolean(left >= right);
                break;
            default:
                // SHL, SHR, AND, OR and XOR have no floating-point form.
                Fail(QvmError.IllegalOperand);
                break;
        }

        _programCounter++;
    }

    private void BinaryText(QvmOpcode opcode)
    {
        if (!TryPopText(out string right) || !TryPopText(out string left))
        {
            if (_error == QvmError.None)
            {
                Fail(QvmError.IllegalOperand);
            }

            _programCounter++;
            return;
        }

        switch (opcode)
        {
            case QvmOpcode.Add:
                Push(QvmValue.FromString(left + right));
                break;
            case QvmOpcode.Eq:
                PushBoolean(string.Equals(left, right, StringComparison.Ordinal));
                break;
            case QvmOpcode.Ne:
                PushBoolean(!string.Equals(left, right, StringComparison.Ordinal));
                break;
            case QvmOpcode.Lt:
                PushBoolean(string.CompareOrdinal(left, right) < 0);
                break;
            case QvmOpcode.Le:
                PushBoolean(string.CompareOrdinal(left, right) <= 0);
                break;
            case QvmOpcode.Gt:
                PushBoolean(string.CompareOrdinal(left, right) > 0);
                break;
            case QvmOpcode.Ge:
                PushBoolean(string.CompareOrdinal(left, right) >= 0);
                break;
            default:
                // Only concatenation and the comparisons are defined for text.
                Fail(QvmError.IllegalOperand);
                break;
        }

        _programCounter++;
    }

    private void Unary(QvmOpcode opcode)
    {
        if (!TryPopNumber(out QvmValue operand))
        {
            if (_error == QvmError.None)
            {
                Fail(QvmError.IllegalOperand);
            }

            return;
        }

        if (operand.Kind == QvmValueKind.Float)
        {
            switch (opcode)
            {
                case QvmOpcode.Plus:
                    Push(QvmValue.FromSingle(operand.ToSingle()));
                    break;
                case QvmOpcode.Minus:
                    Push(QvmValue.FromSingle(-operand.ToSingle()));
                    break;
                default:
                    // Complement and logical negation are integer-only.
                    Fail(QvmError.IllegalOperand);
                    break;
            }
        }
        else
        {
            switch (opcode)
            {
                case QvmOpcode.Plus:
                    Push(QvmValue.FromInt32(operand.Integer));
                    break;
                case QvmOpcode.Minus:
                    Push(QvmValue.FromInt32(unchecked(-operand.Integer)));
                    break;
                case QvmOpcode.Inv:
                    Push(QvmValue.FromInt32(~operand.Integer));
                    break;
                default:
                    PushBoolean(operand.Integer == 0);
                    break;
            }
        }

        _programCounter++;
    }

    private void Assign()
    {
        QvmValue value;
        if (TryPopNumber(out QvmValue number))
        {
            value = number;
        }
        else if (TryPopText(out string text))
        {
            value = QvmValue.FromString(text);
        }
        else
        {
            FailAssignment();
            return;
        }

        if (!TryPopSymbol(out QvmSymbol? target))
        {
            FailAssignment();
            return;
        }

        if (target.Kind != QvmSymbolKind.Variable)
        {
            Fail(QvmError.IdentifierNotVariable);
            return;
        }

        if (!target.Variable!.TryWrite(value, out QvmValue stored))
        {
            FailAssignment();
            return;
        }

        Push(stored);
        _programCounter++;
    }

    private void FailAssignment()
    {
        if (_error == QvmError.None)
        {
            Fail(QvmError.SyntaxError);
        }
    }

    private bool TryPopNumber(out QvmValue value)
    {
        if (_depth == 0)
        {
            SetError(QvmError.StackUnderflow);
            value = default;
            return false;
        }

        QvmValue top = _values[_depth - 1];
        if (top.IsNumber)
        {
            _depth--;
            value = top;
            return true;
        }

        value = default;
        if (!TryPopSymbol(out QvmSymbol? symbol) || !symbol.IsReadable)
        {
            return false;
        }

        QvmValue read = symbol!.Read();
        if (!read.IsNumber)
        {
            return false;
        }

        value = read;
        return true;
    }

    private bool TryPopText(out string text)
    {
        text = string.Empty;
        if (_depth == 0)
        {
            SetError(QvmError.StackUnderflow);
            return false;
        }

        QvmValue top = _values[_depth - 1];
        if (top.Kind is QvmValueKind.String or QvmValueKind.Buffer)
        {
            _depth--;
            text = top.Text;
            return true;
        }

        if (!TryPopSymbol(out QvmSymbol? symbol) || !symbol.IsReadable)
        {
            return false;
        }

        // The original reports success for any readable symbol and only fills the output when the
        // symbol really holds a string, leaving the caller's pointer untouched otherwise. Reading an
        // uninitialised pointer has no counterpart here, so a non-string symbol reads as empty.
        if (symbol.ValueType == QvmValueKind.String)
        {
            text = symbol.Read().Text;
        }

        return true;
    }

    private bool TryPopSymbol([NotNullWhen(true)] out QvmSymbol? symbol)
    {
        symbol = null;
        if (_depth == 0)
        {
            SetError(QvmError.StackUnderflow);
            return false;
        }

        QvmValue top = _values[_depth - 1];
        switch (top.Kind)
        {
            case QvmValueKind.Symbol:
                _depth--;
                symbol = top.Symbol!;
                return true;

            case QvmValueKind.Identifier:
                _depth--;
                if (_natives.TryResolve(top.Text, out symbol))
                {
                    return true;
                }

                // The original pushes the identifier back before reporting, so the faulting name
                // survives on the stack for its error handler to inspect.
                Push(top);
                _errorIdentifier = top.Text;
                SetError(QvmError.UnknownIdentifier);
                return false;

            default:
                return false;
        }
    }

    private bool TryPopTwoNumbers(out QvmValue left, out QvmValue right, out bool isFloat)
    {
        left = default;
        isFloat = false;

        if (!TryPopNumber(out right))
        {
            return false;
        }

        bool rightIsFloat = right.Kind == QvmValueKind.Float;
        if (!TryPopNumber(out left))
        {
            // The right operand goes back, in its own type.
            Push(rightIsFloat ? QvmValue.FromSingle(right.ToSingle()) : QvmValue.FromInt32(right.Integer));
            return false;
        }

        isFloat = rightIsFloat || left.Kind == QvmValueKind.Float;
        return true;
    }

    private void Push(QvmValue value)
    {
        if (_depth == _capacity)
        {
            SetError(QvmError.StackOverflow);
            return;
        }

        if (_depth == _values.Length)
        {
            Array.Resize(ref _values, Math.Min(_capacity, Math.Max(_values.Length * 2, 4)));
        }

        _values[_depth++] = value;
    }

    private void PushBoolean(bool value) => Push(QvmValue.FromInt32(value ? 1 : 0));

    private void PushAndAdvance(QvmValue value, int length)
    {
        Push(value);
        _programCounter += length;
    }

    private (int Index, int Width) ReadIndex(ReadOnlySpan<byte> code, QvmOpcode opcode) => opcode switch
    {
        QvmOpcode.PushSIB or QvmOpcode.PushIIB => (code[_programCounter + 1], 1),
        QvmOpcode.PushSIW or QvmOpcode.PushIIW => (BinaryPrimitives.ReadUInt16LittleEndian(code[(_programCounter + 1)..]), 2),
        _ => (ReadInt32(code, _programCounter + 1), 4),
    };

    private void Fail(QvmError error)
    {
        SetError(error);

        // The original rolls the stack pointers back to where the faulting instruction found them.
        _depth = _rollbackDepth;
    }

    private void SetError(QvmError error)
    {
        _error = error;
        _errorOffset = _programCounter;
    }

    private int FaultingArgumentCount()
    {
        ReadOnlySpan<byte> code = _program.Code.Span;
        return _errorOffset >= 0 && code.Length - _errorOffset >= 5
            ? ReadInt32(code, _errorOffset + 1)
            : 0;
    }

    private static int ReadInt32(ReadOnlySpan<byte> code, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(code[offset..]);

    private static string ReadInlineString(ReadOnlySpan<byte> code, int offset)
    {
        ReadOnlySpan<byte> rest = code[offset..];
        int terminator = rest.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator < 0 ? rest : rest[..terminator]);
    }
}
