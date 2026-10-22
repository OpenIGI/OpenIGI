using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A game loop with the retail installation mounted and the mission table scanned.</summary>
internal sealed class RetailTerrainHarness
{
    private RetailTerrainHarness(
        GameLoop loop,
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        MissionRegistry registry,
        MagicObjectRegistry magic,
        TerrainLoader terrain)
    {
        Loop = loop;
        FileSystem = fileSystem;
        Resources = resources;
        Registry = registry;
        Magic = magic;
        Terrain = terrain;
    }

    public GameLoop Loop { get; }

    public VirtualFileSystem FileSystem { get; }

    public ResourceRegistry Resources { get; }

    public MissionRegistry Registry { get; }

    public MagicObjectRegistry Magic { get; }

    public TerrainLoader Terrain { get; }

    public static RetailTerrainHarness Create()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        // Without these the level's HumanPlayer is never built and the tree simply has no player.
        PlayerTaskTypes.RegisterAll(loop.Types);

        return new RetailTerrainHarness(loop, fileSystem, resources, registry, magic, new TerrainLoader(resources, loop.Natives));
    }

    public (TerrainData Terrain, LevelTask Root) LoadMissionTree(int id)
    {
        LevelTask levelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, Loop.TaskCommands, Loop.Natives, Resources);
        LoadedMission mission = new MissionLoader(Registry, FileSystem, Resources, levelLoader, null, Terrain)
            .Load(id);

        return (mission.Terrain!, levelRoot);
    }

    public (TerrainData Terrain, TerrainSurface Surface, WorldScene Scene) LoadMission(int id)
    {
        LevelTask levelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, Loop.TaskCommands, Loop.Natives, Resources);
        LoadedMission mission = new MissionLoader(Registry, FileSystem, Resources, levelLoader, null, Terrain)
            .Load(id);

        Assert.NotNull(mission.Terrain);
        return (
            mission.Terrain,
            TerrainSurface.Build(mission.Terrain, levelRoot),
            WorldScene.Build(mission, levelRoot, Magic));
    }

    /// <summary>The middle of a level, by median rather than by bounding box.</summary>
    public static (double X, double Y, double Z) Medians(WorldScene scene)
    {
        double[] xs = new double[scene.Placements.Count];
        double[] ys = new double[scene.Placements.Count];
        double[] zs = new double[scene.Placements.Count];

        for (int i = 0; i < scene.Placements.Count; i++)
        {
            xs[i] = scene.Placements[i].Position.X;
            ys[i] = scene.Placements[i].Position.Y;
            zs[i] = scene.Placements[i].Position.Z;
        }

        Array.Sort(xs);
        Array.Sort(ys);
        Array.Sort(zs);
        return (xs[xs.Length / 2], ys[ys.Length / 2], zs[zs.Length / 2]);
    }
}
