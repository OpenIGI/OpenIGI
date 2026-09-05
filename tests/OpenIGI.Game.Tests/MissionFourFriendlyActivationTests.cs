using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 4's friendly soldiers (tasks 400-405) sit under ConditionalContainer_300, whose
/// condition is <c>AnimTask_850.nTick &gt; 66 * GAME_FREQUENCY || this.isRun</c>. The container
/// must open once the getaway helicopter's anim task has been running for 66 seconds; until then
/// the friendlies' brains are gated and never dispatch. This test locks that retail timing chain:
/// the AnimTask nTick export, the timer-driven helicopter container, and the final friendly gate.
/// </summary>
public sealed class MissionFourFriendlyActivationTests
{
    [RequiresGameDataFact]
    public void MissionFourFriendliesActivateAfterHelicopterAnimRunsSixtySixSeconds()
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

        HumanSoldierTask friendly = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(400));
        DeclaredTask anim = Assert.IsType<DeclaredTask>(root.Tree.FromId(850));
        Assert.Equal("AnimTask", anim.Type?.Name);
        GameTask? task760 = root.Tree.FromId(760);
        System.Console.WriteLine($"  task760 type=[{task760?.Type?.Name}] runtimeType=[{task760?.GetType().Name}]");

        // The friendlies must start dormant: the intro/heli-area containers still gate them.
        Assert.False(ConditionalContainerGate.IsTaskActive(friendly));

        int[] window = [3000, 4000, 4900, 5100, 5300, 5600];
        int cursor = 0;
        for (int tick = 0; tick <= 5600; tick++)
        {
            // Keep the mission objectives (which own the ConditionalContainerGate variable exports)
            // in step with the cutscene runtime so container conditions can time off AnimTask_850.nTick.
            objectives.TickLevelState();
            runtime.Tick();
            objectives.TickAfterLevelState();
            if (cursor < window.Length && tick == window[cursor])
            {
                bool nTickReads = loop.Variables.TryRead("AnimTask_850.nTick", out OpenIGI.Scripting.Runtime.QvmValue nTick);
                bool sceneDone = loop.Variables.TryRead("CutScene_1000.isFinished", out OpenIGI.Scripting.Runtime.QvmValue done);
                bool c3900 = loop.Variables.TryRead("ConditionalContainer_3900.isRun", out OpenIGI.Scripting.Runtime.QvmValue r3900);
                bool c3901 = loop.Variables.TryRead("ConditionalContainer_3901.isRun", out OpenIGI.Scripting.Runtime.QvmValue r3901);
                bool c305 = loop.Variables.TryRead("ConditionalContainer_305.isRun", out OpenIGI.Scripting.Runtime.QvmValue r305);
                bool timer = loop.Variables.TryRead("LevelTimer_760.nTick", out OpenIGI.Scripting.Runtime.QvmValue t760);
                System.Console.WriteLine(
                    $"  tick={tick} active={ConditionalContainerGate.IsTaskActive(friendly)} " +
                    $"animNTick=[{(nTickReads ? nTick.ToString() : "unresolved")}] " +
                    $"sceneDone=[{(sceneDone ? done.ToString() : "unresolved")}] " +
                    $"3900=[{(c3900 ? r3900.ToString() : "unresolved")}] 3901=[{(c3901 ? r3901.ToString() : "unresolved")}] " +
                    $"305=[{(c305 ? r305.ToString() : "unresolved")}] timer760=[{(timer ? t760.ToString() : "unresolved")}]");
                cursor++;
            }
        }

        Assert.True(loop.Variables.TryRead("AnimTask_850.nTick", out OpenIGI.Scripting.Runtime.QvmValue nTickValue),
            "AnimTask_850.nTick must be exported so container 300's condition can resolve");
        Assert.True(nTickValue.ToInt32() > 66 * GameClock.DefaultTickRate,
            $"AnimTask_850.nTick must pass the 66s threshold; actual={nTickValue.ToInt32()}");
        Assert.True(ConditionalContainerGate.IsTaskActive(friendly),
            "mission 4 friendly soldiers must activate after the helicopter anim's 66s mark " +
            "(retail container 300 condition AnimTask_850.nTick > 66 * GAME_FREQUENCY)");
        System.Console.WriteLine(
            $"  activation tick log attach/rest: {string.Join(" ; ", runtime.Diagnostics.Take(12))}");
    }

    [RequiresGameDataFact]
    public void MissionFourHarrisonMindsAreFiveHundredNotTheEmptyHeliCopies()
    {
        (_, LevelTask root, _, _) = LoadLevel();
        int[] expectedMinds = [500, 501, 502, 503, 504, 505];
        for (int i = 0; i < expectedMinds.Length; i++)
        {
            HumanSoldierTask soldier = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(400 + i));
            HumanAiTask mind = Assert.Single(soldier.Children.OfType<HumanAiTask>());
            Assert.Equal(expectedMinds[i], mind.Id);
            Assert.Equal(1, mind.GraphId);
            Assert.NotNull(mind.Archetype);
        }

        Assert.IsType<HumanAiTask>(root.Tree.FromId(540));
        QvmProgram empty = QvmProgram.Read(File.ReadAllBytes(MissionAiPath(540)));
        Assert.Empty(empty.SymbolNames);

        QvmProgram harrison = QvmProgram.Read(File.ReadAllBytes(MissionAiPath(500)));
        Assert.Contains(harrison.SymbolNames, name => name == "AIAction_Patrol");
        Assert.Contains(harrison.SymbolNames, name => name == "AIFunction_DefaultHandler");
        Assert.Contains(harrison.SymbolNames, name => name == "EditVariable_752.nValue");
    }

    [RequiresGameDataFact]
    public void MissionFourHarrisonFiveHundredScriptPatrolsThenDefaultHandlesCombat()
    {
        (_, LevelTask root, _, _) = LoadLevel();
        AiPatrolPathRegistry paths = AiPatrolPathRegistry.Build(root);
        int stage = 0;
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        natives.RegisterConstant("GAME_FREQUENCY", 30);
        natives.RegisterRealConstant("WORLD_METER", 4096.0);
        natives.RegisterRealConstant("MATH_PI", Math.PI);
        // 500.qvm IDLE reads this level export; unbound it faults and produces no patrol.
        natives.RegisterVariable(
            "EditVariable_752.nValue",
            new QvmIntegerVariable(() => stage, value => stage = value));
        AiScriptHost host = new(natives, NullLogger.Instance);
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level4", "graphs", "graph1.dat")));
        AiSoldier soldier = new(graph, graph.Nodes[0].Position, facing: 0f, phase: 0, seed: 1);
        QvmProgram perTask = QvmProgram.Read(File.ReadAllBytes(MissionAiPath(500)));
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "common", "ai", "guard.qvm")));
        soldier.SetPatrolPaths(paths);
        soldier.SetScript(host, perTask, guard);

        Vector3d far = new(soldier.Position.X, soldier.Position.Y + (500 * 4096.0), soldier.Position.Z);
        soldier.Tick(1, far, rootMotionThisTick: 0.0);
        Assert.True(soldier.IsInvulnerable, "500.qvm CREATE calls SetInvulnerability(TRUE)");

        soldier.Tick(2, far, rootMotionThisTick: 0.0);
        Assert.Contains(soldier.LastActions, action => action.Name == "AIAction_Patrol" && action.Argument(0) == 700);

        Vector3d shot = new(soldier.Position.X + (10 * 4096.0), soldier.Position.Y, soldier.Position.Z);
        Assert.True(soldier.HearGunshot(shot, silenced: false));
        soldier.Tick(3, far, rootMotionThisTick: 0.0);
        soldier.Tick(4, far, rootMotionThisTick: 0.0);
        Assert.Contains(soldier.LastActions, action => action.Name is "AIAction_Combat" or "AIAction_SetCombat");

        // Renderer target selection must report the dead opposing NPC, not the still-alive same-team
        // player. That one handoff is what lets the brain leave combat and re-enter the authored IDLE
        // dispatch. Stage 2 is retail's post-Josip gate, whose 500.qvm action is path 725.
        // The same brain must immediately resume its Stage 2 rescue patrol after its NPC target
        // disappears. This is the Harrison regression: resetting a separate new brain would not
        // catch a renderer that kept feeding the vanished hostile as a live combat target.
        stage = 2;
        soldier.Tick(5, far, rootMotionThisTick: 0.0, enemyAlive: false);
        Assert.NotEqual(AiSoldierActivity.Engaging, soldier.Activity);
        bool resumedRescuePatrol = false;
        for (int tick = 6; tick <= 30 && !resumedRescuePatrol; tick++)
        {
            soldier.Tick(tick, far, rootMotionThisTick: 0.0, enemyAlive: false);
            resumedRescuePatrol = soldier.LastActions.Any(
                action => action.Name == "AIAction_Patrol" && action.Argument(0) == 725);
        }
        Assert.True(resumedRescuePatrol,
            "Harrison must leave combat and re-dispatch 500.qvm Stage 2 patrol 725 to Josip.");

        AiSoldier postJosip = new(graph, graph.Nodes[0].Position, facing: 0f, phase: 0, seed: 2);
        postJosip.SetPatrolPaths(paths);
        postJosip.SetScript(host, perTask, guard);
        postJosip.Tick(10, far, rootMotionThisTick: 0.0, enemyAlive: false);
        postJosip.Tick(11, far, rootMotionThisTick: 0.0, enemyAlive: false);
        Assert.Contains(postJosip.LastActions, action => action.Name == "AIAction_Patrol" && action.Argument(0) == 725);

        AiPatrolPath path725 = Assert.IsType<AiPatrolPath>(paths.Find(725));
        Assert.Contains(path725.Commands, command =>
            command.Kind == AiPatrolCommandKind.RunTo && command.Operand == 113);
    }

    [RequiresGameDataFact]
    public void MissionFourFriendlyPatrolsAdvanceOnTheLiveLocationZeroDeathGate()
    {
        // Retail Location 0, level 4 carries the patrol transition in two QVMs. objects.qvm's
        // EditVariable_752 advances on 406–408 first, then 417–419; 500.qvm's next IDLE dispatch
        // selects paths 711 and 725 respectively. This deliberately does not tick the 45s/66s Bell
        // containers: the test isolates the post-activation death gate that used to leave the squad
        // standing still long after its opposing guards had died.
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime objectives, CutsceneRuntime _) = LoadLevel();
        AiPatrolPathRegistry paths = AiPatrolPathRegistry.Build(root);
        AiScriptHost host = new(loop.Natives, NullLogger.Instance);
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level4", "graphs", "graph1.dat")));
        QvmProgram friendlyScript = QvmProgram.Read(File.ReadAllBytes(MissionAiPath(500)));
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "common", "ai", "guard.qvm")));
        Vector3d anywhere = default;
        objectives.SetSoldierOccupants(
        [
            new HumanOccupant(406, 1, anywhere, IsDead: true),
            new HumanOccupant(407, 1, anywhere, IsDead: true),
            new HumanOccupant(408, 1, anywhere, IsDead: true),
        ]);
        objectives.TickLevelState();

        Assert.True(loop.Variables.TryRead("EditVariable_752.nValue", out QvmValue afterFirstGuardLine));
        Assert.Equal(1, afterFirstGuardLine.ToInt32());

        // The next friendly state tick reads the live level variable, rather than a disconnected local
        // test value, and dispatches retail 500.qvm's first post-guard patrol. A new brain is the
        // public simulation seam here: during its first tick AI creation and its no-event IDLE dispatch
        // run in the same state update.
        AiSoldier firstAdvanceFriendly = NewFriendly(graph, paths, host, friendlyScript, guard, seed: 1);
        Vector3d far = new(
            firstAdvanceFriendly.Position.X,
            firstAdvanceFriendly.Position.Y + (500 * 4096.0),
            firstAdvanceFriendly.Position.Z);
        firstAdvanceFriendly.Tick(1, far, rootMotionThisTick: 0.0, enemyAlive: false);
        AiProducedAction firstAdvance = Assert.Single(
            firstAdvanceFriendly.LastActions,
            action => action.Name == "AIAction_Patrol");
        Assert.Equal(711, firstAdvance.Argument(0));

        objectives.SetSoldierOccupants(
        [
            new HumanOccupant(406, 1, anywhere, IsDead: true),
            new HumanOccupant(407, 1, anywhere, IsDead: true),
            new HumanOccupant(408, 1, anywhere, IsDead: true),
            new HumanOccupant(417, 1, anywhere, IsDead: true),
            new HumanOccupant(418, 1, anywhere, IsDead: true),
            new HumanOccupant(419, 1, anywhere, IsDead: true),
        ]);
        objectives.TickLevelState();

        Assert.True(loop.Variables.TryRead("EditVariable_752.nValue", out QvmValue afterSecondGuardLine));
        Assert.Equal(2, afterSecondGuardLine.ToInt32());

        // 500.qvm maps stage two to patrol 725. The authored path's first movement is the run toward
        // node 113, so this is the exact rescue-building advance instead of merely checking a counter.
        AiSoldier secondAdvanceFriendly = NewFriendly(graph, paths, host, friendlyScript, guard, seed: 2);
        secondAdvanceFriendly.Tick(2, far, rootMotionThisTick: 0.0, enemyAlive: false);
        AiProducedAction secondAdvance = Assert.Single(
            secondAdvanceFriendly.LastActions,
            action => action.Name == "AIAction_Patrol");
        Assert.Equal(725, secondAdvance.Argument(0));
        AiPatrolPath path725 = Assert.IsType<AiPatrolPath>(paths.Find(725));
        Assert.Contains(path725.Commands, command =>
            command.Kind == AiPatrolCommandKind.RunTo && command.Operand == 113);
    }

    [RequiresGameDataFact]
    public void MissionFourExtractAreaAndOutroCutsceneExportRetailNames()
    {
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime objectives, CutsceneRuntime _) = LoadLevel();
        Assert.True(loop.Variables.TryRead("AreaActivate_156.nActive", out QvmValue area156));
        Assert.Equal(0, area156.ToInt32());
        Assert.True(loop.Variables.TryRead("EditVariable_752.nValue", out QvmValue ev752));
        Assert.Equal(0, ev752.ToInt32());
        Assert.True(loop.Variables.TryRead("CutScene_2222.nTick", out QvmValue nTick));
        Assert.True(nTick.ToInt32() >= 0);

        GameTask? cut = root.Tree.FromId(2222);
        Assert.NotNull(cut);
        Assert.True(cut is CutSceneTask, "2222 is the outro CutScene Guys-running times off");
        GameTask? extract = root.Tree.FromId(3902);
        Assert.IsType<ConditionalContainerTask>(extract);
        Assert.Contains("AreaActivate_156.nActive", ((ConditionalContainerTask)extract).Condition);
        GameTask? running = root.Tree.FromId(2344);
        Assert.IsType<ConditionalContainerTask>(running);
        Assert.Contains("CutScene_2222.nTick", ((ConditionalContainerTask)running).Condition);
        _ = objectives;
    }

    [RequiresGameDataFact]
    public void MissionFourShipsThirteenNavGraphsAndLoaderDoesNotClaimTheyAreMissing()
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
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        Assert.DoesNotContain(mission.PendingSteps, step => step.Contains("AI navigation graphs", StringComparison.Ordinal));
        string graphs = Path.Combine(GameData.RequireRoot(), "missions", "location0", "level4", "graphs");
        Assert.Equal(13, Directory.GetFiles(graphs, "graph*.dat").Length);
    }

    private static (GameLoop Loop, LevelTask Root, MissionObjectiveRuntime Objectives, CutsceneRuntime Runtime)
        LoadLevel()
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
        return (loop, root, objectives, runtime);
    }

    private static string MissionAiPath(int id) => Path.Combine(
        GameData.RequireRoot(), "missions", "location0", "level4", "ai", $"{id}.qvm");

    private static AiSoldier NewFriendly(
        GraphFile graph,
        AiPatrolPathRegistry paths,
        AiScriptHost host,
        QvmProgram script,
        QvmProgram guard,
        int seed)
    {
        AiSoldier friendly = new(graph, graph.Nodes[0].Position, facing: 0f, phase: 0, seed: seed);
        friendly.SetPatrolPaths(paths);
        friendly.SetScript(host, script, guard);
        return friendly;
    }
}
