using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The authored AlarmControl expressions and the AI natives that query them.</summary>
public sealed class AiAlarmRuntimeTests
{
    [Fact]
    public void TriggerIsWrittenBeforeTheAlarmExpressionRunsAndAllRetailFieldsAreExported()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("AlarmControl"),
                QvmExpression.Text("Trigger Expression"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Alarm Expression"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(98),
                QvmExpression.Text("AlarmControl"),
                QvmExpression.Text("Main Alarm"),
                QvmExpression.Text("TRUE"),
                QvmExpression.Text("AlarmControl_98.isTrigger"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        AiAlarmControlRegistry alarms = AiAlarmControlRegistry.Build(tree.Root, variables);
        alarms.Tick(7);

        Assert.True(alarms.IsOn(98));
        Assert.Equal(1, Read(variables, "AlarmControl_98.isTrigger").Integer);
        Assert.Equal(1, Read(variables, "AlarmControl_98.isAlarm").Integer);
        Assert.Equal(7, Read(variables, "AlarmControl_98.nTriggerLastTick").Integer);
    }

    [Fact]
    public void AlarmStatusIsIndependentOfTheSoldiersAlertLatch()
    {
        AiSoldier soldier = new(graph: null, position: default, facing: 0f, phase: 0, seed: 1)
        {
            AlarmControlId = 98,
        };

        soldier.SetAlarmStatusResolver(id => id == 98);
        Assert.True(soldier.GetAlarmControlStatus());
        Assert.False(soldier.Alerted);

        soldier.SetAlarmStatusResolver(_ => false);
        Assert.False(soldier.GetAlarmControlStatus());
    }

    [Fact]
    public void ScriptScratchSlotsBelongToEachSoldier()
    {
        AiSoldier first = new(graph: null, position: default, facing: 0f, phase: 0, seed: 1);
        AiSoldier second = new(graph: null, position: default, facing: 0f, phase: 1, seed: 2);

        first.SetScriptInteger(3, 17);
        first.SetScriptReal(4, 2.5f);

        Assert.Equal(17, first.GetScriptInteger(3));
        Assert.Equal(2.5f, first.GetScriptReal(4));
        Assert.Equal(0, second.GetScriptInteger(3));
        Assert.Equal(0f, second.GetScriptReal(4));
    }

    private static QvmValue Read(ScriptVarRegistry variables, string name)
    {
        Assert.True(variables.TryRead(name, out QvmValue value), $"missing exported variable {name}");
        return value;
    }
}
