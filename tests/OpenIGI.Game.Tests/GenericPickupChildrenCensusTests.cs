using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// No retail level hangs a task off a <c>GenericPickup</c>: the type's run handler (<c>0x46C3C0</c>) runs no child, so a
/// level that did would never update it.
/// </summary>
public sealed class GenericPickupChildrenCensusTests
{
    [RequiresGameDataFact]
    public void No_retail_generic_pickup_has_a_child()
    {
        List<string> parents = [];
        int pickups = 0;
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            foreach (GameTask task in Load(missionId).Descendants())
            {
                if (task is not GenericPickupTask pickup)
                {
                    continue;
                }

                pickups++;
                Assert.NotNull(pickup.Type);
                if (task.Children.Any())
                {
                    parents.Add($"mission {missionId}: {task.Name} ({string.Join(", ", task.Children.Select(static c => c.Type.Name))})");
                }
            }
        }

        Assert.True(pickups > 0, "No generic pickup was read.");
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
