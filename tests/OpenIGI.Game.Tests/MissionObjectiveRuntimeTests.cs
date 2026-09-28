using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks objective/LevelFlow extraction against all fourteen retail mission scripts.</summary>
public sealed class MissionObjectiveRuntimeTests
{
    [Fact]
    public void VehicleCriteriaCountsCarsButNotHumans()
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
                QvmExpression.Text("Vehicle volume"),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(20f), QvmExpression.Real(20f), QvmExpression.Real(20f),
                QvmExpression.Text("CRITERIA_VEHICLE"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);
        runtime.TickLevelState(new Vector3d(0d, 0d, 0d), playerTeam: 0, extraPlayerPosition: null, playerAlive: true);
        Assert.Equal(0, ReadActive());

        runtime.SetVehicleOccupants([new VehicleOccupant(900, Vector3d.Zero)]);
        runtime.TickLevelState(new Vector3d(0d, 0d, 0d), playerTeam: 0, extraPlayerPosition: null, playerAlive: true);
        Assert.Equal(1, ReadActive());

        int ReadActive()
        {
            Assert.True(loop.Variables.TryRead("AreaActivate_900.nActive", out QvmValue value));
            return value.ToInt32();
        }
    }

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
            new HumanOccupant(904, 1, inside, IsDead: true),
            new HumanOccupant(dead.Id, 1, inside, IsDead: true),
            new HumanOccupant(999, 1, inside, IsDead: false),
        ]);

        runtime.TickLevelState();

        Assert.Equal(1, ReadActive(loop));
        Assert.False(ReadDead(loop, dormant.Id));
        Assert.True(ReadDead(loop, dead.Id));

        // 904 was deleted from the tree while still being reported as an occupant. The latch lives on
        // the task now, so a task no longer in the tree stops being fed and reads alive -- which is the
        // answer the original gives too, by a different route: it unexports on deletion
        // (Human_UnexportVars, 0x4608D0) and its preprocessor rewrites a token naming a task that does
        // not exist to the literal 0 (0x4F14E3). The port keeps the binding after a delete rather than
        // unexporting, so the symbol still resolves; only the value agrees. Nothing deletes a soldier
        // on death in play -- the renderer reports every brain precisely so a corpse keeps its latch --
        // so this is the artificial case, not the live one.
        Assert.False(ReadDead(loop, 904));

        Assert.True(loop.Variables.TryWrite("ConditionalContainer_901.isRun", QvmValue.FromInt32(1)));
        runtime.TickLevelState();

        Assert.Equal(2, ReadActive(loop));

        // Declared the way the level's own script declares one: Task_New binds the id and then exports
        // the task, which is what runs HumanTask's ExportVars and creates isDead and eTeam. Building
        // the task without that step would leave it unexported, which no soldier in a loaded level is.
        HumanSoldierTask NewSoldier(GameTask parent, int id)
        {
            HumanSoldierTask soldier = (HumanSoldierTask)parent.Tree.New(parent, parent.Tree.Types.Get("HumanSoldier"));
            Assert.True(parent.Tree.TryBindId(id, soldier));
            Assert.True(loop.Variables.Export(soldier));
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
    public void AreaActivationCountsCameraAndBodyAsOnePlayer()
    {
        (GameLoop loop, MissionObjectiveRuntime runtime) = BuildPlayerAreaRuntime();
        Vector3d inside = new(0.0, 0.0, 0.0);
        Vector3d outside = new(100.0, 0.0, 0.0);

        runtime.TickLevelState(
            inside,
            playerTeam: 0,
            extraPlayerPosition: new Vector3d(2.0, 0.0, 0.0),
            playerAlive: true);
        Assert.Equal(1, ReadPlayerAreaActive(loop));

        runtime.TickLevelState(outside, playerTeam: 0, extraPlayerPosition: inside, playerAlive: true);
        Assert.Equal(1, ReadPlayerAreaActive(loop));

        runtime.TickLevelState(inside, playerTeam: 0, extraPlayerPosition: outside, playerAlive: true);
        Assert.Equal(1, ReadPlayerAreaActive(loop));
    }

    [Fact]
    public void AreaActivationExcludesDeadPlayerProjections()
    {
        (GameLoop loop, MissionObjectiveRuntime runtime) = BuildPlayerAreaRuntime();

        runtime.TickLevelState(
            new Vector3d(0.0, 0.0, 0.0),
            playerTeam: 0,
            extraPlayerPosition: new Vector3d(2.0, 0.0, 0.0),
            playerAlive: false);

        Assert.Equal(0, ReadPlayerAreaActive(loop));
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
    public void ClearMessagesEmptiesEverySlotAndFinishesTheAuthoredMessages()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("StatusMessage"),
                QvmExpression.Text("Send"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Text"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Is send once"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Cutscene message"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Duration"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(300),
                QvmExpression.Text("StatusMessage"),
                QvmExpression.Text("Objective"),
                QvmExpression.Text("1"),
                QvmExpression.Text("OBJECTIVE"),
                QvmExpression.Int(1),
                QvmExpression.Int(0),
                QvmExpression.Real(10f)),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(301),
                QvmExpression.Text("StatusMessage"),
                QvmExpression.Text("Unsent"),
                QvmExpression.Text("0"),
                QvmExpression.Text("UNSENT"),
                QvmExpression.Int(1),
                QvmExpression.Int(0),
                QvmExpression.Real(10f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);
        Assert.True(loop.Variables.TryWrite("StatusMessage_300.nTickSendt", QvmValue.FromInt32(-211)));
        runtime.Tick();
        Assert.True(runtime.ShowTransientMessage("PICKEDUPWEAPONAK-47"));
        Assert.Equal(2, runtime.DisplayedMessages.Count());
        Assert.Equal(0, Read("StatusMessage_300.isFinishedDisplay"));

        runtime.ClearMessages();

        // 0x485D20 finishes the owning task as the hold running out would; the unsent one is untouched.
        Assert.Empty(runtime.DisplayedMessages);
        Assert.Equal(1, Read("StatusMessage_300.isFinishedDisplay"));
        Assert.Equal(Read("StatusMessage_300.nTickSendt") + 1, Read("StatusMessage_300.nFinishedDisplay"));
        Assert.Equal(0, Read("StatusMessage_300.nTicksSinceFinishedDisplay"));
        Assert.Equal(0, Read("StatusMessage_301.isFinishedDisplay"));
        Assert.Single(runtime.Log);

        runtime.Tick();

        Assert.Equal(1, Read("StatusMessage_300.nTicksSinceFinishedDisplay"));
        Assert.Empty(runtime.DisplayedMessages);
        Assert.True(runtime.ShowTransientMessage("AFTER-CLEAR"));
        Assert.Equal("AFTER-CLEAR", Assert.Single(runtime.DisplayedMessages).Text);

        int Read(string name)
        {
            Assert.True(loop.Variables.TryRead(name, out QvmValue value));
            return value.ToInt32();
        }
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

    [Fact]
    public void LevelFlowFailureBindingReachesAuthoredStatusMessageOnTheFollowingTickAndReleases()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("EditVariable"),
                QvmExpression.Text("Initial value"), QvmExpression.Text("Int32"),
                QvmExpression.Text("Add"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Sub"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Interface timer enabled"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Max level play time"), QvmExpression.Text("Real32"),
                QvmExpression.Text("Complete"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Failed"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("StatusMessage"),
                QvmExpression.Text("Send"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Text"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Is send once"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Cutscene message"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Duration"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(2),
                QvmExpression.Text("EditVariable"),
                QvmExpression.Text("Failure switch"),
                QvmExpression.Int(0),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(string.Empty)),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(17),
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Level flow"),
                QvmExpression.Int(0),
                QvmExpression.Real(0f),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text("EditVariable_2.nValue")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(29),
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Secondary level flow"),
                QvmExpression.Int(0),
                QvmExpression.Real(0f),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text("EditVariable_2.nValue")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(23),
                QvmExpression.Text("StatusMessage"),
                QvmExpression.Text("Mission Failed"),
                QvmExpression.Text("LevelFlow_17.isFailed"),
                QvmExpression.Text("MISSIONFAILED"),
                QvmExpression.Int(1),
                QvmExpression.Int(0),
                QvmExpression.Real(2f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        DeclaredTask levelFlow = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(17));
        loop.Variables.BindInt(levelFlow, "unrelated", () => 7, _ => { });
        MissionObjectiveRuntime runtime = new(loop.Tasks.Root, loop.Variables);

        Assert.True(loop.Variables.TryRead("LevelFlow_17.isFailed", out QvmValue initialFailure));
        Assert.Equal(0, initialFailure.ToInt32());
        Assert.True(loop.Variables.TryRead("LevelFlow_29.isFailed", out QvmValue initialSecondFailure));
        Assert.Equal(0, initialSecondFailure.ToInt32());
        Assert.True(loop.Variables.TryRead("LevelFlow_17.nTick", out QvmValue initialTick));
        Assert.Equal(0, initialTick.ToInt32());
        Assert.True(loop.Variables.TryWrite("StatusMessage_23.nTickSendt", QvmValue.FromInt32(-211)));
        Assert.True(loop.Variables.TryWrite("EditVariable_2.nValue", QvmValue.FromInt32(1)));

        runtime.Tick();

        Assert.True(runtime.IsFailed);
        Assert.True(loop.Variables.TryRead("LevelFlow_17.isFailed", out QvmValue failureAfterFlow));
        Assert.Equal(1, failureAfterFlow.ToInt32());
        Assert.True(loop.Variables.TryRead("LevelFlow_29.isFailed", out QvmValue secondFailureAfterFlow));
        Assert.Equal(1, secondFailureAfterFlow.ToInt32());
        Assert.True(loop.Variables.TryRead("LevelFlow_17.isComplete", out QvmValue completeAfterFlow));
        Assert.Equal(0, completeAfterFlow.ToInt32());
        Assert.True(loop.Variables.TryRead("LevelFlow_17.nTick", out QvmValue tickAfterFlow));
        Assert.Equal(1, tickAfterFlow.ToInt32());
        Assert.True(new LevelExpressionRuntime(loop.Variables).EvaluateCondition("LevelFlow_17.isFailed", emptyValue: false));
        Assert.True(loop.Variables.TryRead("StatusMessage_23.isSendt", out QvmValue sentBeforeBinding));
        Assert.Equal(0, sentBeforeBinding.ToInt32());

        runtime.Tick();

        Assert.True(loop.Variables.TryRead("StatusMessage_23.isSendt", out QvmValue sentAfterBinding));
        Assert.Equal(1, sentAfterBinding.ToInt32());

        runtime.Release();

        Assert.False(loop.Variables.TryRead("LevelFlow_17.isFailed", out _));
        Assert.False(loop.Variables.TryRead("LevelFlow_17.isComplete", out _));
        Assert.False(loop.Variables.TryRead("LevelFlow_17.nTick", out _));
        Assert.False(loop.Variables.TryRead("LevelFlow_17.eDifficulty", out _));
        Assert.False(loop.Variables.TryRead("LevelFlow_29.isFailed", out _));
        Assert.False(loop.Variables.TryRead("Key0.isPressed", out _));
        Assert.True(loop.Variables.TryRead("LevelFlow_17.unrelated", out QvmValue unrelated));
        Assert.Equal(7, unrelated.ToInt32());
    }

    [Fact]
    public void LevelFlowStoresItsResultTheWayTheOriginalUpdateDoes()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("EditVariable"),
                QvmExpression.Text("Initial value"), QvmExpression.Text("Int32"),
                QvmExpression.Text("Add"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Sub"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Interface timer enabled"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Max level play time"), QvmExpression.Text("Real32"),
                QvmExpression.Text("Complete"), QvmExpression.Text("VarString"),
                QvmExpression.Text("Failed"), QvmExpression.Text("VarString")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(2),
                QvmExpression.Text("EditVariable"),
                QvmExpression.Text("Complete switch"),
                QvmExpression.Int(0),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(string.Empty)),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(3),
                QvmExpression.Text("EditVariable"),
                QvmExpression.Text("Failure switch"),
                QvmExpression.Int(0),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(string.Empty)),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(10),
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Level flow"),
                QvmExpression.Int(0),
                QvmExpression.Real(0f),
                QvmExpression.Text("EditVariable_2.nValue / 2"),
                QvmExpression.Text("EditVariable_3.nValue")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(11),
                QvmExpression.Text("LevelFlow"),
                QvmExpression.Text("Scripted flow"),
                QvmExpression.Int(0),
                QvmExpression.Real(0f),
                QvmExpression.Text(string.Empty),
                QvmExpression.Text(string.Empty))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        HashSet<KeyCode> held = [KeyCode.D0, KeyCode.D7];
        MissionObjectiveRuntime runtime = new(
            loop.Tasks.Root, loop.Variables, difficulty: 2, isKeyDown: held.Contains);

        // 0x488ED0 copies the profile difficulty; the number row is sampled only by an update.
        Assert.Equal(2, Read(loop, "LevelFlow_10.eDifficulty"));
        Assert.Equal(0, Read(loop, "Key7.isPressed"));

        // 1 / 2 is 0.5 as the original's float evaluation sees it, and a byte stores it as zero.
        Assert.True(loop.Variables.TryWrite("EditVariable_2.nValue", QvmValue.FromInt32(1)));
        runtime.Tick();
        Assert.Equal(0, Read(loop, "LevelFlow_10.isComplete"));
        Assert.Equal(-128, Read(loop, "Key0.isPressed"));
        Assert.Equal(-128, Read(loop, "Key7.isPressed"));
        Assert.Equal(0, Read(loop, "Key1.isPressed"));

        Assert.True(loop.Variables.TryWrite("EditVariable_2.nValue", QvmValue.FromInt32(2)));
        runtime.Tick();
        Assert.Equal(1, Read(loop, "LevelFlow_10.isComplete"));
        Assert.False(runtime.IsComplete); // The runtime answers for the last LevelFlow, LevelFlow_11.

        // A LevelFlow with empty slots leaves a script's write alone, and a failure clears completion.
        Assert.True(loop.Variables.TryWrite("LevelFlow_11.isComplete", QvmValue.FromInt32(1)));
        runtime.Tick();
        Assert.True(runtime.IsComplete);
        Assert.True(loop.Variables.TryWrite("LevelFlow_11.isFailed", QvmValue.FromInt32(1)));
        runtime.Tick();
        Assert.True(runtime.IsFailed);
        Assert.False(runtime.IsComplete);
        Assert.Equal(4, Read(loop, "LevelFlow_11.nTick"));

        Assert.True(loop.Variables.TryWrite("EditVariable_3.nValue", QvmValue.FromInt32(1)));
        runtime.Tick();
        Assert.Equal(1, Read(loop, "LevelFlow_10.isFailed"));
        Assert.Equal(0, Read(loop, "LevelFlow_10.isComplete"));

        runtime.Release();
        for (int digit = 0; digit < 10; digit++)
        {
            Assert.False(loop.Variables.TryRead($"Key{digit}.isPressed", out _));
        }
    }

    private static int Read(GameLoop loop, string symbol)
    {
        Assert.True(loop.Variables.TryRead(symbol, out QvmValue value), symbol);
        return value.ToInt32();
    }

    [RequiresGameDataFact]
    public void EveryRetailMissionPublishesObjectivesAndLevelFlow()
    {
        IReadOnlyList<string> scripts = GameData.EnumerateFiles("objects.qvm");
        Assert.Equal(14, scripts.Count);
        int failureMessageCount = 0;
        int tickReaderCount = 0;

        foreach (string script in scripts)
        {
            GameLoop loop = new();
            LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            ResourceRegistry resources = new(new VirtualFileSystem());
            const string resourceName = "LOCAL:level/objects.qvm";
            resources.Register(resourceName, File.ReadAllBytes(script));
            LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources);
            loader.LoadScript(resourceName);

            List<DeclaredTask> levelFlows = Walk(root)
                .OfType<DeclaredTask>()
                .Where(task => string.Equals(task.Type.Name, "LevelFlow", StringComparison.Ordinal))
                .ToList();
            MissionObjectiveRuntime runtime = new(root, loop.Variables);
            Assert.True(runtime.HasLevelFlow, $"{script} has no LevelFlow task.");
            Assert.NotEmpty(levelFlows);
            Assert.True(runtime.DefinitionCount > 0, $"{script} has no DefineComputerObjective task.");
            Assert.True(runtime.StatusMessageCount > 0, $"{script} has no StatusMessage task.");
            Assert.InRange(runtime.Objectives.Count, 0, MissionObjectiveRuntime.ObjectiveSlotCount);

            foreach (DeclaredTask levelFlow in levelFlows)
            {
                foreach (string field in LevelFlowFields)
                {
                    Assert.True(loop.Variables.TryRead($"{levelFlow.Name}.{field}", out _));
                }
            }

            // Levels 6 and 14 time messages off LevelFlow_10.nTick; an unbound one reads zero forever.
            tickReaderCount += Walk(root)
                .OfType<DeclaredTask>()
                .Count(task => task.Type.Parameters
                    .Select((_, index) => task.GetValue(index))
                    .OfType<string>()
                    .Any(text => levelFlows.Any(levelFlow => text.Contains(
                        $"{levelFlow.Name}.nTick", StringComparison.Ordinal))));

            failureMessageCount += Walk(root)
                .OfType<DeclaredTask>()
                .Count(message => string.Equals(message.Type.Name, "StatusMessage", StringComparison.Ordinal)
                    && levelFlows.Any(levelFlow => String(message, "Send").Contains(
                        $"{levelFlow.Name}.isFailed", StringComparison.Ordinal)));

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

            runtime.Release();
            foreach (DeclaredTask levelFlow in levelFlows)
            {
                foreach (string field in LevelFlowFields)
                {
                    Assert.False(loop.Variables.TryRead($"{levelFlow.Name}.{field}", out _));
                }
            }
        }

        Assert.True(failureMessageCount > 0, "No retail StatusMessage reads a LevelFlow isFailed field.");
        Assert.True(tickReaderCount >= 2, "Levels 6 and 14 should both read a LevelFlow nTick field.");
    }

    /// <summary>The LevelFlow fields retail's export handler (0x488F90) binds on the task itself.</summary>
    private static readonly string[] LevelFlowFields = ["isComplete", "isFailed", "nTick", "eDifficulty"];

    private static string String(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text.TrimEnd('\r', '\n') : string.Empty;

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    private static (GameLoop Loop, MissionObjectiveRuntime Runtime) BuildPlayerAreaRuntime()
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
                QvmExpression.Text("Player activation volume"),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Real(20f), QvmExpression.Real(20f), QvmExpression.Real(20f),
                QvmExpression.Text("PLAYER"))));
        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        return (loop, new MissionObjectiveRuntime(loop.Tasks.Root, loop.Variables));
    }

    private static int ReadPlayerAreaActive(GameLoop loop)
    {
        Assert.True(loop.Variables.TryRead("AreaActivate_900.nActive", out QvmValue active));
        return active.ToInt32();
    }
}
