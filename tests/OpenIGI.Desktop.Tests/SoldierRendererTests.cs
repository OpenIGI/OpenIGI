using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

public sealed class SoldierRendererTests
{
    [RequiresGameDataFact]
    public void LivingSoldiersRemainQueryTargetsBeforeHealthIsCreated()
    {
        (VirtualFileSystem fileSystem, ResourceRegistry resources, LoadedMission mission, LevelTask levelRoot) =
            LoadRetailMission();
        RecordingGraphicsDevice device = new();
        using SoldierRenderer renderer = new(device, resources);

        Assert.True(renderer.Load(mission, levelRoot) > 0);
        Assert.True(renderer.Simulation.Animate(new Dictionary<int, GraphFile>()) > 0);

        IReadOnlyList<Vector3d> contacts = renderer.Simulation.MapContacts();
        Assert.NotEmpty(contacts);

        Vector3d center = contacts[0];
        Assert.True(renderer.Simulation.TryProximity(
            center,
            (float)(HumanHitTest.BodyRadiusInUnits * 2d),
            out HumanProximityTarget proximity));
        Assert.Equal(0d, proximity.Distance, precision: 5);

        Vector3d origin = new(
            center.X - (HumanHitTest.BodyRadiusInUnits * 2d),
            center.Y,
            center.Z + (HumanHitTest.BodyHeightInUnits * 0.5d));
        Vector3d end = new(
            center.X + (HumanHitTest.BodyRadiusInUnits * 2d),
            center.Y,
            center.Z + (HumanHitTest.BodyHeightInUnits * 0.5d));
        Assert.True(renderer.Simulation.TryIntersect(origin, end, out _));
    }

    private static (VirtualFileSystem FileSystem, ResourceRegistry Resources, LoadedMission Mission, LevelTask LevelRoot)
        LoadRetailMission()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(1);
        return (fileSystem, resources, mission, level.LevelRoot);
    }
}
