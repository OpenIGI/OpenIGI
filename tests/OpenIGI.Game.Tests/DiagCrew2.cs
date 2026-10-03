using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using OpenIGI.Formats.World;
using OpenIGI.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

public sealed class DiagCrew2
{
    private readonly ITestOutputHelper _output;

    public DiagCrew2(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void Dump()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        ConditionalContainerGate gate = new(loop.Variables);
        int dormant = gate.Prepare(root);
        _output.WriteLine($"=== dormant containers: {dormant} ===");

        // Count every task type name so we see HumanPlayer / HumanSoldierFemale / Heli tasks the
        // soldier walk never touches.
        Dictionary<string, int> typeCounts = new(StringComparer.Ordinal);
        CountTypes(root, typeCounts);
        foreach (KeyValuePair<string, int> pair in typeCounts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"  TYPE {pair.Key}: {pair.Value}");
        }

        int index = 0;
        foreach (HumanSoldierTask soldier in Walk(root))
        {
            index++;
            bool hasSet = mission.TryGetAnimationSet(soldier.BoneHierarchy, out _);
            string active = ConditionalContainerGate.IsTaskActive(soldier) ? "ACTIVE" : "INACTIVE";
            string ancestry = Ancestry(soldier);
            HumanAiTask? mind = soldier.Children.OfType<HumanAiTask>().FirstOrDefault();
            string mindText = mind is null
                ? "no-mind"
                : $"mind={mind.AiTypeName} graph={mind.GraphId} mindId={mind.Id} arche={mind.Archetype}";
            int pathCommands = CountPathCommands(soldier);
            _output.WriteLine(
                $"  [{index}] {active} task={soldier.Name} model={soldier.Model} id={soldier.Id} standAnim={soldier.StandAnimation} boneHier={soldier.BoneHierarchy} animSet={hasSet} " +
                $"{mindText} pathCmds={pathCommands} {ancestry}");
        }

        _output.WriteLine($"=== total HumanSoldierTask: {index} ===");

        // What are the friendlies' containers gated on, and what do they read?
        foreach (HumanSoldierTask soldier in Walk(root))
        {
            if (soldier.Id is >= 400 and <= 405)
            {
                for (GameTask? a = soldier; a is not null; a = a.Parent)
                {
                    if (a is ConditionalContainerTask cc)
                    {
                        _output.WriteLine(
                            $"  container {cc.Id}: condition=[{cc.Condition}] runAtStart=[{cc.RunAtStart}] runAtStop=[{cc.RunAtStop}] isRun={cc.IsRun}");
                    }
                    else
                    {
                        _output.WriteLine($"  ancestor {a.Name} runtimeType={a.GetType().Name} declared={a is DeclaredTask} id={a.Id}");
                    }
                }

                HumanAiTask? mind = soldier.Children.OfType<HumanAiTask>().FirstOrDefault();
                _output.WriteLine($"  soldier {soldier.Id} mindId={mind?.Id} 400-siblings: " +
                    string.Join(", ", soldier.Parent?.Children.Where(c => c is HumanSoldierTask).Select(c => c.Id) ?? []));
                break;
            }
        }

        // Print every ConditionalContainer's condition expression, marking the friendlies' chain.
        int ccCount = 0;
        foreach (GameTask task in WalkAll(root))
        {
            if (task is not DeclaredTask cc)
            {
                continue;
            }

            string typeName = task.Type?.Name ?? "(null-type)";
            if (!string.Equals(typeName, "ConditionalContainer", StringComparison.Ordinal))
            {
                continue;
            }

            ccCount++;
            string condition = cc.TryGetValue("Condition", out object? c) && c is string cs ? cs : "(none)";
            string runAtStart = cc.TryGetValue("Run at start", out object? r) && r is string rs ? rs : "";
            _output.WriteLine($"  CC {cc.Id}: condition=[{condition.TrimEnd('\r', '\n')}] runAtStart=[{runAtStart.TrimEnd('\r', '\n')}]");
        }

        // Ancestry of every soldier whose mind id is in the friendly set (400-405, 540-544, 1234-1238).
        foreach (HumanSoldierTask soldier in Walk(root))
        {
            int? mindId = soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id;
            if (soldier.Id is >= 400 and <= 405 || mindId is >= 540 and <= 544 or >= 1234 and <= 1238)
            {
                _output.WriteLine($"  soldier {soldier.Id} mind {mindId} ancestry: {Ancestry(soldier)}");
            }
        }

        // Find tasks with the heli-crew mind ids directly.
        foreach (GameTask task in WalkAll(root))
        {
            if (task.Id is >= 540 and <= 544 or >= 1234 and <= 1238)
            {
                GameTask? p = task.Parent;
                _output.WriteLine($"  TASK {task.Id} type={task.Type?.Name} runtime={task.GetType().Name} parent={p?.Type?.Name}_{p?.Id} ancestry={Ancestry(task)}");
            }
        }

        // Where does AnimTask_850 sit, and how long is its anim program?
        foreach (GameTask task in WalkAll(root))
        {
            if (task is DeclaredTask dt && dt.Id == 850 && string.Equals(task.Type?.Name, "AnimTask", StringComparison.Ordinal))
            {
                _output.WriteLine($"  AnimTask_850 ancestry: {Ancestry(dt)}");
                _output.WriteLine($"  AnimTask_850 Run=[{DiagText(dt, "Run")}] InitialRun={DiagBool(dt, "Initial run")} TargetID={DiagInt(dt, "TargetID", -1)}");
                if (dt.TryGetValue("AnimData", out object? data) && data is System.Collections.IEnumerable entries)
                {
                    int n = 0;
                    foreach (object? _ in entries) { n++; }
                    _output.WriteLine($"  AnimTask_850 AnimData entries: {n}");
                }
            }
        }

        // Where is area 156 (the heli-356 crew trigger) and where does the player start?
        foreach (GameTask task in WalkAll(root))
        {
            if (task.Id == 156 && task is DeclaredTask dt156)
            {
                _output.WriteLine($"  AREA156 type={task.Type?.Name} ancestry={Ancestry(task)}");
                foreach (string key in new[] { "Position", "Dimensions", "Criteria" })
                {
                    _output.WriteLine($"    {key} = {(dt156.TryGetValue(key, out object? v) ? v : "(none)")}");
                }
            }
        }
        // The authored node list of every patrol path 700-705, so the intended walk geometry is known.
        foreach (GameTask task in WalkAll(root))
        {
            if (task.Id is >= 700 and <= 705 && string.Equals(task.Type?.Name, AiTaskTypes.PatrolPath, StringComparison.Ordinal))
            {
                var nodes = new List<string>();
                foreach (GameTask cmd in task.Children)
                {
                    if (cmd is PatrolPathCommandTask step)
                    {
                        nodes.Add($"{step.Step.Kind}:{step.Step.Operand}");
                    }
                }

                _output.WriteLine($"  PatrolPath {task.Id}: [{string.Join(" | ", nodes)}]");
            }

            if (task.Id == 2600 && string.Equals(task.Type?.Name, AiTaskTypes.PatrolPath, StringComparison.Ordinal))
            {
                var nodes = new List<string>();
                foreach (GameTask cmd in task.Children)
                {
                    if (cmd is PatrolPathCommandTask step)
                    {
                        nodes.Add($"{step.Step.Kind}:{step.Step.Operand}");
                    }
                }

                _output.WriteLine($"  PatrolPath 2600 (blocker): [{string.Join(" | ", nodes)}]");
            }
        }

        // The authored position of every friendly soldier and its mind.
        foreach (HumanSoldierTask soldier in Walk(root))
        {
            int? mindId = soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id;
            if (soldier.Id is >= 400 and <= 405 || mindId is >= 540 and <= 544 or >= 1234 and <= 1238)
            {
                _output.WriteLine($"  SOLDIER {soldier.Id} mind {mindId} pos={soldier.Position.X / 4096.0:0.000},{soldier.Position.Y / 4096.0:0.000}");
            }

            if (soldier.Id == 666)
            {
                foreach (ConditionalContainerTask cc in WalkAll(root).OfType<ConditionalContainerTask>())
                {
                    if (cc.Id == 3900)
                    {
                        _output.WriteLine($"  CC3900 condition: [{cc.Condition}] runAtStart=[{cc.RunAtStart}] runAtStop=[{cc.RunAtStop}]");
                    }
                }
            }

            if (soldier.Id == 400)
            {
                _output.WriteLine($"  FRIENDLY400 chain: {AncestorChain(soldier.Parent)}");
            }

            if (soldier.Id == 666 || soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id is 540 or 1234)
            {
                _output.WriteLine($"  CHAIN soldier {soldier.Id} mind {soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id}");
                _output.WriteLine($"    {AncestorChain(soldier.Parent)}");
            }

            if (soldier.Id == 666)
            {
                _output.WriteLine($"  BLOCKER soldier 666 mind {mindId} pos={soldier.Position.X / 4096.0:0.000},{soldier.Position.Y / 4096.0:0.000}");
                _output.WriteLine($"  BLOCKER chain: {AncestorChain(soldier.Parent)}");
            }
        }

        // The GraphId each friendly mind and the blocker's mind walks.
        foreach (GameTask task in WalkAll(root))
        {
            if (task is not HumanAiTask ai || task.Id is not (>= 500 and <= 505 or 3400 or >= 540 and <= 544 or >= 1234 and <= 1238))
            {
                continue;
            }

            _output.WriteLine($"  MIND {task.Id} graphId={ai.GraphId} type={ai.AiTypeName}");
        }
    }

    private static void CountTypes(GameTask task, Dictionary<string, int> counts)
    {
        string name = task.Type?.Name ?? task.GetType().Name;
        counts[name] = counts.GetValueOrDefault(name) + 1;
        foreach (GameTask child in task.Children)
        {
            CountTypes(child, counts);
        }
    }

    private static string AncestorChain(GameTask? task)
    {
        var chain = new List<string>();
        for (GameTask? cur = task; cur is not null; cur = cur.Parent)
        {
            string label = $"{cur.GetType().Name}#{cur.Id}";
            if (cur is ConditionalContainerTask cc)
            {
                label += $"[{(cc.IsRun ? "RUN" : "dormant")}][{(cc.Condition ?? "<none>")}]";
            }

            chain.Insert(0, label);
        }

        return string.Join(" > ", chain);
    }

    private static int CountPathCommands(GameTask task)
    {
        int count = 0;
        foreach (GameTask child in task.Children)
        {
            if (string.Equals(child.Type?.Name, AiTaskTypes.PatrolPath, StringComparison.Ordinal))
            {
                foreach (GameTask cmd in child.Children)
                {
                    if (string.Equals(cmd.Type?.Name, AiTaskTypes.PatrolPathCommand, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static string DiagText(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? v) && v is string s ? s.TrimEnd('\r', '\n') : "(none)";

    private static bool DiagBool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? v) && v is bool b && b;

    private static int DiagInt(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? v) && v is int i ? i : fallback;

    private static IEnumerable<GameTask> WalkAll(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask nested in WalkAll(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<HumanSoldierTask> Walk(GameTask task)
    {
        if (task is HumanSoldierTask soldier)
        {
            yield return soldier;
        }

        foreach (GameTask child in task.Children)
        {
            foreach (HumanSoldierTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static string Ancestry(GameTask task)
    {
        List<string> names = [];
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            names.Add(current is DeclaredTask d ? d.Name : current.GetType().Name);
        }

        return "ancestry=" + string.Join(" > ", names);
    }
}
