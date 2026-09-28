using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Mission 4's "Human 1 - Watch out message trigger" (AreaActivate_158) is a CRITERIA_HUMAN1 volume,
/// and its nActive gates the M4_MSG_17 "Watch out" status message. Retail counts every registered
/// human whose team bit is in the volume's criteria mask (<c>sub_460A10</c>), so a team-1 guard fires
/// it and the team-0 player never does.
/// </summary>
public sealed class Level4WatchOutTests
{
    private const int WatchOutAreaId = 158;
    private const string WatchOutMessage = "M4_MSG_17";

    [RequiresGameDataFact]
    public void TeamOneSoldierInsideTheAreaSendsWatchOut()
    {
        (GameLoop loop, LevelTask root, MissionObjectiveRuntime runtime, Vector3d inside) = Load();
        // Every Level 4 guard starts dormant inside a ConditionalContainer the script opens later in
        // the mission; open the chosen guard's containers the way their conditions eventually would.
        HumanSoldierTask guard = Walk(root)
            .OfType<HumanSoldierTask>()
            .First(soldier => soldier.Team == 1
                && ReferenceEquals(root.Tree.FromId(soldier.Id), soldier)
                && !Ancestors(soldier).OfType<GuardGeneratorTask>().Any());
        foreach (ConditionalContainerTask container in Ancestors(guard).OfType<ConditionalContainerTask>())
        {
            Assert.True(loop.Variables.TryWrite($"{container.Name}.isRun", QvmValue.FromInt32(1)));
        }
        Assert.True(ConditionalContainerGate.IsTaskActive(guard));

        runtime.SetSoldierOccupants([new HumanOccupant(guard.Id, guard.Team, inside, IsDead: false)]);
        runtime.Tick();

        Assert.Equal(1, Active(loop));
        Assert.Contains(runtime.Log, entry => entry.TextResource == WatchOutMessage);
        Assert.Contains(runtime.DisplayedMessages, message => message.Text == WatchOutMessage);
    }

    [RequiresGameDataFact]
    public void ThePlayerInsideTheAreaDoesNotSendWatchOut()
    {
        (GameLoop loop, _, MissionObjectiveRuntime runtime, Vector3d inside) = Load();

        runtime.Tick(inside);

        Assert.Equal(0, Active(loop));
        Assert.DoesNotContain(runtime.Log, entry => entry.TextResource == WatchOutMessage);
    }

    private static (GameLoop Loop, LevelTask Root, MissionObjectiveRuntime Runtime, Vector3d Inside) Load()
    {
        GameLoop loop = new();
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        PlayerTaskTypes.RegisterAll(loop.Types);
        ResourceRegistry resources = new(new VirtualFileSystem());
        const string resourceName = "LOCAL:level4/objects.qvm";
        resources.Register(
            resourceName,
            File.ReadAllBytes(GameData.Resolve("MISSIONS/location0/LEVEL4/OBJECTS.QVM")));
        new LevelLoader(root, loop.TaskCommands, loop.Natives, resources).LoadScript(resourceName);
        new ConditionalContainerGate(loop.Variables).Prepare(root);

        MissionObjectiveRuntime runtime = new(root, loop.Variables);
        DeclaredTask area = Assert.IsType<DeclaredTask>(root.Tree.FromId(WatchOutAreaId));
        Assert.True(area.TryGetValue("Position", out object? position));
        return (loop, root, runtime, Assert.IsType<Vector3d>(position));
    }

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

    private static IEnumerable<GameTask> Ancestors(GameTask task)
    {
        for (GameTask? current = task.Parent; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }

    private static int Active(GameLoop loop)
    {
        Assert.True(loop.Variables.TryRead($"AreaActivate_{WatchOutAreaId}.nActive", out var active));
        return active.ToInt32();
    }
}
