using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The menu's backdrop shows every section a level streams in by area, and Play puts the level back as a cold load
/// finds it.
/// </summary>
public sealed class BackdropAreaTests
{
    [RequiresGameDataFact]
    public void TheLastMissionsHallsAreInTheBackdropAndOutOfAColdStart()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        OpenIGI.Game.Flow.LevelTaskTypes.Register(loop.Types);
        OpenIGI.Game.Igi1.Igi1TaskTypes.Register(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(14);
        WorldScene scene = WorldScene.Build(mission, root, magic, destroyedModelArchiveName: new Igi1Quirks().DestroyedModelArchiveName);

        var gate = new ConditionalContainerGate(loop.Variables);
        gate.Prepare(root);
        ConditionalContainerTask[] containers = [.. Descendants(root).OfType<ConditionalContainerTask>()];
        (bool Run, bool Active)[] cold = [.. containers.Select(c => (c.IsRun, c.IsActive))];
        int coldHidden = Hidden(scene);

        Action undo = gate.OpenEveryArea(root);
        int backdropHidden = Hidden(scene);

        // Every hall and tunnel waits on an area; with them all occupied, nearly all of the level is in.
        Assert.True(coldHidden > 1000, $"a cold start hides {coldHidden}");
        Assert.True(backdropHidden < coldHidden / 5, $"the backdrop still hides {backdropHidden} of {coldHidden}");

        // A container that waits on anything but an area stays as the level starts: no objective is done for it.
        ConditionalContainerTask hacked = Assert.Single(containers, c => c.Condition.Trim() == "Terminal_300.isHacked");
        Assert.False(hacked.IsActive);

        undo();
        Assert.Equal(coldHidden, Hidden(scene));
        Assert.Equal(cold, containers.Select(c => (c.IsRun, c.IsActive)));

        static int Hidden(WorldScene scene) => scene.Placements.Count(p => !ConditionalContainerGate.IsTaskActive(p.Owner));
    }

    private static IEnumerable<GameTask> Descendants(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }
}
