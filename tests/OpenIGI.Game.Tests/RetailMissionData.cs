using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>A retail mission loaded as the game loads it: its level tree, terrain, models and vehicle types.</summary>
internal sealed class RetailMissionData
{
    private static readonly Dictionary<int, RetailMissionData> Loaded = [];

    private RetailMissionData(LoadedMission mission, LevelTask root, VehiclePhysicsRegistry vehicles, MagicObjectRegistry magic)
    {
        Magic = magic;
        Mission = mission;
        Root = root;
        Vehicles = vehicles;
        TerrainData terrain = mission.Terrain ?? throw new InvalidOperationException("no terrain");
        TerrainRay = new RetailTerrainRay(
            terrain.Octree,
            terrain.Commands,
            TerrainDiscardBinding.For(terrain).Regions,
            TerrainHeightBindings.Bind(terrain, root));
        Objects = RetailWorldObjects.Build(root, name => mission.TryGetModel(name, out MefModel? model) ? model : null);
    }

    public LoadedMission Mission { get; }

    public LevelTask Root { get; }

    public VehiclePhysicsRegistry Vehicles { get; }

    public MagicObjectRegistry Magic { get; }

    public RetailTerrainRay TerrainRay { get; }

    public RetailWorldObjects Objects { get; }

    public static RetailMissionData Get(int id)
    {
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(id, out RetailMissionData? data))
            {
                data = Load(id);
                Loaded[id] = data;
            }

            return data;
        }
    }

    private static RetailMissionData Load(int id)
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
        PlayerTaskTypes.RegisterAll(loop.Types);
        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader, null, terrainLoader).Load(id);
        VehiclePhysicsRegistry vehicles = new();
        vehicles.Load(resources);
        return new RetailMissionData(mission, levelRoot, vehicles, magic);
    }
}
