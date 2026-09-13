// Dump the authored PatrolPath task ids in a level, to confirm the friendlies' paths exist.
using System;
using System.Linq;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class DiagPatrolPaths
{
    [RequiresGameDataFact]
    public void Dump()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new(); magic.Register(loop.Natives); magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);

        AiPatrolPathRegistry registry = AiPatrolPathRegistry.Build(root);
        Console.WriteLine($"total paths: {registry.Count}");
        foreach (AiPatrolPath path in registry.Paths.OrderBy(p => p.Id))
        {
            Console.WriteLine($"  path {path.Id}: {path.Commands.Count} commands");
        }

        // Every task named PatrolPath, with or without an id, plus any task whose id is 240 or 243.
        int allPatrol = 0;
        foreach (GameTask task in WalkAll(root))
        {
            bool named = string.Equals(task.Type?.Name, "PatrolPath", StringComparison.Ordinal) || task is PatrolPathTask;
            if (task.Id is 240 or 243)
            {
                Console.WriteLine($"  ID240/243 task: id={task.Id} type={task.Type?.Name} parent={task.Parent?.Type?.Name}");
            }

            if (named)
            {
                allPatrol++;
                if (allPatrol <= 60 && task.Id is not (>= 700 and <= 745) and not 2600)
                {
                    Console.WriteLine($"  patrol-other: id={task.Id} type={task.Type?.Name} parent={task.Parent?.Type?.Name}");
                }
            }
        }

        Console.WriteLine($"total PatrolPath tasks: {allPatrol}");

        // EditVariable tasks: what does 751/752 start at, and what changes them?
        foreach (GameTask task in WalkAll(root))
        {
            if (task is not DeclaredTask declared || !string.Equals(task.Type?.Name, "EditVariable", StringComparison.Ordinal))
            {
                continue;
            }

            if (declared.Id is 751 or 752 || declared.Id >= 740)
            {
                Console.WriteLine(
                    $"  EditVariable {declared.Id}: initial={Int(declared, "Initial value", 0)} add={Text(declared, "Add")} sub={Text(declared, "Sub")}");
            }
        }
    }

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text.TrimEnd('\r', '\n') : string.Empty;

    private static int Int(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) && value is int number ? number : fallback;

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
