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
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>A graph authored in a lift's cab rides it (<c>0x4F9770</c> reads the cab's frame as it stands).</summary>
public sealed class LiftGraphRideTests
{
    /// <summary>Mission 12's graph 30 is in elevator 1667's frame, and every node moves with the cab.</summary>
    [RequiresGameDataFact]
    public void MissionTwelvesLiftGraphRidesTheCab()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(12);

        Dictionary<int, GraphFile> graphs = NavGraphs.Read(
            Path.Combine(GameData.RequireRoot(), "missions", "location0", "level12", "graphs"),
            root,
            NullLogger.Instance);
        GraphFile lift = graphs[30];
        Assert.Equal("Elevator", root.Tree.FromId(30)?.Parent?.Type.Name);
        Assert.True(lift.InParentFrame);

        Vector3d[] before = [.. lift.Nodes.Select(node => node.Position)];
        Vector3d step = new(0.0, 0.0, 120.0);
        lift.MoveParent(step);

        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i] + step, lift.Nodes[i].Position);
        }
    }
}
