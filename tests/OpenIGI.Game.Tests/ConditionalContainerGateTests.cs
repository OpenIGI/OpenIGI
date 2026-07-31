using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A <see cref="ConditionalContainerTask"/> only registers its subtree into the world while its
/// <c>Condition</c> holds, exactly as the original's <c>ConditionalContainer_RunHandler</c>
/// (<c>0x520EF0</c>) registers and removes children as the condition changes. These reproduce level 1's
/// own structure: a cut-scene container gated on cut-scene state that is false during play, and the
/// gameplay container gated on that cut-scene container <em>not</em> running.
/// </summary>
/// <remarks>
/// This is the fix for the extra David Jones — the intro/outro cut-scenes place <c>HumanSoldier</c>s and a
/// <c>HumanPlayer</c> on the player's own <c>000_01_1</c> model inside these containers, and without the
/// gate the port drew them standing in the base at mission start.
/// </remarks>
public sealed class ConditionalContainerGateTests
{
    private static (TaskTree Tree, ScriptVarRegistry Variables) BuildLevel(params QvmExpression[] statements)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        types.Register<CutSceneTask>(CutSceneTask.TypeName);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        PlayerTaskTypes.RegisterAll(types);

        QvmProgram program = QvmProgram.Read(QvmExpression.BuildProgram(statements));
        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());
        return (tree, variables);
    }

    private static ConditionalContainerTask Container(TaskTree tree, int id) =>
        Assert.IsType<ConditionalContainerTask>(Find(tree.Root, id));

    private static GameTask Find(GameTask task, int id)
    {
        foreach (GameTask child in task.Children)
        {
            if (child.Id == id)
            {
                return child;
            }

            if (Find(child, id) is { } found)
            {
                return found;
            }
        }

        return null!;
    }

    [Fact]
    public void ACutSceneContainerIsDormantAndTheGameplayContainerRuns()
    {
        // CutScene 1203 presents as already finished, so the intro condition is false; the outro condition
        // names an EditVariable the level does not model here, which reads zero, so 4007 is false too; and
        // the gameplay container 1495 runs because neither cut-scene container does — level 1's own logic.
        (TaskTree tree, ScriptVarRegistry variables) = BuildLevel(
            QvmExpression.Call("Task_New", QvmExpression.Int(1203), QvmExpression.Text("CutScene"), QvmExpression.Text("")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(4001),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Intro cutscene"),
                QvmExpression.Text("!CutScene_1203.isFinished"),
                QvmExpression.Text(""),
                QvmExpression.Text("")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(4007),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Outro Cutscene"),
                QvmExpression.Text("EditVariable_199.nValue == 1 && !CutScene_1204.isFinished"),
                QvmExpression.Text(""),
                QvmExpression.Text("")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(1495),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Main character and status messages"),
                QvmExpression.Text("!ConditionalContainer_4001.isRun && !ConditionalContainer_4007.isRun"),
                QvmExpression.Text(""),
                QvmExpression.Text("")));

        int dormant = new ConditionalContainerGate(variables).Prepare(tree.Root);

        Assert.False(Container(tree, 4001).IsRun);
        Assert.False(Container(tree, 4007).IsRun);
        Assert.True(Container(tree, 1495).IsRun);

        Assert.True(ConditionalContainerGate.IsDormant(Container(tree, 4001)));
        Assert.False(ConditionalContainerGate.IsDormant(Container(tree, 1495)));
        Assert.Equal(2, dormant);
    }

    [Fact]
    public void ThePlayerSpawnSkipsAPlayerInADormantCutsceneContainer()
    {
        // The outro cut-scene declares a second HumanPlayer on the player's own model, BEFORE the gameplay
        // container declares the real one. Without the gate PlayerSpawn takes the first it walks to — the
        // cut-scene one; with it, the dormant subtree is skipped and the real spawn is found.
        (TaskTree tree, ScriptVarRegistry variables) = BuildLevel(
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(4007),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Outro Cutscene"),
                QvmExpression.Text("FALSE"),
                QvmExpression.Text(""),
                QvmExpression.Text(""),
                Player(id: 90, x: 1.0f, model: "cutscene_jones")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(1495),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Main"),
                QvmExpression.Text("!ConditionalContainer_4007.isRun"),
                QvmExpression.Text(""),
                QvmExpression.Text(""),
                Player(id: 0, x: 2.0f, model: "the_real_jones")));

        new ConditionalContainerGate(variables).Prepare(tree.Root);

        Assert.True(PlayerSpawn.TryFind(tree.Root, out PlayerSpawnPoint spawn));
        Assert.Equal("the_real_jones", spawn.Model);
        Assert.Equal(2.0, spawn.Position.X);

        static QvmExpression Player(int id, float x, string model) => QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(id),
            QvmExpression.Text("HumanPlayer"),
            QvmExpression.Text("player"),
            QvmExpression.Real(x),
            QvmExpression.Real(0f),
            QvmExpression.Real(0f),
            QvmExpression.Real(0f),
            QvmExpression.Text(model),
            QvmExpression.Int(0));
    }
}
