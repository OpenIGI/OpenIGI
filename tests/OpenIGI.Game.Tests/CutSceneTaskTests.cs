using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The <see cref="CutSceneTask"/> exports the three script variables a level's AI scripts read off a
/// cut-scene — <c>isRun</c>, <c>isFinished</c> and <c>nTick</c> — so a read like
/// <c>CutScene_1200.nTick</c> resolves instead of faulting the archetype script every idle tick.
/// </summary>
public sealed class CutSceneTaskTests
{
    [Fact]
    public void ACutSceneExportsIsRunIsFinishedAndNTick()
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);
        types.Register<CutSceneTask>(CutSceneTask.TypeName);

        // The exact shape of a level's cut-scene declaration: Task_New(id, "CutScene", label).
        QvmProgram program = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(1200),
                QvmExpression.Text("CutScene"),
                QvmExpression.Text("intro"))));
        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());

        // All three symbols a MISSION:AI script reads now bind, so the read that faulted guard.qsc's idle
        // branch with UnknownIdentifier: CutScene_1200.nTick now resolves.
        Assert.True(natives.Contains("CutScene_1200.nTick"), "nTick did not export");
        Assert.True(natives.Contains("CutScene_1200.isRun"), "isRun did not export");
        Assert.True(natives.Contains("CutScene_1200.isFinished"), "isFinished did not export");
    }
}
