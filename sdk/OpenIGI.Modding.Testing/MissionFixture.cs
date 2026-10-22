using OpenIGI.Core.Diagnostics;
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

namespace OpenIGI.Modding.Testing;

/// <summary>Missions loaded headless from a game's installation, for a mod's tests to build their level systems on.</summary>
/// <remarks>
/// A mission is loaded as the game's mission loader loads one, with no window, no device and no host: its tasks, its terrain,
/// its placed models and its graphs. Nothing of the mission runs; a test spawns what it tests into the level and ticks it.
/// </remarks>
public static class MissionFixture
{
    /// <summary>Loads a Project I.G.I. mission.</summary>
    /// <param name="missionId">The mission's number, one to fourteen.</param>
    /// <param name="root">The installation, or <see langword="null"/> for the one <c>IGI_GAME_PATH</c> names.</param>
    /// <returns>The mission.</returns>
    /// <remarks>A test that loads one asks for the installation with <see cref="RequiresGameDataFactAttribute"/>.</remarks>
    public static Igi1Mission Igi1(int missionId, string? root = null)
    {
        string installation = root ?? GameDataSource.Igi1.RequireRoot();
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(installation));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        TerrainLoader terrain = new(resources, loop.Natives);
        LevelTask level = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levels = new(level, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levels, null, terrain).Load(missionId);
        string directory = Path.Combine(installation, mission.Descriptor.MissionDirectory);
        Dictionary<int, GraphFile> graphs = NavGraphs.Read(Path.Combine(directory, "graphs"), level, NullLogger.Instance);
        return new Igi1Mission(loop, fileSystem, resources, mission, level, WorldScene.Build(mission, level, magic), directory, graphs);
    }
}
