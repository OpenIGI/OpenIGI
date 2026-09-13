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
    public void AnUnfinishedIntroContainerRunsAndTheGameplayContainerWaits()
    {
        // A newly loaded authored cutscene is unfinished, so the intro subtree registers. The gameplay
        // subtree waits until that intro completes, while the untriggered outro remains dormant.
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

        Assert.True(Container(tree, 4001).IsRun);
        Assert.False(Container(tree, 4007).IsRun);
        Assert.False(Container(tree, 1495).IsRun);

        Assert.False(ConditionalContainerGate.IsDormant(Container(tree, 4001)));
        Assert.True(ConditionalContainerGate.IsDormant(Container(tree, 1495)));
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

    [Fact]
    public void GameplaySpawnRemainsAvailableWhileTheIntroOwnsRegistration()
    {
        (TaskTree tree, ScriptVarRegistry variables) = BuildLevel(
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(4001),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Intro cutscene"),
                QvmExpression.Text("TRUE"),
                QvmExpression.Text(""),
                QvmExpression.Text(""),
                Player(id: 90, x: 1f, model: "cinematic_jones")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(1495),
                QvmExpression.Text("ConditionalContainer"),
                QvmExpression.Text("Main character"),
                QvmExpression.Text("FALSE"),
                QvmExpression.Text(""),
                QvmExpression.Text(""),
                Player(id: 0, x: 2f, model: "gameplay_jones")));

        new ConditionalContainerGate(variables).Prepare(tree.Root);

        Assert.True(PlayerSpawn.TryFindGameplay(tree.Root, out PlayerSpawnPoint spawn));
        Assert.Equal("gameplay_jones", spawn.Model);

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

    [Fact]
    public void ARetailSelfLatchingContainerHoldsAfterItsTriggerClears()
    {
        // Missions 4, 6, 7, 8, 9, 10, 11 and 14 write 36 of their container conditions as
        // "<trigger> || this.isRun": the container latches itself on when the trigger first fires and
        // holds once it clears. `this` is the task that owns the expression -- the original rewrites the
        // token to "<owning task name>.<field>" in its expression preprocessor (0x4F1400,
        // sprintf("%s.%s", Task_GetName(owner), rest)) before the text is ever compiled. Container 301 is
        // the control: the same trigger without the self-reference, which must drop out again.
        (TaskTree tree, ScriptVarRegistry variables) = BuildLevel(
            Scene(1203),
            Gate(300, "CutScene_1203.isTriggered || this.isRun"),
            Gate(301, "CutScene_1203.isTriggered"));

        bool triggered = false;
        variables.BindBool(Find(tree.Root, 1203), "isTriggered", () => triggered, _ => { });
        new ConditionalContainerGate(variables).Prepare(tree.Root);

        Assert.False(Container(tree, 300).IsRun);
        Assert.False(Container(tree, 301).IsRun);

        triggered = true;
        Container(tree, 300).Refresh(out _);
        Container(tree, 301).Refresh(out _);
        Assert.True(Container(tree, 300).IsRun);
        Assert.True(Container(tree, 301).IsRun);

        triggered = false;
        Container(tree, 300).Refresh(out _);
        Container(tree, 301).Refresh(out _);
        Assert.True(Container(tree, 300).IsRun);
        Assert.False(Container(tree, 301).IsRun);
    }

    [Fact]
    public void TwoContainersSharingAConditionResolveThisAgainstTheirOwnNames()
    {
        // Both conditions are the same string, so they share one parse-cache entry. `this` must still
        // resolve per evaluation against the container being resolved: pinning 300's own isRun must not
        // hold 301 open through the cached expression.
        (TaskTree tree, ScriptVarRegistry variables) = BuildLevel(
            Scene(1203),
            Gate(300, "CutScene_1203.isTriggered || this.isRun"),
            Gate(301, "CutScene_1203.isTriggered || this.isRun"));

        bool triggered = false;
        variables.BindBool(Find(tree.Root, 1203), "isTriggered", () => triggered, _ => { });
        new ConditionalContainerGate(variables).Prepare(tree.Root);

        Assert.True(variables.TryWrite("ConditionalContainer_300.isRun", QvmValue.FromInt32(1)));
        Container(tree, 300).Refresh(out _);
        Container(tree, 301).Refresh(out _);

        Assert.True(Container(tree, 300).IsRun);
        Assert.False(Container(tree, 301).IsRun);
    }

    private static QvmExpression Scene(int id) => QvmExpression.Call(
        "Task_New", QvmExpression.Int(id), QvmExpression.Text("CutScene"), QvmExpression.Text(""));

    private static QvmExpression Gate(int id, string condition) => QvmExpression.Call(
        "Task_New",
        QvmExpression.Int(id),
        QvmExpression.Text("ConditionalContainer"),
        QvmExpression.Text("container"),
        QvmExpression.Text(condition),
        QvmExpression.Text(""),
        QvmExpression.Text(""));
}
