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
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The soldiers and the player update where the level walk reaches them, so every human registered in the world
/// must sit where the walk goes: under containers, cars, cut-scenes and generators that walk their children,
/// never under a task whose update does not.
/// </summary>
public sealed class LevelWalkReachesHumansTests
{
    public static TheoryData<int> Missions => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14];

    [RequiresGameDataTheory]
    [MemberData(nameof(Missions))]
    public void EveryLiveHumanIsReachedByTheWalk(int mission)
    {
        (GameLoop loop, LevelTask root) = Load(mission);
        CutsceneRuntime runtime = new(root, loop.Variables);
        HashSet<GameTask> reached = new(ReferenceEqualityComparer.Instance);
        List<HumanTask> humans = [.. Descend(root).OfType<HumanTask>()];
        Assert.NotEmpty(humans);
        foreach (HumanTask human in humans)
        {
            runtime.Walker.Bind(human, task =>
            {
                reached.Add(task);
                runtime.Walker.RunChildren(task);
            });
        }

        // The player's container 560 is not the same on every mission; the first ticks cover the intro's hand-off.
        for (int tick = 0; tick < 90; tick++)
        {
            reached.Clear();
            runtime.Tick();
            runtime.FinishExpressionTick();
            foreach (HumanTask human in humans)
            {
                if (ConditionalContainerGate.IsTaskActive(human))
                {
                    Assert.True(
                        reached.Contains(human),
                        $"mission {mission} tick {tick}: {human.Name} is registered but the walk did not reach it "
                            + $"({string.Join(" <- ", Ancestors(human).Select(task => task.Type.Name))})");
                }
            }
        }
    }

    private static IEnumerable<GameTask> Ancestors(GameTask task)
    {
        for (GameTask? current = task.Parent; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }

    private static IEnumerable<GameTask> Descend(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Descend(child))
            {
                yield return nested;
            }
        }
    }

    private static (GameLoop Loop, LevelTask Root) Load(int mission)
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
        _ = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(mission);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        return (loop, root);
    }
}
