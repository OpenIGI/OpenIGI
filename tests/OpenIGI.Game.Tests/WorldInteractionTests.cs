using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class WorldInteractionTests
{
    [Fact]
    public void LevelConditionsUseRetailBooleanConstantsAndEmptyDefaults()
    {
        NativeRegistry natives = new();
        ScriptVarRegistry variables = new(natives);
        LevelExpressionRuntime expressions = new(variables);

        Assert.True(expressions.EvaluateCondition("TRUE && !FALSE", emptyValue: false));
        Assert.True(expressions.EvaluateCondition(string.Empty, emptyValue: true));
        Assert.False(expressions.EvaluateCondition(string.Empty, emptyValue: false));

        natives.RegisterVariable("Lift.vFloor", new QvmFloatVariable(() => 0.00025, _ => { }));
        Assert.False(expressions.EvaluateCondition("Lift.vFloor == 0", emptyValue: true));
    }

    [Fact]
    public void LevelConditionsResolveThisAgainstEachOwningTask()
    {
        (ScriptVarRegistry variables, GameTask first, GameTask second) = BuildDoorTasks();
        variables.BindBool(first, "isClosed", () => true, _ => { });
        variables.BindBool(second, "isClosed", () => false, _ => { });
        LevelExpressionRuntime expressions = new(variables);

        Assert.True(expressions.EvaluateCondition("this.isClosed", emptyValue: false, first));
        Assert.False(expressions.EvaluateCondition("this.isClosed", emptyValue: false, second));
        Assert.True(expressions.EvaluateCondition("this.isClosed", emptyValue: false, first));
    }

    [Fact]
    public void ReentrantLevelConditionsKeepTheirOwningTasksIsolated()
    {
        (ScriptVarRegistry variables, GameTask outer, GameTask nested) = BuildDoorTasks();
        variables.BindBool(outer, "isClosed", () => true, _ => { });
        variables.BindBool(nested, "isClosed", () => false, _ => { });
        LevelExpressionRuntime? expressions = null;
        expressions = new LevelExpressionRuntime(variables, (name, _) =>
            name == "EvaluateNested"
                ? QvmValue.FromInt32(
                    expressions!.EvaluateCondition("this.isClosed", emptyValue: false, nested) ? 1 : 0)
                : null);

        Assert.True(expressions.EvaluateCondition(
            "EvaluateNested() || this.isClosed", emptyValue: false, outer));
    }

    [Fact]
    public void DoorDrawTransformFollowsBothMotionChannels()
    {
        DoorState state = new(maxAngleDegrees: 90f, openTimeSeconds: 1f, slides: true);
        PlacedDoor door = new(
            new LevelTask(),
            new Vector3d(4096.0, 8192.0, 12288.0),
            Matrix3f.Identity,
            1f,
            0f,
            0f,
            state);

        state.CommandOpen();

        // One step past time × 30: the chop-rounded slide step lands a hair short of one after thirty (see DoorStateTests).
        for (int i = 0; i < 31; i++)
        {
            state.Tick();
        }

        Assert.Equal(new Vector3d(2.0, 2.0, 3.0), door.DrawPosition);
        Assert.Equal(90f, state.AngleDegrees);
    }

    [Theory]
    [InlineData(4, 31)]
    [InlineData(5, 32)]
    [InlineData(6, 33)]
    [InlineData(7, 34)]
    [InlineData(12, 35)]
    [InlineData(13, 36)]
    public void RetailSelfDrivingTypesEnterTheirExactState(int type, int state)
    {
        Assert.Equal(state, OpenIGI.Game.Player.HumanInteractionDispatch.Dispatch(type).LocomotionState);
    }

    private static (ScriptVarRegistry Variables, GameTask First, GameTask Second) BuildDoorTasks()
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call("Task_DeclareParameters", QvmExpression.Text("Door")),
            QvmExpression.Call(
                "Task_New", QvmExpression.Int(400), QvmExpression.Text("Door"), QvmExpression.Text("First")),
            QvmExpression.Call(
                "Task_New", QvmExpression.Int(403), QvmExpression.Text("Door"), QvmExpression.Text("Second"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        return (
            variables,
            Assert.IsType<DeclaredTask>(tree.FromId(400)),
            Assert.IsType<DeclaredTask>(tree.FromId(403)));
    }

    /// <summary>
    /// The preprocessor answers <c>.isExists</c> before any variable is consulted: <c>0x4F1574</c> is
    /// the line after the <c>this.</c> rewrite, and it replaces any token CONTAINING that text with the
    /// literal <c>1</c>. Resolving it as an unbound name instead would read zero, which is the opposite
    /// of what the engine says. It is a <c>strstr</c>, so the match is anywhere in the token, and it
    /// applies to a name the <c>this.</c> rewrite produced too.
    /// </summary>
    [Fact]
    public void AnIsExistsFieldIsAlwaysTrueWhicheverTaskNamesIt()
    {
        (ScriptVarRegistry variables, GameTask first, _) = BuildDoorTasks();
        LevelExpressionRuntime expressions = new(variables);

        Assert.True(expressions.EvaluateCondition("Door_400.isExists", emptyValue: false));
        Assert.True(expressions.EvaluateCondition("SomethingNeverDeclared_7.isExists", emptyValue: false));
        Assert.True(expressions.EvaluateCondition("this.isExists", emptyValue: false, first));

        // And it is still only that field: a name beside it resolves normally, which for an unbound
        // one is zero.
        Assert.False(expressions.EvaluateCondition("Door_400.isClosed", emptyValue: false));
    }
}
