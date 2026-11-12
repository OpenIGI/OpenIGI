using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi2;
using OpenIGI.Game.Igi2.World;
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
        // The level's own task classes, as the game's content registers them: a door, a lift, a mounted gun and the rest are
        // told by their class.
        OpenIGI.Game.Flow.Igi1LevelTaskTypes.Register(loop.Types);
        OpenIGI.Game.Igi1.Igi1TaskTypes.Register(loop.Types);
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

    /// <summary>Loads an I.G.I. 2 mission as the game loads it, with the mods given, for its player to be stood (<see cref="Igi2Mission.Stand"/>).</summary>
    /// <param name="missionId">The mission's id, location times ten plus level: mission one is 11.</param>
    /// <param name="mods">The mods its session runs with, or <see langword="null"/> for none.</param>
    /// <param name="difficulty">The difficulty the player and the soldiers play at.</param>
    /// <param name="root">The game's installation, or <see langword="null"/> for the test machine's.</param>
    /// <param name="logger">Where the session and the mods report, or <see langword="null"/> to discard it.</param>
    /// <returns>The mission, its world built and its player not yet stood.</returns>
    /// <remarks>
    /// The session is the game's own content and the mods', started with where both games are installed, as the host starts it;
    /// the mission is read through the game's catalogue and built by its world builder, as the menu's preview builds one.
    /// </remarks>
    public static Igi2Mission Igi2(
        int missionId, ModHarness? mods = null, AiDifficulty difficulty = AiDifficulty.Normal, string? root = null, IOpenIgiLogger? logger = null)
    {
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        string installation = root ?? GameDataSource.Igi2.RequireRoot();
        IGameTitleRuntime runtime = mods?.Extend(new Igi2Runtime()) ?? new Igi2Runtime();
        ServiceCatalog seed = new();
        seed.Provide<ITitleDataLocator>(new ModSession.InstalledGames());
        GuestTitle guest = GuestTitle.Start(runtime, installation, log, inherited: seed);
        GameLoop loop = guest.Loop;

        Igi2MissionCatalogue missions = new();
        missions.Register(loop.Natives);
        missions.Scan(guest.FileSystem, guest.Resources, loop.Natives);
        LevelTask level = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levels = new(level, loop.TaskCommands, loop.Natives, guest.Resources);
        TerrainLoader terrain = new(guest.Resources, loop.Natives);
        LoadedMission loaded = new MissionLoader(missions, guest.FileSystem, guest.Resources, levels, null, terrain, soundBank: null, runtime.Formats)
            .Load(missionId);
        new ConditionalContainerGate(loop.Variables, log).Prepare(level);

        IServiceCatalog services = guest.Session.Services;
        WorldBuildRequest world = new(
            loaded,
            level,
            guest.Resources,
            services.TryGet(out MagicObjectRegistry? magic) ? magic : null,
            log,
            services.TryGet(out VehiclePhysicsRegistry? vehicles) ? vehicles : null,
            services);
        WorldBuildResult built = new Igi2WorldBuilder().Build(world);
        return new Igi2Mission(guest, loaded, level, built, new TitlePlayerRequest(world, built, services, difficulty, Natives: loop.Natives));
    }
}
