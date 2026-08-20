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
/// Diagnostic: prints mission 4's authored gate wiring — every ConditionalContainer's condition and
/// transitions, every LevelTimer's Run, and every AnimTask's Run/TargetID/Initial run — from the
/// parsed level tree, exactly as the runtime sees it. Not an assertion test; run with a filter and
/// read the console output.
/// </summary>
public sealed class DiagLevel4Gates
{
    [RequiresGameDataFact]
    public void DumpMission4TaskTypeInventory()
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

        GameTask[] tasks = WalkAll(root).ToArray();
        IReadOnlyDictionary<string, int> expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["AIGraph"] = 13,
            ["AddAmmo"] = 3,
            ["AmbientArea"] = 7,
            ["AnimTask"] = 2,
            ["AreaActivate"] = 8,
            ["Binocular"] = 1,
            ["Building"] = 17,
            ["ComputerHilight"] = 10,
            ["ConditionalContainer"] = 14,
            ["ConditionalSound"] = 18,
            ["Container"] = 21,
            ["CutScene"] = 2,
            ["DefineComputerObjective"] = 1,
            ["Dirlight"] = 1,
            ["DirlightKeyframe"] = 2,
            ["Door"] = 5,
            ["Dynamic"] = 1,
            ["EditCamera"] = 41,
            ["EditRigidObj"] = 101,
            ["EditVariable"] = 4,
            ["FlatSky"] = 1,
            ["FlatSkyLayer"] = 2,
            ["GlobalLight"] = 1,
            ["GlobalLightKeyframe"] = 2,
            ["GuardGenerator"] = 4,
            ["Gun"] = 54,
            ["GunDRAGUNOV"] = 1,
            ["GunM16A2"] = 6,
            ["GunMP5SD"] = 1,
            ["GunSPAS12"] = 5,
            ["Heli"] = 2,
            ["HumanAI"] = 50,
            ["HumanPlayer"] = 1,
            ["HumanPlayerInput"] = 1,
            ["HumanSoldier"] = 50,
            ["LODSettings"] = 1,
            ["Level"] = 1,
            ["LevelFlow"] = 1,
            ["LevelTimer"] = 4,
            ["LightmapInfo"] = 15,
            ["MipMapControl"] = 1,
            ["PatrolPath"] = 47,
            ["PatrolPathCommand"] = 162,
            ["RainEffect"] = 1,
            ["Static"] = 5,
            ["StationaryGun"] = 1,
            ["StatusMessage"] = 37,
            ["TerrainLightMap"] = 282,
            ["TextureModifier"] = 4,
        };
        Dictionary<string, int> actualCounts = tasks
            .GroupBy(task => task.Type?.Name ?? "<untyped>", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(1015, tasks.Length);
        Assert.Equal(expectedCounts.Keys.Order(StringComparer.Ordinal), actualCounts.Keys.Order(StringComparer.Ordinal));
        foreach ((string typeName, int expectedCount) in expectedCounts)
        {
            Assert.Equal(expectedCount, actualCounts[typeName]);
        }

        GuardGeneratorTask[] generators = tasks.OfType<GuardGeneratorTask>().OrderBy(task => task.Id).ToArray();
        Assert.Equal([600, 601, 602, 603], generators.Select(task => task.Id));
        Assert.All(generators, task => Assert.Equal("1", task.GenerateGuards));
        Assert.Equal([4, 3, 4, 4], generators.Select(task => task.MaxSpawns));
        Assert.Equal([416, 429, 430, 431], generators.Select(task =>
            Assert.Single(task.Children.OfType<HumanSoldierTask>()).Id));

        Console.WriteLine("type,count,declaration_only,runtime_classes,ids");
        foreach (IGrouping<string, GameTask> group in tasks
                     .GroupBy(task => task.Type?.Name ?? "<untyped>", StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            GameTask[] instances = group.OrderBy(task => task.Id).ToArray();
            string runtimeClasses = string.Join('|', instances
                .Select(task => task.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal));
            string ids = string.Join('|', instances.Select(task => task.Id));
            Console.WriteLine(
                $"{group.Key},{instances.Length},{instances[0].Type.IsDeclarationOnly},{runtimeClasses},{ids}");
        }

        string[] unusedDeclarations = loop.Types.Types
            .Where(type => type.IsDeclared && tasks.All(task => !ReferenceEquals(task.Type, type)))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"TOTAL tasks={tasks.Length} used-types={tasks.Select(task => task.Type).Distinct().Count()} "
            + $"declared-types={loop.Types.Types.Count(type => type.IsDeclared)} "
            + $"declaration-only-types={loop.Types.Types.Count(type => type.IsDeclarationOnly)}");
        Console.WriteLine($"UNUSED-DECLARATIONS {string.Join('|', unusedDeclarations)}");
        Assert.Empty(unusedDeclarations);
    }

    [RequiresGameDataFact]
    public void DumpMission4Gates()
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

        foreach (GameTask task in WalkAll(root))
        {
            if (task is ConditionalContainerTask cc)
            {
                Console.WriteLine(
                    $"CC {cc.Id} [{cc.IsRun}] cond=[{cc.Condition}] start=[{cc.RunAtStart}] stop=[{cc.RunAtStop}] " +
                    $"parent=[{task.Parent?.Id}:{task.Parent?.GetType().Name}]");
            }
            else if (task is DeclaredTask declared)
            {
                string type = declared.Type?.Name ?? "?";
                if (type is "LevelTimer" or "AnimTask" or "EditVariable" or "LevelFlow" or "CutScene")
                {
                    // The run expression is authored under the task-specific key: LevelTimer reads "On"
                    // while AnimTask reads "Run" (see CutsceneRuntime's capture pass).
                    string runKey = type == "AnimTask" ? "Run" : "On";
                    string run = declared.TryGetValue(runKey, out object? runValue) ? runValue as string ?? "" : "";
                    string target = declared.TryGetValue("TargetID", out object? targetValue) ? targetValue?.ToString() ?? "" : "";
                    string initial = declared.TryGetValue("Initial run", out object? initialValue) ? initialValue?.ToString() ?? "" : "";
                    Console.WriteLine(
                        $"TASK {declared.Id} type={type} run=[{run}] target=[{target}] initial=[{initial}] " +
                        $"parent=[{task.Parent?.Id}:{task.Parent?.GetType().Name}]");
                }
            }
        }
    }

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
}
