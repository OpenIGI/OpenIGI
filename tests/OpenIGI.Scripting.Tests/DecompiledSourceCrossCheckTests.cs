using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// Checks the interpreter against scripts whose source is known from an independent decompiler.
/// </summary>
/// <remarks>
/// <para>
/// The community research repository ships QSC recovered from the retail bytecode by a separate tool
/// (<c>DConv</c>). Running the same bytecode here and comparing what the natives observe is a much
/// stronger statement than "it decoded without throwing": it exercises constant resolution,
/// comparison, conditional branching and deferred argument evaluation together, and pins the result
/// to source nobody involved in this port wrote.
/// </para>
/// <para>
/// Skipped unless <c>IGI_GAME_PATH</c> points at an original installation.
/// </para>
/// </remarks>
public sealed class DecompiledSourceCrossCheckTests
{
    private const string MissionScript = "missions/location0/level1/mission.qvm";
    private const string PatrolAiScript = "missions/location0/level1/ai/1205.qvm";

    [RequiresGameDataFact]
    public void Level1Mission_PassesTheArgumentsItsDecompiledSourceShows()
    {
        // DefineMission(1, "Mission 1 Name", "Mission 1 Desc", "", 2, "missions/location0/level1",
        //               "missions/location0/common", "missions/location0/level1", "location0",
        //               "level1", "mission1.spr");
        List<RecordedCall> log = Execute(MissionScript, _ => { });

        RecordedCall call = Assert.Single(log);
        Assert.Equal("DefineMission", call.Name);
        Assert.Equal(11, call.Arguments.Count);
        Assert.Equal(1, call.Arguments[0].Integer);
        Assert.Equal("Mission 1 Name", call.Arguments[1].Text);
        Assert.Equal("Mission 1 Desc", call.Arguments[2].Text);
        Assert.Equal(string.Empty, call.Arguments[3].Text);
        Assert.Equal(2, call.Arguments[4].Integer);
        Assert.Equal("missions/location0/level1", call.Arguments[5].Text);
        Assert.Equal("missions/location0/common", call.Arguments[6].Text);
        Assert.Equal("missions/location0/level1", call.Arguments[7].Text);
        Assert.Equal("location0", call.Arguments[8].Text);
        Assert.Equal("level1", call.Arguments[9].Text);
        Assert.Equal("mission1.spr", call.Arguments[10].Text);
    }

    [RequiresGameDataFact]
    public void PatrolAi_TakesTheBranchItsDecompiledSourceShows()
    {
        // if (AIFunction_GetCurrentEventType() == AIEVENT_CREATE)  { AIFunction_DefaultHandler(); }
        // else if (... == AIEVENT_IDLE)                            { AIAction_Patrol(1203, 0, AIACTIONFLAG_NONE); }
        // else if (... == AIEVENT_ALARMON)                         { }
        // else if (... == AIEVENT_COMBAT)                          { AIFunction_DefaultHandler(); }
        // else                                                     { AIFunction_DefaultHandler(); }
        //
        // The constants' real values do not matter: the script only compares the reported event
        // against them, so giving each a distinct value and reporting one back selects that arm.
        Assert.Equal(["AIFunction_DefaultHandler"], BranchTaken("AIEVENT_CREATE"));
        Assert.Equal(["AIAction_Patrol"], BranchTaken("AIEVENT_IDLE"));
        Assert.Empty(BranchTaken("AIEVENT_ALARMON"));
        Assert.Equal(["AIFunction_DefaultHandler"], BranchTaken("AIEVENT_COMBAT"));
        Assert.Equal(["AIFunction_DefaultHandler"], BranchTaken("AIEVENT_DEAD"));
    }

    [RequiresGameDataFact]
    public void PatrolAi_PassesThePatrolArgumentsItsDecompiledSourceShows()
    {
        List<RecordedCall> log = ExecuteAi("AIEVENT_IDLE");
        RecordedCall patrol = log.Single(call => call.Name == "AIAction_Patrol");

        Assert.Equal(3, patrol.Arguments.Count);
        Assert.Equal(1203, patrol.Arguments[0].Integer);
        Assert.Equal(0, patrol.Arguments[1].Integer);

        // AIACTIONFLAG_NONE is a named constant, so the third argument must arrive as its value.
        Assert.Equal(ConstantValue("AIACTIONFLAG_NONE"), patrol.Arguments[2].Integer);
    }

    private static IEnumerable<string> BranchTaken(string reportedEvent) =>
        ExecuteAi(reportedEvent)
            .Where(call => call.Name != "AIFunction_GetCurrentEventType")
            .Select(call => call.Name);

    private static List<RecordedCall> ExecuteAi(string reportedEvent) =>
        Execute(PatrolAiScript, natives => natives.RegisterFunction(
            "AIFunction_GetCurrentEventType",
            _ => QvmValue.FromInt32(ConstantValue(reportedEvent)),
            returnType: QvmValueKind.Integer));

    // Every symbol a script names gets a distinct, stable value so that comparisons between two of
    // them are only ever equal when the script really is comparing a symbol with itself.
    private static int ConstantValue(string name) => name.Length + (name.GetHashCode(StringComparison.Ordinal) & 0xFFFF);

    private static List<RecordedCall> Execute(string relativePath, Action<NativeRegistry> configure)
    {
        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(GameData.Resolve(relativePath)));
        List<RecordedCall> log = [];
        NativeRegistry natives = new();
        configure(natives);

        foreach (QvmSymbolUse use in ClassifySymbols(program))
        {
            if (natives.Contains(use.Name))
            {
                continue;
            }

            if (use.IsCallee)
            {
                natives.RegisterFunction(use.Name, call => Record(call, log), use.ArgumentCount);
            }
            else
            {
                natives.RegisterConstant(use.Name, ConstantValue(use.Name));
            }
        }

        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());
        return log;
    }

    private static List<QvmSymbolUse> ClassifySymbols(QvmProgram program)
    {
        List<QvmInstruction> instructions = [.. QvmDisassembler.Decode(program)];
        Dictionary<string, QvmSymbolUse> uses = new(StringComparer.Ordinal);

        for (int i = 0; i < instructions.Count; i++)
        {
            QvmInstruction instruction = instructions[i];
            if (instruction.Opcode is not (QvmOpcode.PushII or QvmOpcode.PushIIB or QvmOpcode.PushIIW)
                || !program.TryGetSymbolName(instruction.Integer, out string name))
            {
                continue;
            }

            bool isCallee = i + 1 < instructions.Count && instructions[i + 1].Opcode == QvmOpcode.Call;
            if (isCallee || !uses.ContainsKey(name))
            {
                uses[name] = new QvmSymbolUse(name, isCallee, isCallee ? instructions[i + 1].Integer : 0);
            }
        }

        return [.. uses.Values];
    }

    private static QvmValue Record(QvmCallContext call, List<RecordedCall> log)
    {
        // Both representations, because asking for one does not tell you the argument was not the
        // other: a pop-as-string of an integer constant succeeds and yields the empty string, which
        // is the original's behaviour rather than a shortcut here. See QvmInterpreterTests.
        List<RecordedArgument> arguments = [];
        for (int i = 0; i < call.ArgumentCount; i++)
        {
            arguments.Add(new RecordedArgument(
                call.GetInt32(i),
                call.TryGetString(i, out string text) ? text : string.Empty));
        }

        log.Add(new RecordedCall(call.Symbol.Name, arguments));
        return QvmValue.Zero;
    }

    private sealed record QvmSymbolUse(string Name, bool IsCallee, int ArgumentCount);

    private sealed record RecordedArgument(int Integer, string Text);

    private sealed record RecordedCall(string Name, IReadOnlyList<RecordedArgument> Arguments);
}
