using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// No retail level hangs a task off a <c>StatusMessage</c>: the type's run handler (<c>0x485890</c>) runs no child, so a
/// level that did would never update it.
/// </summary>
public sealed class StatusMessageChildrenCensusTests
{
    [RequiresGameDataFact]
    public void No_retail_status_message_has_a_child()
    {
        List<string> parents = [];
        int messages = 0;
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            foreach (GameTask task in Load(missionId).Descendants())
            {
                if (task is not Igi1StatusMessageTask)
                {
                    continue;
                }

                messages++;
                if (task.Children.Any())
                {
                    parents.Add($"mission {missionId}: {task.Name} ({string.Join(", ", task.Children.Select(static c => c.Type.Name))})");
                }
            }
        }

        Assert.True(messages > 100, $"Only {messages} status messages were read.");
        Assert.Empty(parents);
    }

    private static LevelTask Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        OpenIGI.Game.Flow.LevelTaskTypes.Register(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        _ = new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return root;
    }
}
