using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail installation mounted under a fresh game loop, with the cutscene task types registered, a level
/// root made and the mission table scanned: the state every cutscene runtime test loads its mission into.
/// </summary>
/// <remarks>
/// Every call to <see cref="Open"/> builds its own state, so no test shares anything with another. The
/// steps a test takes after opening, such as loading the magic objects or the vehicle physics, stay in the
/// test in the order it takes them, because what runs before the mission load can change what it builds.
/// </remarks>
internal sealed class RetailMissionFixture
{
    private RetailMissionFixture(
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        GameLoop loop,
        LevelTask root,
        MissionRegistry missions)
    {
        FileSystem = fileSystem;
        Resources = resources;
        Loop = loop;
        Root = root;
        Missions = missions;
    }

    /// <summary>The file system with the retail installation mounted.</summary>
    public VirtualFileSystem FileSystem { get; }

    /// <summary>The resources read through <see cref="FileSystem"/>.</summary>
    public ResourceRegistry Resources { get; }

    /// <summary>The game loop whose task types, natives and variables the mission loads into.</summary>
    public GameLoop Loop { get; }

    /// <summary>The level task the mission's tree is built under.</summary>
    public LevelTask Root { get; }

    /// <summary>The scanned mission table.</summary>
    public MissionRegistry Missions { get; }

    /// <summary>Mounts the installation and gets everything ready for a mission load.</summary>
    public static RetailMissionFixture Open()
    {
        VirtualFileSystem fs = new(); fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs); GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new(); missions.Register(loop.Natives); missions.Scan(fs, resources, loop.Natives);
        return new RetailMissionFixture(fs, resources, loop, root, missions);
    }

    /// <summary>Registers the magic-object natives and loads the retail magic objects.</summary>
    public MagicObjectRegistry LoadMagic()
    {
        MagicObjectRegistry magic = new(); magic.Register(Loop.Natives); magic.Load(Resources, Loop.Natives);
        return magic;
    }

    /// <summary>Loads a mission's tree under <see cref="Root"/>, without its terrain.</summary>
    public LoadedMission Load(int missionId) => new MissionLoader(Missions, FileSystem, Resources,
        new LevelLoader(Root, Loop.TaskCommands, Loop.Natives, Resources)).Load(missionId);

    /// <summary>Loads a mission's tree under <see cref="Root"/>, with its terrain.</summary>
    public LoadedMission LoadWithTerrain(int missionId) => new MissionLoader(
        Missions,
        FileSystem,
        Resources,
        new LevelLoader(Root, Loop.TaskCommands, Loop.Natives, Resources),
        terrainLoader: new TerrainLoader(Resources, Loop.Natives)).Load(missionId);

    /// <summary>Every task under <paramref name="root"/>, depth first, parents before their children.</summary>
    public static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static void RegisterCutsceneTypes(TaskTypeRegistry types)
    {
        PlayerTaskTypes.RegisterAll(types);
        types.Register<CutSceneTask>(CutSceneTask.TypeName);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
    }
}
