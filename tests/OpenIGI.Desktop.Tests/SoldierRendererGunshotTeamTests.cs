using System.Reflection;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Exercises the renderer's public gunshot-delivery seam with real renderer-owned brains. A player-team
/// NPC shot must take the player-origin AI gate: allies ignore it, while hostile guards still receive it.
/// </summary>
public sealed class SoldierRendererGunshotTeamTests
{
    private const double Metre = 4096.0;

    [RequiresGameDataFact]
    public void NotifyGunshotSuppressesAPlayerTeamNpcsShotForFriendlyListenersButDeliversItToHostiles()
    {
        (LoadedMission mission, LevelTask root, ResourceRegistry resources) = LoadMissionFour();
        using SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources);
        Assert.True(renderer.Load(mission, root) > 0);
        Assert.True(renderer.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0);

        Dictionary<SoldierView, AiSoldier> brains = RendererBrains(renderer);
        (SoldierView Shooter, AiSoldier ShooterBrain) shooter = Assert.Single(
            brains.Where(pair => !pair.Value.PlayerIsEnemy).Take(1)
                .Select(pair => (pair.Key, pair.Value)));
        (SoldierView Listener, AiSoldier FriendlyBrain) friendly = Assert.Single(
            brains.Where(pair => !pair.Value.PlayerIsEnemy && !ReferenceEquals(pair.Key, shooter.Shooter)).Take(1)
                .Select(pair => (pair.Key, pair.Value)));
        (SoldierView Listener, AiSoldier HostileBrain) hostile = Assert.Single(
            brains.Where(pair => pair.Value.PlayerIsEnemy).Take(1)
                .Select(pair => (pair.Key, pair.Value)));

        Vector3d origin = new(0, 0, 0);
        shooter.ShooterBrain.SeparateTo(origin);
        friendly.FriendlyBrain.SeparateTo(new Vector3d(5 * Metre, 0, 0));
        hostile.HostileBrain.SeparateTo(new Vector3d(-5 * Metre, 0, 0));

        renderer.NotifyGunshot(origin, 10 * Metre, except: shooter.Shooter);

        Assert.Equal(0, shooter.ShooterBrain.Events.PendingCount(AiEvent.Gunshot));
        Assert.Equal(0, friendly.FriendlyBrain.Events.PendingCount(AiEvent.Gunshot));
        Assert.True(hostile.HostileBrain.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    private static Dictionary<SoldierView, AiSoldier> RendererBrains(SoldierRenderer renderer) =>
        Assert.IsType<Dictionary<SoldierView, AiSoldier>>(
            typeof(SoldierRenderer)
                .GetField("_brains", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(renderer));

    private static (LoadedMission Mission, LevelTask Root, ResourceRegistry Resources) LoadMissionFour()
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
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        return (mission, root, resources);
    }
}
