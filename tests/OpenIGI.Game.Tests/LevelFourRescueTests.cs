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
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>Live rescue contracts for Mission 4: Harrison must walk to Josip, and Josip must act civilian.</summary>
public sealed class LevelFourRescueTests
{
    private readonly ITestOutputHelper _output;

    public LevelFourRescueTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void HarrisonTraversesPatrol725ToNode113()
    {
        (_, LevelTask root, _, _) = LoadLevel();
        AiPatrolPathRegistry paths = AiPatrolPathRegistry.Build(root);
        Assert.NotNull(paths.Find(725));
        GraphFile graph = LoadGraph();
        Assert.NotNull(graph.RouteTable);

        GraphNode node42 = graph.Nodes.First(n => n.Id == 42);
        GraphNode node113 = graph.Nodes.First(n => n.Id == 113);

        int stage = 2;
        NativeRegistry natives = TestNatives();
        natives.RegisterVariable(
            "EditVariable_752.nValue",
            new QvmIntegerVariable(() => stage, value => stage = value));
        AiScriptHost host = new(natives, NullLogger.Instance);
        QvmProgram perTask = QvmProgram.Read(File.ReadAllBytes(MissionAiPath(500)));
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(GuardPath()));

        AiSoldier harrison = new(graph, node42.Position, facing: 0f, phase: 0, seed: 7);
        harrison.SetPatrolPaths(paths);
        harrison.SetScript(host, perTask, guard);
        Assert.Equal(42, harrison.CurrentNode);

        Vector3d far = new(
            harrison.Position.X,
            harrison.Position.Y + (500 * 4096.0),
            harrison.Position.Z);
        harrison.Tick(1, far, rootMotionThisTick: 0.0, enemyAlive: false);
        harrison.Tick(2, far, rootMotionThisTick: 0.0, enemyAlive: false);
        Assert.Contains(
            harrison.LastActions,
            action => action.Name == "AIAction_Patrol" && action.Argument(0) == 725);

        // Patrol 725 opens with Delay:30, then RunTo:113 and Quit. Feed walking root motion until
        // the route drains; a stall (no progress while a route is active) fails with the node trail.
        List<int> trail = [harrison.CurrentNode];
        int stall = 0;
        Vector3d previous = harrison.Position;
        int tick = 3;
        for (; tick < 3000 && harrison.Route is not null; tick++)
        {
            harrison.Tick(tick, far, rootMotionThisTick: 175.0, enemyAlive: false);
            if (harrison.CurrentNode != trail[^1])
            {
                trail.Add(harrison.CurrentNode);
            }
            if (Vector3d.Distance(harrison.Position, previous) < 1.0)
            {
                stall++;
            }
            else
            {
                stall = 0;
            }
            previous = harrison.Position;
            Assert.True(stall < 120, $"Harrison stalled en route to node 113: {string.Join("->", trail)}");
        }

        _output.WriteLine($"trail={string.Join("->", trail)} ticks={tick}");
        Assert.Null(harrison.Route);
        double arrivalGap = Vector3d.Distance(harrison.Position, node113.Position);
        _output.WriteLine($"arrivalGapUnits={arrivalGap:0.0}");
        Assert.True(
            arrivalGap <= (node113.Radius * GraphNode.MetreInUnits) + 500.0,
            $"Harrison must reach node 113; gap={arrivalGap:0.0} units trail={string.Join("->", trail)}");
    }

    [RequiresGameDataFact]
    public void JosipKeepsTheCivilianContract()
    {
        (_, LevelTask root, _, _) = LoadLevel();

        HumanSoldierTask josip = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(428));
        Assert.Equal(0, josip.Team);
        Assert.Equal("009_02_1", josip.SoldierModel);
        Assert.DoesNotContain(josip.Children, c => c.Type is not null && c.Type.Name.StartsWith("Gun", StringComparison.Ordinal));
        HumanAiTask mind = Assert.Single(josip.Children.OfType<HumanAiTask>());
        Assert.Equal(528, mind.Id);
        Assert.Equal("AITYPE_CIVILIAN", mind.AiTypeName);
        Assert.Equal(AiArchetype.Civilian, mind.Archetype);
        Assert.Equal(1, mind.GraphId);

        int stage = 0;
        NativeRegistry natives = TestNatives();
        natives.RegisterVariable(
            "EditVariable_752.nValue",
            new QvmIntegerVariable(() => stage, value => stage = value));
        AiScriptHost host = new(natives, NullLogger.Instance);
        GraphFile graph = LoadGraph();
        QvmProgram josipScript = QvmProgram.Read(File.ReadAllBytes(MissionAiPath(528)));
        QvmProgram civilian = QvmProgram.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "common", "ai", "civilian.qvm")));

        AiSoldier brain = new(graph, graph.Nodes[0].Position, facing: 0f, phase: 0, seed: 3);
        brain.SetScript(host, josipScript, civilian);
        Vector3d far = new(
            brain.Position.X,
            brain.Position.Y + (500 * 4096.0),
            brain.Position.Z);

        // Before the extraction stage Josip takes no authored action: 528.qvm only patrols when
        // EditVariable_752 == 3, and the civilian fallback serves DefaultHandler (combat/alarm), not
        // idle. No patrol and no combat is the contract.
        brain.Tick(1, far, rootMotionThisTick: 0.0, enemyAlive: false);
        brain.Tick(2, far, rootMotionThisTick: 0.0, enemyAlive: false);
        Assert.DoesNotContain(brain.LastActions, action => action.Name == "AIAction_Patrol");
        Assert.DoesNotContain(brain.LastActions, action => action.Name is "AIAction_Combat" or "AIAction_SetCombat");
        Assert.NotEqual(AiSoldierActivity.Engaging, brain.Activity);

        // Gunfire sends a civilian panicking, never into combat.
        Vector3d shot = new(brain.Position.X + (10 * 4096.0), brain.Position.Y, brain.Position.Z);
        Assert.True(brain.HearGunshot(shot, silenced: false));
        brain.Tick(3, far, rootMotionThisTick: 0.0, enemyAlive: false);
        Assert.Contains(brain.LastActions, action => action.Name == "AIAction_RunPanicking");
        Assert.DoesNotContain(brain.LastActions, action => action.Name is "AIAction_Combat" or "AIAction_SetCombat");

        // Only the extraction stage (retail 528.qvm patrols when EditVariable_752 == 3) may patrol,
        // and it runs to the helicopter node on path 735. The panic action above spans ticks, so poll
        // until it drains and the next idle dispatch runs, mirroring the Harrison resume loop.
        stage = 3;
        bool dispatchedExtractionPatrol = false;
        for (int tick = 4; tick <= 2000 && !dispatchedExtractionPatrol; tick++)
        {
            // The panic run owns the brain until its escape node is reached, so feed walking root
            // motion exactly as the run animation would in game; standing still must never end panic.
            brain.Tick(tick, far, rootMotionThisTick: 175.0, enemyAlive: false);
            dispatchedExtractionPatrol = brain.LastActions.Any(
                action => action.Name == "AIAction_Patrol" && action.Argument(0) == 735);
        }
        Assert.True(
            dispatchedExtractionPatrol,
            "Josip must dispatch 528.qvm extraction patrol 735 once EditVariable_752 reaches 3.");
    }

    [RequiresGameDataFact]
    public void OutroStagedJosipDoubleStaysDormantUntilTheOutro()
    {
        (_, LevelTask root, _, _) = LoadLevel();

        // Heli 356 carries an outro-staged performer wearing Josip's 009_02_1 model on the enemy
        // team with a patrol archetype and a gun. It must stay gated behind 3902: visible and
        // fighting before the outro it reads as Josip armed and patrolling instead of civilian.
        HumanSoldierTask? stagedDouble = null;
        GameTask? heli = root.Tree.FromId(356);
        Assert.NotNull(heli);
        foreach (GameTask child in heli.Children)
        {
            if (child is HumanSoldierTask soldier && soldier.SoldierModel == "009_02_1")
            {
                stagedDouble = soldier;
            }
        }
        Assert.NotNull(stagedDouble);
        Assert.Equal(1, stagedDouble.Team);
        Assert.Contains(stagedDouble.Children, c => c.Type is not null && c.Type.Name.StartsWith("Gun", StringComparison.Ordinal));

        Assert.False(
            ConditionalContainerGate.IsTaskActive(stagedDouble),
            "the outro-staged Josip double must be dormant until ConditionalContainer_3902 runs");
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

    private static GraphFile LoadGraph() => GraphFile.Read(File.ReadAllBytes(Path.Combine(
        GameData.RequireRoot(), "missions", "location0", "level4", "graphs", "graph1.dat")));

    private static string MissionAiPath(int id) => Path.Combine(
        GameData.RequireRoot(), "missions", "location0", "level4", "ai", $"{id}.qvm");

    private static string GuardPath() => Path.Combine(
        GameData.RequireRoot(), "common", "ai", "guard.qvm");

    private static NativeRegistry TestNatives()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        natives.RegisterConstant("GAME_FREQUENCY", 30);
        natives.RegisterRealConstant("WORLD_METER", 4096.0);
        natives.RegisterRealConstant("MATH_PI", Math.PI);
        return natives;
    }
}
