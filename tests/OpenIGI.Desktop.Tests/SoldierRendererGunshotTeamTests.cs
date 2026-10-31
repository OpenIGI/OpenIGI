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
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using System.Reflection;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Rendering.Headless;

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
        using Scenario scenario = CreateScenario();
        (SoldierView Shooter, Igi1HumanAi ShooterBrain) shooter = scenario.PlayerTeamSoldier;
        (SoldierView Listener, Igi1HumanAi FriendlyBrain) friendly = scenario.OtherPlayerTeamSoldier;
        (SoldierView Listener, Igi1HumanAi HostileBrain) hostile = scenario.HostileSoldier;

        PlaceInHearingRange(shooter.ShooterBrain, friendly.FriendlyBrain, hostile.HostileBrain);
        scenario.Renderer.Simulation.NotifyGunshot(Vector3d.Zero, 10 * Metre, except: shooter.Shooter);

        Assert.Equal(0, shooter.ShooterBrain.Events.PendingCount(AiEvent.Gunshot));
        Assert.Equal(0, friendly.FriendlyBrain.Events.PendingCount(AiEvent.Gunshot));
        Assert.True(hostile.HostileBrain.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    [RequiresGameDataFact]
    public void NotifyGunshotDeliversAHostileNpcsShotToFriendlyListenersButExcludesTheShooter()
    {
        using Scenario scenario = CreateScenario();
        (SoldierView Shooter, Igi1HumanAi ShooterBrain) shooter = scenario.HostileSoldier;
        (SoldierView Listener, Igi1HumanAi FriendlyBrain) friendly = scenario.PlayerTeamSoldier;

        PlaceInHearingRange(shooter.ShooterBrain, friendly.FriendlyBrain);
        scenario.Renderer.Simulation.NotifyGunshot(Vector3d.Zero, 10 * Metre, except: shooter.Shooter);

        Assert.Equal(0, shooter.ShooterBrain.Events.PendingCount(AiEvent.Gunshot));
        Assert.True(friendly.FriendlyBrain.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    [RequiresGameDataFact]
    public void NotifyGunshotWithoutAShooterStillUsesThePlayerTeamGate()
    {
        using Scenario scenario = CreateScenario();
        (SoldierView Listener, Igi1HumanAi FriendlyBrain) friendly = scenario.PlayerTeamSoldier;
        (SoldierView Listener, Igi1HumanAi HostileBrain) hostile = scenario.HostileSoldier;

        PlaceInHearingRange(friendly.FriendlyBrain, hostile.HostileBrain);
        scenario.Renderer.Simulation.NotifyGunshot(Vector3d.Zero, 10 * Metre);

        Assert.Equal(0, friendly.FriendlyBrain.Events.PendingCount(AiEvent.Gunshot));
        Assert.True(hostile.HostileBrain.Events.PendingCount(AiEvent.Gunshot) > 0);
    }

    private static void PlaceInHearingRange(params Igi1HumanAi[] soldiers)
    {
        for (int index = 0; index < soldiers.Length; index++)
        {
            soldiers[index].SeparateTo(new Vector3d(index * 5 * Metre, 0, 0));
        }
    }

    private static Scenario CreateScenario()
    {
        (LoadedMission mission, LevelTask root, ResourceRegistry resources) = LoadMissionFour();
        SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources);
        Assert.True(renderer.Load(mission, root) > 0);
        Assert.True(renderer.Simulation.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0);

        Dictionary<SoldierView, Igi1HumanAi> brains = RendererBrains(renderer);
        (SoldierView Shooter, Igi1HumanAi ShooterBrain) shooter = Assert.Single(
            brains.Where(pair => !pair.Value.PlayerIsEnemy).Take(1)
                .Select(pair => (pair.Key, pair.Value)));
        (SoldierView Listener, Igi1HumanAi FriendlyBrain) friendly = Assert.Single(
            brains.Where(pair => !pair.Value.PlayerIsEnemy && !ReferenceEquals(pair.Key, shooter.Shooter)).Take(1)
                .Select(pair => (pair.Key, pair.Value)));
        (SoldierView Listener, Igi1HumanAi HostileBrain) hostile = Assert.Single(
            brains.Where(pair => pair.Value.PlayerIsEnemy).Take(1)
                .Select(pair => (pair.Key, pair.Value)));
        return new Scenario(renderer, shooter, friendly, hostile);
    }

    private static Dictionary<SoldierView, Igi1HumanAi> RendererBrains(SoldierRenderer renderer) => renderer.Simulation.Brains;

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

    private sealed class Scenario(
        SoldierRenderer renderer,
        (SoldierView Shooter, Igi1HumanAi ShooterBrain) playerTeamSoldier,
        (SoldierView Listener, Igi1HumanAi FriendlyBrain) otherPlayerTeamSoldier,
        (SoldierView Listener, Igi1HumanAi HostileBrain) hostileSoldier) : IDisposable
    {
        public SoldierRenderer Renderer { get; } = renderer;

        public (SoldierView Shooter, Igi1HumanAi ShooterBrain) PlayerTeamSoldier { get; } = playerTeamSoldier;

        public (SoldierView Listener, Igi1HumanAi FriendlyBrain) OtherPlayerTeamSoldier { get; } = otherPlayerTeamSoldier;

        public (SoldierView Listener, Igi1HumanAi HostileBrain) HostileSoldier { get; } = hostileSoldier;

        public void Dispose() => Renderer.Dispose();
    }
}
