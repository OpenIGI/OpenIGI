using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Loads retail level 1 and finds a spot on it with terrain underfoot.
/// </summary>
/// <remarks>
/// Shared because more than one suite now needs a real place to stand. The octree does not author
/// empty air, so not every point over a level has ground under it — the search is what makes a test
/// about standing a test about standing rather than a test about where the level happens to be solid.
/// </remarks>
internal static class RetailLevelOneGround
{
    private static readonly Lock Gate = new();
    private static (TerrainData Terrain, double X, double Y, double Z)? _cached;

    /// <summary>Finds a point on level 1 with ground beneath it.</summary>
    /// <returns>The level's terrain, a place to stand, and the height to start the body at.</returns>
    public static (TerrainData Terrain, double X, double Y, double Z) Find()
    {
        lock (Gate)
        {
            if (_cached is { } hit)
            {
                return hit;
            }

            TerrainData terrain = LoadTerrain(out WorldScene scene);
            (double centreX, double centreY, double ground) = Medians(scene);

            for (int step = 0; step < 60; step++)
            {
                double x = (centreX + (step * 4.0)) * WorldScene.UnitsPerMetre;
                double y = centreY * WorldScene.UnitsPerMetre;
                double from = (ground + 1.0) * WorldScene.UnitsPerMetre;

                if (TerrainHeightQuery.TryGetGround(terrain, x, y, from, out TerrainGround found))
                {
                    // Start the body a little above the surface so its own settle puts it down.
                    (TerrainData, double, double, double) result = (terrain, x, y, found.Height + 1.0);
                    _cached = result;
                    return result;
                }
            }

            Assert.Fail("no point over the middle of level 1 had terrain under it");
            throw new InvalidOperationException("unreachable");
        }
    }

    private static LoadedMission? _mission;
    private static WorldScene? _scene;
    private static LevelTask? _levelRoot;

    /// <summary>Level 1's loaded task tree.</summary>
    public static LevelTask LevelRoot()
    {
        Find();
        Assert.NotNull(_levelRoot);
        return _levelRoot;
    }

    /// <summary>Level 1's loaded packs, for tests that verify authored common assets.</summary>
    public static LoadedMission Mission()
    {
        Find();
        Assert.NotNull(_mission);
        return _mission;
    }

    /// <summary>Level 1's built scene, for tests that need its placements.</summary>
    public static WorldScene Scene()
    {
        Find();
        Assert.NotNull(_scene);
        return _scene;
    }

    /// <summary>Every model level 1's packs carry, for format probes.</summary>
    public static IEnumerable<MefModel> Models()
    {
        Find();
        if (_mission is null)
        {
            yield break;
        }

        foreach (LoadedPack pack in new[] { _mission.CommonPack, _mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (pack.TryGetModel(instance, out MefModel? model) && model is not null)
                {
                    yield return model;
                }
            }
        }
    }

    /// <summary>Every instance name level 1's packs declare.</summary>
    public static IEnumerable<string> InstanceNames()
    {
        Find();
        if (_mission is null)
        {
            yield break;
        }

        foreach (LoadedPack pack in new[] { _mission.CommonPack, _mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                yield return instance.Name;
            }
        }
    }

    private static TerrainData LoadTerrain(out WorldScene scene)
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

        TerrainLoader terrainLoader = new(resources, loop.Natives);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        _levelRoot = levelRoot;
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader, null, terrainLoader)
            .Load(1);

        Assert.NotNull(mission.Terrain);
        _mission = mission;
        scene = WorldScene.Build(mission, levelRoot, magic);
        _scene = scene;
        return mission.Terrain;
    }

    private static (double X, double Y, double Z) Medians(WorldScene scene)
    {
        double[] xs = new double[scene.Placements.Count];
        double[] ys = new double[scene.Placements.Count];

        for (int i = 0; i < scene.Placements.Count; i++)
        {
            xs[i] = scene.Placements[i].Position.X;
            ys[i] = scene.Placements[i].Position.Y;
        }

        Array.Sort(xs);
        Array.Sort(ys);

        int middle = xs.Length / 2;
        return (xs[middle], ys[middle], scene.GroundHeight);
    }
}
