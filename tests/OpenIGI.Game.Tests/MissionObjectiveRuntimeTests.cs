using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks objective/LevelFlow extraction against all fourteen retail mission scripts.</summary>
public sealed class MissionObjectiveRuntimeTests
{
    [Fact]
    public void AreaActivationCountsOnlySoldiersRegisteredIntoTheLiveWorld()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("AreaActivate"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Orientation"), QvmExpression.Text("Real32x9"),
                QvmExpression.Text("Dimensions"), QvmExpression.Text("Real32x3"),
                QvmExpression.Text("Criteria"), QvmExpression.Text("String32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(900),
                QvmExpression.Text("AreaActivate"),
                QvmExpression.Text("Activation volume"),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(20f), QvmExpression.Real(20f), QvmExpression.Real(20f),
                QvmExpression.Text("CRITERIA_HUMAN1"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        PlayerTaskTypes.RegisterAll(loop.Types);

        ConditionalContainerTask dormantGate = (ConditionalContainerTask)loop.Tasks.New(
            loop.Tasks.Root,
            loop.Types.Get(ConditionalContainerTask.TypeName));
        Assert.True(loop.Tasks.TryBindId(901, dormantGate));
        loop.Variables.Export(dormantGate);
        dormantGate.Condition = "0";

        HumanSoldierTask dormant = NewSoldier(dormantGate, 902);
        HumanSoldierTask active = NewSoldier(loop.Tasks.Root, 903);
        HumanSoldierTask deleted = NewSoldier(loop.Tasks.Root, 904);
        HumanSoldierTask dead = NewSoldier(loop.Tasks.Root, 905);

        new ConditionalContainerGate(loop.Variables).Prepare(loop.Tasks.Root);
        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);
        loop.Tasks.Delete(deleted);
        Vector3d inside = new(0.0, 0.0, 0.0);
        runtime.SetSoldierOccupants(
        [
            new HumanOccupant(dormant.Id, 1, inside, IsDead: false),
            new HumanOccupant(active.Id, 1, inside, IsDead: false),
            new HumanOccupant(904, 1, inside, IsDead: false),
            new HumanOccupant(dead.Id, 1, inside, IsDead: true),
            new HumanOccupant(999, 1, inside, IsDead: false),
        ]);

        runtime.TickLevelState();

        Assert.Equal(1, ReadActive(loop));
        Assert.False(ReadDead(loop, dormant.Id));
        Assert.False(ReadDead(loop, 904));
        Assert.True(ReadDead(loop, dead.Id));

        Assert.True(loop.Variables.TryWrite("ConditionalContainer_901.isRun", QvmValue.FromInt32(1)));
        runtime.TickLevelState();

        Assert.Equal(2, ReadActive(loop));

        static HumanSoldierTask NewSoldier(GameTask parent, int id)
        {
            HumanSoldierTask soldier = (HumanSoldierTask)parent.Tree.New(parent, parent.Tree.Types.Get("HumanSoldier"));
            Assert.True(parent.Tree.TryBindId(id, soldier));
            return soldier;
        }

        static int ReadActive(GameLoop loop)
        {
            Assert.True(loop.Variables.TryRead("AreaActivate_900.nActive", out QvmValue active));
            return active.ToInt32();
        }

        static bool ReadDead(GameLoop loop, int id)
        {
            Assert.True(loop.Variables.TryRead($"HumanSoldier_{id}.isDead", out QvmValue isDead));
            return isDead.ToInt32() != 0;
        }
    }

    [Fact]
    public void AreaActivationUsesTheLoadedUpperZBound()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("AreaActivate"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Orientation"), QvmExpression.Text("Real32x9"),
                QvmExpression.Text("Dimensions"), QvmExpression.Text("Real32x3"),
                QvmExpression.Text("Criteria"), QvmExpression.Text("String32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(900),
                QvmExpression.Text("AreaActivate"),
                QvmExpression.Text("Activation volume"),
                QvmExpression.Real(100f), QvmExpression.Real(200f), QvmExpression.Real(300f),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(40f), QvmExpression.Real(60f), QvmExpression.Real(80f),
                QvmExpression.Text("PLAYER"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);
        runtime.TickLevelState(new Vector3d(100.0, 200.0, 340.0));
        Assert.Equal(1, ReadActive(loop));

        runtime.TickLevelState(new Vector3d(100.0, 200.0, 340.1));
        Assert.Equal(0, ReadActive(loop));

        static int ReadActive(GameLoop loop)
        {
            Assert.True(loop.Variables.TryRead("AreaActivate_900.nActive", out QvmValue active));
            return active.ToInt32();
        }
    }

    [Fact]
    public void TransientStatusMessageUsesTheMissionMessageSlotsWithoutWritingTheComputerLog()
    {
        GameLoop loop = new();
        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);

        Assert.True(runtime.ShowTransientMessage("PICKEDUPWEAPONAK-47"));
        StatusMessageDisplay message = Assert.Single(runtime.DisplayedMessages);
        Assert.Equal("PICKEDUPWEAPONAK-47", message.Text);
        Assert.Empty(runtime.Log);
    }

    [Fact]
    public void AuthoredInterfaceTimerStartsAtTheRetailMaximumAndStopsAtZero()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Interface timer enabled"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Max level play time"), QvmExpression.Text("Real32"),
                QvmExpression.Text("Complete"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Failed"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(1),
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Level flow"),
                QvmExpression.Int(1),
                QvmExpression.Real(2.5f),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(string.Empty))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);
        Assert.True(runtime.InterfaceTimerEnabled);
        Assert.Equal(2.5f, runtime.MaximumLevelPlayTimeSeconds);
        Assert.Equal(75, runtime.InterfaceTimerRemainingTicks);

        for (int tick = 0; tick < 80; tick++)
        {
            runtime.Tick();
        }

        Assert.Equal(0, runtime.InterfaceTimerRemainingTicks);
    }

    [RequiresGameDataFact]
    public void EveryRetailMissionPublishesObjectivesAndLevelFlow()
    {
        IReadOnlyList<string> scripts = GameData.EnumerateFiles("objects.qvm");
        Assert.Equal(14, scripts.Count);

        foreach (string script in scripts)
        {
            GameLoop loop = new();
            LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            ResourceRegistry resources = new(new VirtualFileSystem());
            const string resourceName = "LOCAL:level/objects.qvm";
            resources.Register(resourceName, File.ReadAllBytes(script));
            LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources);
            loader.LoadScript(resourceName);

            MissionObjectiveRuntime runtime = new(root, loop.Variables);
            Assert.True(runtime.HasLevelFlow, $"{script} has no LevelFlow task.");
            Assert.True(runtime.DefinitionCount > 0, $"{script} has no DefineComputerObjective task.");
            Assert.True(runtime.StatusMessageCount > 0, $"{script} has no StatusMessage task.");
            Assert.InRange(runtime.Objectives.Count, 0, MissionObjectiveRuntime.ObjectiveSlotCount);

            if (runtime.InterfaceTimerEnabled)
            {
                Assert.True(runtime.MaximumLevelPlayTimeSeconds > 0f, $"{script} enables a zero-length timer.");
                Assert.Equal(
                    (int)MathF.Round(runtime.MaximumLevelPlayTimeSeconds * 30f, MidpointRounding.ToEven),
                    runtime.InterfaceTimerRemainingTicks);
                runtime.Tick();
                Assert.Equal(
                    (int)MathF.Round(runtime.MaximumLevelPlayTimeSeconds * 30f, MidpointRounding.ToEven) - 1,
                    runtime.InterfaceTimerRemainingTicks);
            }
            else
            {
                Assert.Null(runtime.InterfaceTimerRemainingTicks);
            }

            if (script.EndsWith(@"level1\objects.qvm", StringComparison.OrdinalIgnoreCase))
            {
                DeclaredTask gateArea = Assert.IsType<DeclaredTask>(root.Tree.FromId(200));
                Assert.True(gateArea.TryGetValue("Position", out object? rawPosition));
                runtime.Tick(Assert.IsType<OpenIGI.Core.Numerics.Vector3d>(rawPosition));

                Assert.True(loop.Variables.TryRead("AreaActivate_200.nActive", out var active));
                Assert.Equal(1, active.ToInt32());
                Assert.True(loop.Variables.TryRead("EditVariable_199.nValue", out var edit));
                Assert.Equal(1, edit.ToInt32());
            }
        }
    }
}
