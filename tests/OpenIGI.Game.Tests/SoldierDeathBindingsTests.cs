using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 4's combat flow reads soldier deaths straight off the script variable <c>HumanSoldier_N.isDead</c>
/// (EV 752 on guards 406-408, EV 753 on friendlies 401-402, MSG 975 on 400, MSG 985 on 428, MSG 996 on
/// 433-434). The mission runtime binds that variable as a LIVE alias fed each tick by the host, so a guard
/// killed in the NPC-vs-NPC gate fight flips its trigger exactly when it dies. This locks the binding and
/// the per-tick feed.
/// </summary>
public sealed class SoldierDeathBindingsTests
{
    private static (MissionObjectiveRuntime Objectives, GameLoop Loop) BuildMissionFour()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);
        return (objectives, loop);
    }

    private static IEnumerable<GameTask> WalkAll(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask nested in WalkAll(child))
            {
                yield return nested;
            }
        }
    }

    [RequiresGameDataFact]
    public void FriendlyAdvanceIntoInternalPlateauCompletesTheObjective()
    {
        // The objective-complete trigger, MSG 987, is
        // `EditVariable_753.nValue == 0 && AreaActivate_155.nActive`: it fires when the FRIENDLY
        // squad (team 0) is alive and one of them stands inside area 155, the internal plateau where
        // Josef waits. This locks the team-aware area count — the squad's advance, not just the
        // player walking in, must register an occupant and complete the objective.
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: runtime.TryInvokeNative);

        // Find area 155 (the internal plateau) and its centre position.
        DeclaredTask? area155 = WalkAll(root)
            .OfType<DeclaredTask>()
            .FirstOrDefault(d => d.Type?.Name == "AreaActivate" && d.Id == 155);
        Assert.NotNull(area155);
        Assert.True(area155.TryGetValue("Position", out object? positionRaw),
            "area 155 must author a Position for the friendly-advance test");
        OpenIGI.Core.Numerics.Vector3d plateau = positionRaw is OpenIGI.Core.Numerics.Vector3d v
            ? v
            : default;

        // The friendlies begin in a closed authored subtree. Reproduce the registration transition before
        // presenting friendly 400 at the plateau: the runtime retains an inactive occupant for isDead,
        // but AreaActivate only sees it once every conditional ancestor is live.
        for (GameTask? task = root.Tree.FromId(400); task is not null; task = task.Parent)
        {
            if (task is ConditionalContainerTask container)
            {
                Assert.True(loop.Variables.TryWrite($"{container.Name}.isRun", QvmValue.FromInt32(1)));
                container.IsActive = true; // registered as the level walk's rising edge would (0x4874E0)
            }
        }
        Assert.True(ConditionalContainerGate.IsTaskActive(root.Tree.FromId(400)));

        // Friendly 400 stands on the internal plateau; the squad is alive, so EditVariable_753 stays 0.
        objectives.SetSoldierOccupants([new HumanOccupant(400, 0, plateau, IsDead: false)]);
        for (int tick = 0; tick < 600; tick++)
        {
            objectives.Tick();
            if (loop.Variables.TryRead("StatusMessage_987.isSendt", out QvmValue sent) && sent.ToInt32() != 0)
            {
                return;
            }
        }

        Assert.True(
            loop.Variables.TryRead("StatusMessage_987.isSendt", out QvmValue finalSent) && finalSent.ToInt32() != 0,
            "the objective-complete message (MSG 987) must fire once a living friendly occupies the internal plateau");
    }

    [RequiresGameDataFact]
    public void SoldierIsDeadVariableReadsAliveByDefault()
    {
        (MissionObjectiveRuntime objectives, GameLoop loop) = BuildMissionFour();
        Assert.True(loop.Variables.TryRead("HumanSoldier_406.isDead", out QvmValue value),
            "HumanSoldier_406.isDead must be exported for mission 4's combat flow");
        Assert.False(value.ToInt32() != 0, "an unreported soldier is alive");
        _ = objectives;
    }

    [RequiresGameDataFact]
    public void ReportedDeadSoldierFlipsItsIsDeadVariable()
    {
        (MissionObjectiveRuntime objectives, GameLoop loop) = BuildMissionFour();
        Vector3d anywhere = new(0.0, 0.0, 0.0);

        objectives.SetSoldierOccupants(
        [
            new HumanOccupant(406, 1, anywhere, IsDead: true),
            new HumanOccupant(407, 1, anywhere, IsDead: false),
        ]);
        objectives.TickLevelState();

        Assert.True(loop.Variables.TryRead("HumanSoldier_406.isDead", out QvmValue dead),
            "HumanSoldier_406.isDead must be readable after the host reports it");
        Assert.True(dead.ToInt32() != 0, "a reported-dead soldier reads dead");
        Assert.True(loop.Variables.TryRead("HumanSoldier_407.isDead", out QvmValue alive),
            "HumanSoldier_407.isDead must be readable");
        Assert.False(alive.ToInt32() != 0, "a reported-alive soldier reads alive");
    }

    [RequiresGameDataFact]
    public void GuardDeathsDriveEditVariableGate()
    {
        (MissionObjectiveRuntime objectives, GameLoop loop) = BuildMissionFour();
        Vector3d anywhere = new(0.0, 0.0, 0.0);

        // EV 752 turns on when guards 406-408 are all dead; feeding the deaths then ticking must
        // advance EditVariable_752 past zero, exactly the trigger the gate fight must fire.
        objectives.SetSoldierOccupants(
        [
            new HumanOccupant(406, 1, anywhere, IsDead: true),
            new HumanOccupant(407, 1, anywhere, IsDead: true),
            new HumanOccupant(408, 1, anywhere, IsDead: true),
        ]);
        objectives.TickLevelState();

        Assert.True(loop.Variables.TryRead("EditVariable_752.nValue", out QvmValue gate),
            "EditVariable_752.nValue must be bound by the runtime");
        Assert.True(gate.ToInt32() > 0,
            $"EV 752's add-expression (guards 406-408 dead) must fire once the guard deaths are fed; nValue={gate.ToInt32()}");
    }
}
