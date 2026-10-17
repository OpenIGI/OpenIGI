using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

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
    public void SecurityCameraDamageProxyDoesNotSynthesizeAnAlarmLoop()
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("SCamera")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(12),
                QvmExpression.Text("SCamera"),
                QvmExpression.Text("Camera"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        bool alarmOn = false;
        AlarmSpeakerRegistry speakers = AlarmSpeakerRegistry.Build(tree.Root, variables, () => alarmOn);
        AlarmSpeaker camera = Assert.Single(speakers.Speakers);

        Assert.False(speakers.IsOn(camera));
        alarmOn = true;
        Assert.False(speakers.IsOn(camera));
        Assert.Equal(string.Empty, camera.SirenSound);
        Assert.Equal("explo_camera", camera.DestructionSound);
    }

    [Fact]
    public void ACameraTraceDestroysOnlyTheExactCameraTask()
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call("Task_DeclareParameters", QvmExpression.Text("SCamera")),
            QvmExpression.Call(
                "Task_New", QvmExpression.Int(12), QvmExpression.Text("SCamera"), QvmExpression.Text("Left")),
            QvmExpression.Call(
                "Task_New", QvmExpression.Int(13), QvmExpression.Text("SCamera"), QvmExpression.Text("Right"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        AlarmSpeakerRegistry speakers = AlarmSpeakerRegistry.Build(tree.Root, variables);
        GameTask left = Assert.IsType<DeclaredTask>(tree.FromId(12));
        AlarmSpeaker destroyed = Assert.IsType<AlarmSpeaker>(speakers.TryDestroy(left));

        Assert.Same(left, destroyed.Task);
        Assert.True(destroyed.IsDestroyed);
        Assert.False(speakers.Speakers.Single(speaker => speaker.Task.Id == 13).IsDestroyed);
        Assert.Null(speakers.TryDestroy(left));
    }

    [Fact]
    public void ACameraAddsUpItsRoundsAndBreaksOnlyAtHalfDamage()
    {
        (AlarmSpeakerRegistry speakers, GameTask camera) = OneCamera();

        // Retail Mission 1, probed live: each round sends 0.2 to 0x4422B0, which adds it to the lens's
        // +260 and explodes once that reaches the 0.5 at +264 -- 0.2, 0.4, then 0.6 on the third round.
        Assert.Null(speakers.TryDestroy(camera, 0.2f));
        Assert.Null(speakers.TryDestroy(camera, 0.2f));
        AlarmSpeaker destroyed = Assert.IsType<AlarmSpeaker>(speakers.TryDestroy(camera, 0.2f));

        Assert.True(destroyed.IsDestroyed);
        Assert.Equal(0.6f, destroyed.Damage, 5);
        Assert.Null(speakers.TryDestroy(camera, 0.2f));
    }

    [Fact]
    public void ABlastAddsItsContactDoseToACameraMeasuredFromItsHead()
    {
        (AlarmSpeakerRegistry speakers, GameTask cameraTask) = OneCamera();
        AlarmSpeaker camera = speakers.Speakers.Single();

        // 0x4422F0 -> 0x416C90: past radius + contact radius the dose falls off linearly to zero.
        const float Radius = 4096f;
        const float Falloff = 4096f;
        Vector3d halfway = camera.PositionUnits + new Vector3d(1f + Radius + (Falloff / 2), 0, 0);
        Assert.Empty(speakers.ApplyExplosion(halfway, Radius, Falloff, damageFactor: 0.4f));
        Assert.Equal(0.2f, camera.Damage, 5);
        Assert.False(camera.IsDestroyed);

        // Inside the radius the whole factor lands, and the sum now passes 0.5.
        Assert.Same(camera, Assert.Single(speakers.ApplyExplosion(camera.PositionUnits, Radius, Falloff, 0.4f)));
        Assert.True(speakers.IsDestroyed(cameraTask));
    }

    private static (AlarmSpeakerRegistry Speakers, GameTask Camera) OneCamera()
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call("Task_DeclareParameters", QvmExpression.Text("SCamera")),
            QvmExpression.Call(
                "Task_New", QvmExpression.Int(12), QvmExpression.Text("SCamera"), QvmExpression.Text("Lens"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        return (AlarmSpeakerRegistry.Build(tree.Root, variables), Assert.IsType<DeclaredTask>(tree.FromId(12)));
    }

    [Fact]
    public void ConditionalSoundBecomesAPositionalAlarmEmitter()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("ConditionalSound"),
                QvmExpression.Text("Condition"), QvmExpression.Text("VarString"),
                QvmExpression.Text("SoundDef"), QvmExpression.Text("String32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(96),
                QvmExpression.Text("ConditionalSound"),
                QvmExpression.Text("Alarm speaker"),
                QvmExpression.Text("TRUE"),
                QvmExpression.Text("alarmsystem_working"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        AlarmSpeakerRegistry speakers = AlarmSpeakerRegistry.Build(tree.Root, variables);
        AlarmSpeaker alarm = Assert.Single(speakers.Speakers);

        Assert.True(speakers.IsOn(alarm));
        Assert.Equal("alarmsystem_working", alarm.SirenSound);
        Assert.Equal(string.Empty, alarm.DestructionSound);
        Assert.False(alarm.CanBeDestroyed);
    }

    [RequiresGameDataFact]
    public void MissionOneUsesTwoConditionalSoundsAsItsAlarmSpeakers()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);
        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        QvmProgram level = QvmProgram.Read(File.ReadAllBytes(
            GameData.Resolve("missions/location0/level1/objects.qvm")));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, natives).Run());

        AlarmSpeakerRegistry speakers = AlarmSpeakerRegistry.Build(tree.Root, variables);
        AlarmSpeaker[] alarmSounds = speakers.Speakers
            .Where(speaker => speaker.Task.Type.Name == "ConditionalSound")
            .ToArray();

        Assert.Equal(7, speakers.Speakers.Count);
        Assert.Equal(2, alarmSounds.Length);
        Assert.All(alarmSounds, speaker => Assert.Equal("alarmsystem_working", speaker.SirenSound));

        // Mission 1 latches EditVariable_97 from the switch pulse, then AlarmControl_98 reads it.
        // Running those phases in retail order must leave the alarm on after the press has ended.
        MissionObjectiveRuntime objectives = new(tree.Root, variables);
        AiAlarmControlRegistry alarms = AiAlarmControlRegistry.Build(tree.Root, variables);
        DeclaredTask alarmSwitch = Assert.IsType<DeclaredTask>(tree.FromId(94));
        ScriptFlag pressed = new();
        variables.BindFlag(alarmSwitch, "isPressed", pressed);

        // The switch holds isPressed for three ticks while isLastPressed remains false.
        pressed.Value = true;
        for (int tick = 1; tick <= 3; tick++)
        {
            objectives.TickLevelState();
            alarms.Tick(tick);
            objectives.TickAfterLevelState();
        }

        // On expiry IGI1 emits isLastPressed for one tick. The control sees the trigger now; on the
        // following tick EditVariable_97 latches and the alarm turns on after the pulse has cleared.
        pressed.EndPulse();
        objectives.TickLevelState();
        alarms.Tick(4);
        objectives.TickAfterLevelState();
        pressed.CommitTick();

        objectives.TickLevelState();
        alarms.Tick(5);
        objectives.TickAfterLevelState();

        Assert.True(alarms.IsOn(98));

        objectives.TickLevelState();
        alarms.Tick(6);
        objectives.TickAfterLevelState();
        Assert.True(alarms.IsOn(98));
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
