using System.Globalization;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// Verifies the loader, the disassembler and the interpreter against the retail scripts.
/// </summary>
/// <remarks>
/// Skipped unless <c>IGI_GAME_PATH</c> points at an original installation. No game file is committed.
/// </remarks>
public sealed class ConfigQvmGoldenTests
{
    [RequiresGameDataFact]
    public void ConfigQvm_HasTheHeaderTheAnalysisRecorded()
    {
        QvmProgram program = Load("config.qvm");

        Assert.Equal(QvmProgram.IgiVersion, program.Version);
        Assert.Equal(78, program.SymbolNames.Count);
        Assert.Equal(9, program.Strings.Count);
        Assert.Equal(0x21B3, program.Code.Length);
        Assert.Equal("GOStart", program.SymbolNames[0]);
        Assert.Equal("GOContentControlPW", program.SymbolNames[1]);
        Assert.Equal(string.Empty, program.Strings[0]);
        Assert.Equal(QvmProgram.DefaultStackSizeBytes, program.StackSizeBytes);
        Assert.Equal(QvmProgram.DefaultMaxLocals, program.MaxLocals);
    }

    [RequiresGameDataFact]
    public void ConfigQvm_StartsWithTheStatementsTheAnalysisRecorded()
    {
        QvmProgram program = Load("config.qvm");

        string[] listing = [.. QvmDisassembler.Disassemble(program).Take(7)];

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
            listing);
    }

    [RequiresGameDataFact]
    public void ConfigQvm_ExecutesTheNativeCallSequenceItsCodeDescribes()
    {
        QvmProgram program = Load("config.qvm");
        List<RecordedCall> log = [];
        NativeRegistry natives = BuildRecordingNamespace(program, log);
        QvmInterpreter interpreter = new(program, natives);

        QvmError error = interpreter.Run();

        Assert.Equal(QvmError.None, interpreter.Error);
        Assert.Equal(QvmError.None, error);
        Assert.Equal(0, interpreter.StackDepth);

        Assert.Equal("GOStart", log[0].Name);
        Assert.Equal(["6"], log[0].Arguments);
        Assert.Equal("GOContentControlPW", log[1].Name);
        Assert.Equal([string.Empty], log[1].Arguments);

        // Every statement in this script is one native call, so the call count is the statement count.
        Assert.Equal(
            QvmDisassembler.Decode(program).Count(instruction => instruction.Opcode == QvmOpcode.Call),
            log.Count);
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
