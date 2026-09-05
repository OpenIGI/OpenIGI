using System.Globalization;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// Verifies the loader, the disassembler and the interpreter using controlled config programs and retail scripts.
/// </summary>
/// <remarks>
/// Only retail-script checks require <c>IGI_GAME_PATH</c> to point at an original installation;
/// controlled-config checks run without game data. No game file is committed.
/// </remarks>
public sealed class ConfigQvmGoldenTests
{
    [Fact]
    public void ConfigPreambleHasTheKnownHeaderAndStatementEncoding()
    {
        // Golden bytes belong to a controlled program, not config.qvm: the game rewrites that
        // file whenever profiles, passwords, bindings or display settings change.
        QvmProgram program = Preamble(activeIndex: 6, password: string.Empty);
        Assert.Equal(QvmProgram.IgiVersion, program.Version);
        Assert.Equal(2, program.SymbolNames.Count);
        Assert.Single(program.Strings);
        Assert.Equal("GOStart", program.SymbolNames[0]);
        Assert.Equal("GOContentControlPW", program.SymbolNames[1]);
        Assert.Equal(string.Empty, program.Strings[0]);
        Assert.Equal(QvmProgram.DefaultStackSizeBytes, program.StackSizeBytes);
        Assert.Equal(QvmProgram.DefaultMaxLocals, program.MaxLocals);
        Assert.Equal(
            [
                "     0: PUSHIIB #0                    ; symbol \"GOStart\"",
                "     2: CALL    argc=1                ; args @16",
                "    11: BRA     +3                    ; -> 19",
                "    16: PUSHB   6",
                "    18: BRK",
                "    19: POP",
                "    20: PUSHIIB #1                    ; symbol \"GOContentControlPW\"",
            ],
            QvmDisassembler.Disassemble(program).Take(7).ToArray());
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "custom-password")]
    [InlineData(6, "")]
    public void ConfigPreambleExecutesTheAuthoredValues(int activeIndex, string password)
    {
        List<RecordedCall> log = Execute(Preamble(activeIndex, password));
        Assert.Equal(2, log.Count);
        Assert.Equal("GOStart", log[0].Name);
        Assert.Equal([activeIndex.ToString(CultureInfo.InvariantCulture)], log[0].Arguments);
        Assert.Equal("GOContentControlPW", log[1].Name);
        Assert.Equal([password], log[1].Arguments);
    }

    [RequiresGameDataFact]
    public void RetailConfigExecutesEveryAuthoredCall()
    {
        QvmProgram program = Load("config.qvm");
        List<RecordedCall> log = Execute(program);
        Assert.Equal("GOStart", log[0].Name);
        Assert.Single(log[0].Arguments);
        Assert.Equal("GOContentControlPW", log[1].Name);
        Assert.Single(log[1].Arguments);
        int profiles = log.Count(call => call.Name == "GOPlayer");
        Assert.True(profiles > 0);
        Assert.InRange(int.Parse(log[0].Arguments[0], CultureInfo.InvariantCulture), 0, profiles - 1);
    }

    private static List<RecordedCall> Execute(QvmProgram program)
    {
        List<RecordedCall> log = [];
        NativeRegistry natives = BuildRecordingNamespace(program, log);
        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());
        Assert.Equal(QvmError.None, interpreter.Error);
        Assert.Equal(0, interpreter.StackDepth);
        Assert.Equal(
            QvmDisassembler.Decode(program).Count(instruction => instruction.Opcode == QvmOpcode.Call),
            log.Count);
        return log;
    }

    private static QvmProgram Preamble(int activeIndex, string password)
    {
        QvmWriter writer = new();
        QvmCodeWriter code = writer.Code;
        QvmLabel index = code.CreateLabel();
        QvmLabel afterIndex = code.CreateLabel();
        writer.PushSymbol("GOStart");
        code.Call(index).Branch(QvmOpcode.Bra, afterIndex).MarkLabel(index);
        if (activeIndex == 0)
        {
            code.Emit(QvmOpcode.Push0);
        }
        else if (activeIndex == 1)
        {
            code.Emit(QvmOpcode.Push1);
        }
        else
        {
            code.PushB(checked((sbyte)activeIndex));
        }
        code.Emit(QvmOpcode.Brk).MarkLabel(afterIndex).Emit(QvmOpcode.Pop);

        QvmLabel text = code.CreateLabel();
        QvmLabel afterText = code.CreateLabel();
        writer.PushSymbol("GOContentControlPW");
        code.Call(text).Branch(QvmOpcode.Bra, afterText).MarkLabel(text);
        writer.PushString(password);
        code.Emit(QvmOpcode.Brk).MarkLabel(afterText).Emit(QvmOpcode.Pop).Emit(QvmOpcode.Brk);
        return QvmProgram.Read(writer.Build());
    }

    [RequiresGameDataFact]
    public void EveryRetailScriptLoadsAndDisassembles()
    {
        IReadOnlyList<string> scripts = GameData.EnumerateFiles("*.qvm");
        Assert.NotEmpty(scripts);

        foreach (string path in scripts)
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            List<QvmInstruction> instructions = [.. QvmDisassembler.Decode(program)];

            Assert.NotEmpty(instructions);
            Assert.All(instructions, instruction =>
                Assert.True(
                    QvmOpcodeInfo.TryGet(instruction.Opcode, out _),
                    $"{path} at {instruction.Offset}: opcode 0x{(byte)instruction.Opcode:X2} is not in the table."));

            // A linear sweep that ends exactly on the section boundary proves the operand widths:
            // one wrong length would desynchronise the decode and overshoot or undershoot.
            QvmInstruction last = instructions[^1];
            Assert.Equal(program.Code.Length, last.Offset + last.Length);
        }
    }

    private static QvmProgram Load(string relativePath) =>
        QvmProgram.Read(File.ReadAllBytes(GameData.Resolve(relativePath)));

    private static NativeRegistry BuildRecordingNamespace(QvmProgram program, List<RecordedCall> log)
    {
        // Which symbols are functions is decided by the code itself: a symbol pushed immediately
        // before a CALL is the callee. Everything else a script names is a constant.
        List<QvmInstruction> instructions = [.. QvmDisassembler.Decode(program)];
        Dictionary<string, int> callees = new(StringComparer.Ordinal);

        for (int i = 1; i < instructions.Count; i++)
        {
            if (instructions[i].Opcode != QvmOpcode.Call)
            {
                continue;
            }

            QvmInstruction callee = instructions[i - 1];
            if (callee.Opcode is QvmOpcode.PushII or QvmOpcode.PushIIB or QvmOpcode.PushIIW
                && program.TryGetSymbolName(callee.Integer, out string name))
            {
                callees[name] = instructions[i].Integer;
            }
        }

        NativeRegistry natives = new();
        for (int index = 0; index < program.SymbolNames.Count; index++)
        {
            if (program.SymbolNames[index] is not { } name || natives.Contains(name))
            {
                continue;
            }

            if (callees.TryGetValue(name, out int argumentCount))
            {
                natives.RegisterFunction(name, call => Record(call, log), argumentCount);
            }
            else
            {
                natives.RegisterConstant(name, index);
            }
        }

        return natives;
    }

    private static QvmValue Record(QvmCallContext call, List<RecordedCall> log)
    {
        List<string> arguments = [];
        for (int i = 0; i < call.ArgumentCount; i++)
        {
            arguments.Add(call.TryGetString(i, out string text)
                ? text
                : call.GetInt32(i).ToString(CultureInfo.InvariantCulture));
        }

        log.Add(new RecordedCall(call.Symbol.Name, arguments));
        return QvmValue.Zero;
    }

    private sealed record RecordedCall(string Name, IReadOnlyList<string> Arguments);
}
