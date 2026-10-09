using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's AI as data: the tuning out of <c>settings.qvm</c> (<c>HumanAIConfig_LoadSettings</c>, <c>igi2 0x41E5B0</c>), the
/// graphs (<c>Graph_ParseGraphStream</c>, <c>0x59B4B0</c>), the soldiers, their minds and squads, and the patrols.
/// </summary>
public sealed class RetailIgi2AiDataTests
{
    /// <summary>The script made every type's base row and every difficulty's scale row, with their values.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_tuning_is_read()
    {
        RetailIgi2LevelLoadTests.Harness harness = Harness();
        Igi2HumanAiConfig config = harness.Session.Services.Require<Igi2HumanAiConfig>();

        Assert.Equal(6, config.BaseRows.Count);
        Assert.Equal(3, config.ScaleRows.Count);
        Assert.All(config.BaseRows, static r => Assert.True(r.IsBaseData));
        Assert.All(config.ScaleRows, static r => Assert.False(r.IsBaseData));
        Assert.Equal(
            Igi2AiConstants.AiTypes,
            config.BaseRows.Select(static r => r.AiTypeName));
        Assert.Equal(
            ["DIFF_SCALE_GD_1", "DIFF_SCALE_GD_2", "DIFF_SCALE_GD_3"],
            config.ScaleRows.Select(static r => r.AiTypeName));

        Igi2HumanAiConfigItemTask normal = config.BaseRows[0];
        Assert.Equal((5f, 2.5f, 0.1f), (normal.DangerTimeout, normal.InquireTimeout, normal.DetectionTimeDistanceFactor));
        Assert.Equal((50f, 10f, 5f, 20f, 100f), (normal.MinRangeAccuracy, normal.MedRangeAccuracy, normal.MaxRangeAccuracy, normal.AccuracyMedRange, normal.AccuracyMaxRange));
        Assert.Equal((0.8f, 0.015f, 3f), (normal.CloseCombatDamage, normal.HitRecoilThreshold, normal.DetectionToAttackTime));
        Assert.Equal((0.55f, 100f), (normal.DamageMultipliers[0], normal.DamageMultipliers[16]));

        Igi2HumanAiConfigItemTask easy = config.ScaleRows[0];
        Assert.Equal((1f, 1.4f, 2f, 0.45f, 0.75f), (easy.DangerTimeout, easy.InquireTimeout, easy.DetectionTimeDistanceFactor, easy.MinRangeAccuracy, easy.MedRangeAccuracy));
        Assert.Equal(0.3f, easy.HitPoints);
    }

    /// <summary>
    /// A soldier's tuning is its base times the difficulty's scale in the AI's units (<c>HumanAIConfig_SetAISettingsFromConfig</c>,
    /// <c>igi2 0x41E8B0</c>): an easy C1 soldier keeps a third of its life.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Tuning_scales_by_difficulty()
    {
        Igi2HumanAiConfig config = Harness().Session.Services.Require<Igi2HumanAiConfig>();
        Igi2AiTweaks t = config.Tweaks(0, 0)!;

        Assert.Equal((5f, 3.49999976f), (t.DangerTimeout, t.InquireTimeout));
        Assert.Equal(0.2f, t.DetectionTimeDistanceFactor, 6);
        Assert.Equal(0.3f, t.HitPoints, 6);
        Assert.Equal((81920f, 409600f), (t.AccuracyMedRange, t.AccuracyMaxRange));
        Assert.Equal(0.225f, t.MinRangeAccuracy, 6);
        Assert.Equal(0.075f, t.MedRangeAccuracy, 6);
        Assert.Equal(90f, t.DetectionToAttackTime);
        Assert.Equal(120f, t.GrenadeDetonationTimer);
        Assert.InRange(t.AnimSpeedFactor, 0.8f, 1.2f);
        Assert.Equal((float)Math.Pow(t.MedRangeAccuracy / t.MinRangeAccuracy, 1.0 / t.AccuracyMedRange), t.MedRangeAccuracyFactor, 6);
        Assert.Null(config.Tweaks(6, 0));
        Assert.Null(config.Tweaks(0, 3));
    }

    /// <summary>
    /// Every graph is read whole: as many nodes and links as its editor counted in <c>Graphdata</c>, ids under the I.G.I. 2 limit,
    /// door nodes as <c>NODECRITERIA_DOOR</c>, and each link measured.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_graphs_are_read()
    {
        RetailIgi2LevelLoadTests.Harness harness = Harness();
        Igi2AiGraphs graphs = Graphs(harness);
        Igi2AiGraphTask[] tasks = [.. RetailIgi2Mission1.All(harness.LevelRoot).OfType<Igi2AiGraphTask>()];

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], tasks.Select(static t => t.Id).Order());
        Assert.Equal(7, graphs.Graphs.Count);
        foreach (Igi2AiGraphTask task in tasks)
        {
            Igi2AiGraph graph = graphs.Graph(task.Id)!;
            Assert.Equal(((int)task.GraphData[0], (int)task.GraphData[2]), (graph.Nodes.Count, graph.Links.Count));
            Assert.Empty(graph.SkippedFields);
            Assert.All(graph.Nodes.Values, static n => Assert.InRange(n.Id, 0, Igi2AiGraph.IdLimit - 1));
            Assert.All(graph.Links, static l => Assert.True(l.Length > 0f));
        }

        Igi2AiGraph first = graphs.Graph(1)!;
        Assert.True(first.Nodes.Count > 999, "the graph was cut at I.G.I. 1's limit");
        Igi2AiGraphNode[] doors = [.. graphs.Graphs.Values.SelectMany(static g => g.Nodes.Values).Where(static n => n.CriteriaText.Length > 0)];
        Assert.NotEmpty(doors);
        Assert.All(doors, static n => Assert.Equal(("NODECRITERIA_DOOR", 2), (n.CriteriaText, n.Criteria)));
        Assert.Contains(first.Nodes.Values, static n => n.Terrain != 0);
        Assert.Contains(first.Nodes.Values, static n => n.LightValue > 0f);

        Igi2AiGraphLink link = first.Links[0];
        Igi2AiGraphNode a = first.Node(link.NodeA)!, b = first.Node(link.NodeB)!;
        Assert.Contains(link, a.Links);
        Assert.Contains(link, b.Links);
        Assert.Equal(Distance(a, b), link.Length, 0.01f);
    }

    /// <summary>
    /// The soldiers are typed, each with one mind naming a graph the level has, and resolve to C1 riflemen and pistol carriers;
    /// the squads keep their soldiers.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_soldiers_and_squads_are_read()
    {
        RetailIgi2LevelLoadTests.Harness harness = Harness();
        GameTask[] tree = [.. RetailIgi2Mission1.All(harness.LevelRoot)];
        Igi2HumanSoldierTask[] soldiers = [.. tree.OfType<Igi2HumanSoldierTask>()];
        Igi2AiSquadTask[] squads = [.. tree.OfType<Igi2AiSquadTask>()];
        HashSet<int> graphIds = [.. tree.OfType<Igi2AiGraphTask>().Select(static g => g.Id)];
        Igi2ScriptFunctions functions = harness.Session.Services.Require<Igi2ScriptFunctions>();

        Assert.Equal(53, soldiers.Length);
        Assert.Equal(53, tree.OfType<Igi2HumanAiTask>().Count());
        Assert.Equal(21, squads.Length);
        foreach (Igi2HumanSoldierTask soldier in soldiers)
        {
            Igi2HumanAiTask ai = Assert.Single(soldier.Children.OfType<Igi2HumanAiTask>());
            Assert.Same(ai, soldier.Ai);
            Assert.Contains(ai.GraphId, graphIds);
            Assert.Matches("^re?sg[123]_1$", soldier.Model);
            Assert.Equal(1, soldier.Team);
            ai.ResolveTypes(harness.Loop.Natives, functions);
            Assert.InRange(ai.AiType, 0, Igi2AiConstants.AiTypes.Count - 1);
            Assert.Equal(Igi2AiConstants.AiTypes.ToList().IndexOf(ai.AiTypeName), ai.AiType);
            Assert.Equal(Igi2AiConstants.AnimTypes.ToList().IndexOf(ai.AnimTypeName), ai.AnimType);
        }

        Assert.Equal(53, squads.Sum(static s => s.Soldiers.Count()));
        Assert.All(squads, static s => Assert.InRange(s.FormationDistance, 0f, 10f));
        Assert.Contains(squads, static s => (s.AlarmTriggerId, s.AlarmControlId) == (102, 100));
    }

    /// <summary>
    /// Every patrol's commands are known ones, and each node a walk or run names is in one of the level's graphs.
    /// </summary>
    /// <remarks>
    /// Not necessarily the patrol's own <c>Graph ID</c>: the game registers no parameters for a <c>PatrolPath</c>
    /// (<c>PatrolPath_Open</c>, <c>igi2 0x51BB70</c>) and walks the soldier's graph (<c>HumanAI_MoveCharacterToNode</c>,
    /// <c>0x462860</c>). Six of mission 1's patrols name graph 2, of 50 nodes, and walk nodes past 100.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_patrols_are_read()
    {
        RetailIgi2LevelLoadTests.Harness harness = Harness();
        Igi2AiGraphs graphs = Graphs(harness);
        Igi2PatrolPathTask[] paths = [.. RetailIgi2Mission1.All(harness.LevelRoot).OfType<Igi2PatrolPathTask>()];

        Assert.Equal(34, paths.Length);
        Assert.Equal(936, paths.Sum(static p => p.Commands.Count));
        foreach (Igi2PatrolPathCommandTask command in paths.SelectMany(static p => p.Commands))
        {
            Assert.InRange(command.CommandValue, -1, 11);
            if (command.Command is Igi2PatrolCommand.WalkTo or Igi2PatrolCommand.RunTo)
            {
                Assert.Contains(graphs.Graphs.Values, g => g.Node(command.Parameter) is not null);
            }
        }

        Assert.Equal(6, paths.Count(p => p.GraphId == 2 && p.Commands.Any(c => c.Command is Igi2PatrolCommand.WalkTo or Igi2PatrolCommand.RunTo && graphs.Graph(2)!.Node(c.Parameter) is null)));

        Assert.Contains(paths.SelectMany(static p => p.Commands), static c => (c.Command, c.Parameter) == (Igi2PatrolCommand.Delay, 300));
    }

    private static float Distance(Igi2AiGraphNode a, Igi2AiGraphNode b) =>
        (float)Math.Sqrt(Math.Pow(a.Position.X - b.Position.X, 2) + Math.Pow(a.Position.Y - b.Position.Y, 2) + Math.Pow(a.Position.Z - b.Position.Z, 2));

    private static RetailIgi2LevelLoadTests.Harness Harness()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(level.Directory, "objects.qvm"), loadTables: true);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
        return harness;
    }

    private static Igi2AiGraphs Graphs(RetailIgi2LevelLoadTests.Harness harness)
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        return Igi2AiGraphs.Build(
            harness.LevelRoot, new ResourceRegistry(files), harness.Loop.Natives, harness.Session.Services.Require<Igi2ScriptFunctions>());
    }
}
