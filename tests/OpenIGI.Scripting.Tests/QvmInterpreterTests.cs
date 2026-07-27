using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class QvmInterpreterTests
{
    [Theory]
    [InlineData(QvmOpcode.Add, 7, 3, 10)]
    [InlineData(QvmOpcode.Sub, 7, 3, 4)]
    [InlineData(QvmOpcode.Mul, 7, 3, 21)]
    [InlineData(QvmOpcode.Div, 7, 3, 2)]
    [InlineData(QvmOpcode.Div, -7, 3, -2)]
    [InlineData(QvmOpcode.Shl, 3, 4, 48)]
    [InlineData(QvmOpcode.Shr, -16, 2, -4)]
    [InlineData(QvmOpcode.And, 12, 10, 8)]
    [InlineData(QvmOpcode.Or, 12, 10, 14)]
    [InlineData(QvmOpcode.Xor, 12, 10, 6)]
    [InlineData(QvmOpcode.LAnd, 2, 0, 0)]
    [InlineData(QvmOpcode.LOr, 0, 5, 1)]
    [InlineData(QvmOpcode.Lt, -1, 1, 1)]
    [InlineData(QvmOpcode.Ge, -1, 1, 0)]
    public void IntegerArithmeticMatchesTheOriginalsOperandOrder(QvmOpcode opcode, int left, int right, int expected)
    {
        QvmValue result = RunExpression(code => code.Push(left).Push(right).Emit(opcode));

        Assert.Equal(QvmValueKind.Integer, result.Kind);
        Assert.Equal(expected, result.Integer);
    }

    [Fact]
    public void MixingAnIntegerAndAFloatTakesTheFloatingPointPath()
    {
        QvmValue result = RunExpression(code => code.Push(7).PushF(2f).Emit(QvmOpcode.Div));

        Assert.Equal(QvmValueKind.Float, result.Kind);
        Assert.Equal(3.5f, result.ToSingle());
    }

    [Fact]
    public void ComparingFloatsYieldsAnInteger()
    {
        QvmValue result = RunExpression(code => code.PushF(1.5f).PushF(2.5f).Emit(QvmOpcode.Lt));

        Assert.Equal(QvmValueKind.Integer, result.Kind);
        Assert.Equal(1, result.Integer);
    }

    [Fact]
    public void BitwiseOperatorsRejectFloats()
    {
        QvmInterpreter interpreter = Build(code => code.PushF(1f).PushF(2f).Emit(QvmOpcode.And).Emit(QvmOpcode.Brk));

        Assert.Equal(QvmError.IllegalOperand, interpreter.Run());
        Assert.Equal("Illegal operand(s) for instruction", interpreter.ErrorMessage);
    }

    [Fact]
    public void AddingStringsConcatenatesThem()
    {
        QvmValue result = RunExpression(code => code.PushS("I'm ").PushS("Going In").Emit(QvmOpcode.Add));

        Assert.Equal(QvmValueKind.String, result.Kind);
        Assert.Equal("I'm Going In", result.Text);
    }

    [Theory]
    [InlineData(QvmOpcode.Eq, "a", "a", 1)]
    [InlineData(QvmOpcode.Ne, "a", "b", 1)]
    [InlineData(QvmOpcode.Lt, "a", "b", 1)]
    [InlineData(QvmOpcode.Gt, "a", "b", 0)]
    public void ComparingStringsMatchesStrcmp(QvmOpcode opcode, string left, string right, int expected)
    {
        QvmValue result = RunExpression(code => code.PushS(left).PushS(right).Emit(opcode));

        Assert.Equal(expected, result.Integer);
    }

    [Fact]
    public void SubtractingStringsIsAnIllegalOperand()
    {
        QvmInterpreter interpreter = Build(code =>
            code.PushS("a").PushS("b").Emit(QvmOpcode.Sub).Emit(QvmOpcode.Brk));

        Assert.Equal(QvmError.IllegalOperand, interpreter.Run());
    }

    [Theory]
    [InlineData(QvmOpcode.Minus, 5, -5)]
    [InlineData(QvmOpcode.Inv, 5, -6)]
    [InlineData(QvmOpcode.Not, 0, 1)]
    [InlineData(QvmOpcode.Not, 5, 0)]
    [InlineData(QvmOpcode.Plus, 5, 5)]
    public void UnaryOperatorsFollowC(QvmOpcode opcode, int operand, int expected)
    {
        QvmValue result = RunExpression(code => code.Push(operand).Emit(opcode));

        Assert.Equal(expected, result.Integer);
    }

    [Fact]
    public void DividingByZeroStopsInsteadOfFaulting()
    {
        QvmInterpreter interpreter = Build(code => code.Push(1).Push(0).Emit(QvmOpcode.Div).Emit(QvmOpcode.Brk));

        Assert.Equal(QvmError.InternalError, interpreter.Run());
    }

    [Fact]
    public void ReturnAndJumpToSubroutineAreIllegalInstructions()
    {
        Assert.Equal(QvmError.IllegalInstruction, Build(code => code.Emit(QvmOpcode.Ret)).Run());
        Assert.Equal(QvmError.IllegalInstruction, Build(code => code.Emit(QvmOpcode.Jsr)).Run());
    }

    [Fact]
    public void BranchIfFalseTakesTheBranchOnZero()
    {
        Assert.Equal(1, RunBranch(QvmOpcode.Bf, 0).Integer);
        Assert.Equal(2, RunBranch(QvmOpcode.Bf, 1).Integer);
        Assert.Equal(2, RunBranch(QvmOpcode.Bt, 0).Integer);
        Assert.Equal(1, RunBranch(QvmOpcode.Bt, 1).Integer);
    }

    [Fact]
    public void BranchingOnSomethingThatIsNotANumberFaults()
    {
        QvmCodeWriter writer = new();
        QvmLabel target = writer.CreateLabel();
        writer.PushS("not a number");
        writer.Branch(QvmOpcode.Bf, target);
        writer.MarkLabel(target).Emit(QvmOpcode.Brk);

        QvmInterpreter interpreter = Build(writer);

        Assert.Equal(QvmError.BranchOperandNotNumeric, interpreter.Run());
        Assert.Equal("Undefined error", interpreter.ErrorMessage);
    }

    [Fact]
    public void BlockSkipsItsContent()
    {
        QvmValue result = RunExpression(code => code.Push(9).Blk(2).Raw((byte)QvmOpcode.Push0, (byte)QvmOpcode.Push0));

        Assert.Equal(9, result.Integer);
    }

    [Fact]
    public void AnUnknownIdentifierNamesItselfInTheMessage()
    {
        QvmWriter writer = new();
        writer.PushSymbol("Task_New");
        writer.Code.Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), new NativeRegistry());

        Assert.Equal(QvmError.UnknownIdentifier, interpreter.Run());
        Assert.Equal("Task_New", interpreter.ErrorIdentifier);
        Assert.Equal("Unknown identifier: Task_New", interpreter.ErrorMessage);
    }

    [Fact]
    public void CallingSomethingThatIsNotAFunctionFaults()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("KEY_LEFT", 203);

        QvmWriter writer = new();
        writer.PushSymbol("KEY_LEFT");
        writer.Code.Call().Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), natives);

        Assert.Equal(QvmError.IdentifierNotFunction, interpreter.Run());
    }

    [Fact]
    public void CallingWithNoSymbolOnTheStackFaults()
    {
        QvmWriter writer = new();
        writer.Code.Push(1).Call().Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), new NativeRegistry());

        Assert.Equal(QvmError.NoFunctionOnStack, interpreter.Run());
    }

    [Fact]
    public void ANativeReadsItsArgumentsOnDemand()
    {
        List<int> evaluated = [];
        NativeRegistry natives = new();
        natives.RegisterFunction("Note", call =>
        {
            evaluated.Add(call.GetInt32(1));
            evaluated.Add(call.GetInt32(1));
            return QvmValue.Zero;
        });

        Run(natives, writer =>
        {
            QvmLabel first = writer.Code.CreateLabel();
            QvmLabel second = writer.Code.CreateLabel();
            QvmLabel after = writer.Code.CreateLabel();

            writer.PushSymbol("Note");
            writer.Code.Call(first, second);
            writer.Code.Branch(QvmOpcode.Bra, after);
            writer.Code.MarkLabel(first).Push(111).Emit(QvmOpcode.Brk);
            writer.Code.MarkLabel(second).Push(222).Emit(QvmOpcode.Brk);
            writer.Code.MarkLabel(after).Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        });

        // The first argument is never touched, and the second is evaluated once per read.
        Assert.Equal([222, 222], evaluated);
    }

    [Fact]
    public void AnArgumentSeesTheSideEffectsOfAnEarlierOne()
    {
        int counter = 0;
        NativeRegistry natives = new();
        natives.RegisterFunction("Bump", _ => QvmValue.FromInt32(++counter), returnType: QvmValueKind.Integer);
        natives.RegisterFunction("Sum", call => QvmValue.FromInt32(call.GetInt32(0) + call.GetInt32(0)));

        Run(natives, writer =>
        {
            QvmLabel argument = writer.Code.CreateLabel();
            QvmLabel after = writer.Code.CreateLabel();

            writer.PushSymbol("Sum");
            writer.Code.Call(argument);
            writer.Code.Branch(QvmOpcode.Bra, after);
            writer.Code.MarkLabel(argument);
            writer.PushSymbol("Bump");
            writer.Code.Call().Emit(QvmOpcode.Brk);
            writer.Code.MarkLabel(after).Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        });

        Assert.Equal(2, counter);
    }

    [Fact]
    public void ACallLeavesExactlyOneValueBehind()
    {
        NativeRegistry natives = new();
        natives.RegisterFunction("Answer", _ => QvmValue.FromInt32(42));

        QvmInterpreter interpreter = Run(natives, writer =>
        {
            writer.PushSymbol("Answer");
            writer.Code.Call().Emit(QvmOpcode.Brk);
        });

        Assert.Equal(1, interpreter.StackDepth);
        Assert.Equal(42, interpreter.Peek().Integer);
    }

    [Fact]
    public void TheDeclaredReturnTypeChoosesWhichResultIsPushed()
    {
        NativeRegistry natives = new();
        natives.RegisterFunction("AsInt", _ => QvmValue.FromSingle(2.75f));
        natives.RegisterFunction("AsFloat", _ => QvmValue.FromSingle(2.75f), returnType: QvmValueKind.Float);
        natives.RegisterFunction("AsText", _ => QvmValue.FromString("hello"), returnType: QvmValueKind.String);

        QvmInterpreter integer = Run(natives, writer =>
        {
            writer.PushSymbol("AsInt");
            writer.Code.Call().Emit(QvmOpcode.Brk);
        });
        Assert.Equal(QvmValue.FromInt32(2), integer.Peek());

        QvmInterpreter single = Run(natives, writer =>
        {
            writer.PushSymbol("AsFloat");
            writer.Code.Call().Emit(QvmOpcode.Brk);
        });
        Assert.Equal(QvmValueKind.Float, single.Peek().Kind);
        Assert.Equal(2.75f, single.Peek().ToSingle());

        QvmInterpreter text = Run(natives, writer =>
        {
            writer.PushSymbol("AsText");
            writer.Code.Call().Emit(QvmOpcode.Brk);
        });
        Assert.Equal("hello", text.Peek().Text);
    }

    [Fact]
    public void ANativeReceivesTheContextBoundToItsSymbol()
    {
        object first = new();
        object second = new();
        List<object?> seen = [];

        NativeRegistry natives = new();
        natives.RegisterFunction("Task_New", call =>
        {
            seen.Add(call.Context);
            return QvmValue.Zero;
        }, context: first);

        Run(natives, writer =>
        {
            writer.PushSymbol("Task_New");
            writer.Code.Call().Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        });

        natives.SetSymbolContext("Task_New", second);

        Run(natives, writer =>
        {
            writer.PushSymbol("Task_New");
            writer.Code.Call().Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        });

        Assert.Equal([first, second], seen);
    }

    [Fact]
    public void RebindingAnUnregisteredSymbolIsFatal() =>
        Assert.Throws<EngineFatalException>(() => new NativeRegistry().SetSymbolContext("Task_New", new object()));

    [Fact]
    public void AssigningToAnIntegerVariableNarrowsToItsWidth()
    {
        int stored = 0;
        NativeRegistry natives = new();
        natives.RegisterVariable("Health", new QvmIntegerVariable(() => stored, value => stored = value, size: 1));

        QvmInterpreter interpreter = Run(natives, writer =>
        {
            writer.PushSymbol("Health");
            writer.Code.Push(300).Emit(QvmOpcode.Assign).Emit(QvmOpcode.Brk);
        });

        // 300 truncated to a signed byte, and the assignment leaves what it stored on the stack.
        Assert.Equal(44, stored);
        Assert.Equal(44, interpreter.Peek().Integer);
    }

    [Fact]
    public void AssigningAFloatToAnIntegerVariableTruncatesTowardZero()
    {
        int stored = 0;
        NativeRegistry natives = new();
        natives.RegisterVariable("Count", new QvmIntegerVariable(() => stored, value => stored = value));

        Run(natives, writer =>
        {
            writer.PushSymbol("Count");
            writer.Code.PushF(-2.75f).Emit(QvmOpcode.Assign).Emit(QvmOpcode.Brk);
        });

        Assert.Equal(-2, stored);
    }

    [Fact]
    public void AssigningToAFloatVariableStoresFullPrecisionAndPushesBackAFloat()
    {
        double stored = 0;
        NativeRegistry natives = new();
        natives.RegisterVariable("Sensitivity", new QvmFloatVariable(() => stored, value => stored = value));

        QvmInterpreter interpreter = Run(natives, writer =>
        {
            writer.PushSymbol("Sensitivity");
            writer.Code.Push(3).Emit(QvmOpcode.Assign).Emit(QvmOpcode.Brk);
        });

        Assert.Equal(3d, stored);
        Assert.Equal(QvmValueKind.Float, interpreter.Peek().Kind);
    }

    [Fact]
    public void AssigningToAStringVariableTruncatesToItsCapacity()
    {
        string stored = string.Empty;
        NativeRegistry natives = new();
        natives.RegisterVariable("Player", new QvmStringVariable(() => stored, value => stored = value, capacity: 4));

        Run(natives, writer =>
        {
            writer.PushSymbol("Player");
            writer.Code.PushS("Jones").Emit(QvmOpcode.Assign).Emit(QvmOpcode.Brk);
        });

        Assert.Equal("Jon", stored);
    }

    [Fact]
    public void AssigningATypeAVariableCannotHoldIsASyntaxError()
    {
        NativeRegistry natives = new();
        natives.RegisterVariable("Count", new QvmIntegerVariable(() => 0, _ => { }));

        QvmWriter writer = new();
        writer.PushSymbol("Count");
        writer.Code.PushS("text").Emit(QvmOpcode.Assign).Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), natives);

        Assert.Equal(QvmError.SyntaxError, interpreter.Run());
    }

    [Fact]
    public void AssigningToSomethingThatIsNotAVariableFaults()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("KEY_LEFT", 203);

        QvmWriter writer = new();
        writer.PushSymbol("KEY_LEFT");
        writer.Code.Push(1).Emit(QvmOpcode.Assign).Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), natives);

        Assert.Equal(QvmError.IdentifierNotVariable, interpreter.Run());
        Assert.Equal("Identifier not a variable", interpreter.ErrorMessage);
    }

    [Fact]
    public void ReadingAVariableInAnExpressionUsesItsCurrentValue()
    {
        int stored = 41;
        NativeRegistry natives = new();
        natives.RegisterVariable("Count", new QvmIntegerVariable(() => stored, value => stored = value));

        QvmInterpreter interpreter = Run(natives, writer =>
        {
            writer.PushSymbol("Count");
            writer.Code.Push(1).Emit(QvmOpcode.Add).Emit(QvmOpcode.Brk);
        });

        Assert.Equal(42, interpreter.Peek().Integer);
    }

    [Fact]
    public void AConstantResolvesToItsValue()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("KEY_LEFT", 203);

        QvmInterpreter interpreter = Run(natives, writer =>
        {
            writer.PushSymbol("KEY_LEFT");
            writer.Code.Push(0).Emit(QvmOpcode.Add).Emit(QvmOpcode.Brk);
        });

        Assert.Equal(203, interpreter.Peek().Integer);
    }

    [Fact]
    public void AnInlineIdentifierResolvesWhenItIsConsumed()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("KEY_LEFT", 203);

        QvmWriter writer = new();
        writer.Code.PushI("KEY_LEFT").Push(0).Emit(QvmOpcode.Add).Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), natives);

        Assert.Equal(QvmError.None, interpreter.Run());
        Assert.Equal(203, interpreter.Peek().Integer);
    }

    [Fact]
    public void ReadingANonStringSymbolAsAStringSucceedsAndYieldsEmpty()
    {
        // The original's pop-as-string reports success for any readable symbol and only writes the
        // caller's buffer when the symbol really holds a string - so a native that asks for a string
        // argument and gets an integer constant is told "yes" and reads whatever was in the buffer.
        // Faithful, and observable: retail AI scripts pass constants like AIACTIONFLAG_NONE.
        string? asText = null;
        int asInteger = -1;

        NativeRegistry natives = new();
        natives.RegisterConstant("AIACTIONFLAG_NONE", 4242);
        natives.RegisterFunction("AIAction_Patrol", call =>
        {
            Assert.True(call.TryGetString(0, out string text));
            asText = text;
            asInteger = call.GetInt32(0);
            return QvmValue.Zero;
        });

        Run(natives, writer =>
        {
            QvmLabel argument = writer.Code.CreateLabel();
            QvmLabel after = writer.Code.CreateLabel();

            writer.PushSymbol("AIAction_Patrol");
            writer.Code.Call(argument);
            writer.Code.Branch(QvmOpcode.Bra, after);
            writer.Code.MarkLabel(argument);
            writer.PushSymbol("AIACTIONFLAG_NONE");
            writer.Code.Emit(QvmOpcode.Brk);
            writer.Code.MarkLabel(after).Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        });

        Assert.Equal(string.Empty, asText);
        Assert.Equal(4242, asInteger);
    }

    [Fact]
    public void PoppingAnEmptyStackUnderflows()
    {
        QvmInterpreter interpreter = Build(code => code.Emit(QvmOpcode.Pop));

        Assert.Equal(QvmError.StackUnderflow, interpreter.Run());
        Assert.Equal("Stack underflow", interpreter.ErrorMessage);
    }

    [Fact]
    public void PushingPastTheProgramsStackOverflows()
    {
        QvmWriter writer = new() { MaxLocals = 4 };
        for (int i = 0; i < 8; i++)
        {
            writer.Code.Emit(QvmOpcode.Push1);
        }

        writer.Code.Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), new NativeRegistry());

        Assert.Equal(QvmError.StackOverflow, interpreter.Run());
    }

    [Fact]
    public void AProgramCanBeRunMoreThanOnce()
    {
        NativeRegistry natives = new();
        int calls = 0;
        natives.RegisterFunction("Note", _ =>
        {
            calls++;
            return QvmValue.Zero;
        });

        QvmWriter writer = new();
        writer.PushSymbol("Note");
        writer.Code.Call().Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), natives);

        Assert.Equal(QvmError.None, interpreter.Run());
        Assert.Equal(QvmError.None, interpreter.Run());
        Assert.Equal(2, calls);
        Assert.Equal(0, interpreter.StackDepth);
    }

    private static QvmValue RunExpression(Action<QvmCodeWriter> emit)
    {
        QvmInterpreter interpreter = Build(code =>
        {
            emit(code);
            code.Emit(QvmOpcode.Brk);
        });

        Assert.Equal(QvmError.None, interpreter.Run());
        return interpreter.Peek();
    }

    private static QvmValue RunBranch(QvmOpcode opcode, int condition)
    {
        QvmCodeWriter writer = new();
        QvmLabel taken = writer.CreateLabel();
        QvmLabel end = writer.CreateLabel();

        writer.Push(condition);
        writer.Branch(opcode, taken);
        writer.Push(2);
        writer.Branch(QvmOpcode.Bra, end);
        writer.MarkLabel(taken).Push(1);
        writer.MarkLabel(end).Emit(QvmOpcode.Brk);

        QvmInterpreter interpreter = Build(writer);
        Assert.Equal(QvmError.None, interpreter.Run());
        return interpreter.Peek();
    }

    private static QvmInterpreter Build(Action<QvmCodeWriter> emit)
    {
        QvmCodeWriter code = new();
        emit(code);
        return Build(code);
    }

    private static QvmInterpreter Build(QvmCodeWriter code)
    {
        QvmWriter writer = new();
        writer.Code.Raw(code.ToArray());
        return new QvmInterpreter(QvmProgram.Read(writer.Build()), new NativeRegistry());
    }

    private static QvmInterpreter Run(NativeRegistry natives, Action<QvmWriter> emit)
    {
        QvmWriter writer = new();
        emit(writer);
        QvmInterpreter interpreter = new(QvmProgram.Read(writer.Build()), natives);
        Assert.Equal(QvmError.None, interpreter.Run());
        return interpreter;
    }
}
