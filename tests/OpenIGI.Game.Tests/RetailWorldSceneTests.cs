using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Builds the drawable scene for a retail mission: every task that places a model, joined to the pack
/// instance supplying its geometry.
/// </summary>
/// <remarks>
/// The join is the part worth pinning, because geometry and placement live in different files and
/// nothing declares that they agree. They do: level 1 names 501 models across its object tree and every
/// one of them is a pack instance name.
/// </remarks>
public sealed class RetailWorldSceneTests
{
    [RequiresGameDataFact]
    public void EveryPlacedModelResolvesToAPackInstance()
    {
        (WorldScene scene, _) = Build();

        Assert.Equal(0, scene.UnresolvedCount);
        Assert.True(scene.Placements.Count > 400, $"expected the level's objects, got {scene.Placements.Count}");
    }

    [RequiresGameDataFact]
    public void SecurityCamerasUseTheirHolderTransformAndCarryTheDestroyedHead()
    {
        (WorldScene scene, _) = Build();
        WorldPlacement[] cameras = scene.Placements
            .Where(placement => placement.Owner.Type.Name == WorldScene.SecurityCameraType)
            .ToArray();

        Vector3d[] retailHeadPositions =
        [
            new(24652964.083, -56288034.612, 174436146.125),
            new(24811765.286, -56258174.683, 174429586.125),
            new(24471017.438, -56423954.263, 174429666.125),
            new(24596161.464, -56270227.708, 174427890.125),
            new(24441183.806, -56257203.768, 174432514.125),
        ];

        // Mission 1 authors five cameras. Retail creates a holder and an articulated head for each.
        Assert.Equal(10, cameras.Length);
        IGrouping<GameTask, WorldPlacement>[] grouped = cameras.GroupBy(placement => placement.Owner).ToArray();
        Assert.Equal(retailHeadPositions.Length, grouped.Length);
        for (int i = 0; i < grouped.Length; i++)
        {
            IGrouping<GameTask, WorldPlacement> camera = grouped[i];
            Assert.Equal(2, camera.Count());
            WorldPlacement holder = Assert.Single(camera, placement => placement.Instance.Name == "313_01_1");
            WorldPlacement head = Assert.Single(camera, placement => placement.Instance.Name == "313_02_1");

            Assert.NotEqual(default, holder.Position);
            Assert.NotEqual(holder.Position, head.Position);
            AssertEnginePosition(retailHeadPositions[i], head.Position);
            Assert.Null(holder.DestroyedReplacement);
            Assert.Equal("313_03_1", Assert.IsType<WorldModelBinding>(head.DestroyedReplacement).Instance.Name);
            Assert.True(
                ObjectCollisionIndex.Build(camera).Count > 0,
                $"{camera.Key.Name} has no shootable collision mesh");
        }
    }

    [RequiresGameDataFact]
    public void MissionOneDoesNotSynthesizeModelsForAudioOnlyConditionalSounds()
    {
        (WorldScene scene, _) = Build();

        // The injected retail commit hook records the two ConditionalSound tasks as audio voices only:
        // Mission 1 creates no 309/310 loudspeaker and no AlarmLight world object for them.
        Assert.DoesNotContain(scene.Placements, placement => placement.Owner.Type.Name == "ConditionalSound");

        Assert.Equal(5, scene.Placements
            .Where(placement => placement.Owner.Type.Name == WorldScene.SecurityCameraType)
            .Select(placement => placement.Owner)
            .Distinct()
            .Count());
    }

    [RequiresGameDataFact]
    public void SwitchesPlaceTheModelTheirTaskDeclaresNotTheGenericSwitch()
    {
        (WorldScene scene, _) = Build();

        // Mission 1 authors two families of Switch task: the three alarm pads (94, 92, 99) declare
        // "On model" = 234_01_1 (ALARM_SWITCH) and the three door buttons (500, 505, 506) declare
        // 202_01_1 (SWITCH). Each switch must place the model its level data names, not the literal
        // common-pack "switch" every one used to fall back to.
        WorldPlacement[] switches = scene.Placements
            .Where(placement => placement.Owner.Type.Name == WorldScene.SwitchType)
            .ToArray();

        Assert.Equal(6, switches.Length);
        Assert.Equal(3, switches.Count(placement => placement.Instance.Name == "234_01_1"));
        Assert.Equal(3, switches.Count(placement => placement.Instance.Name == "202_01_1"));
        Assert.DoesNotContain(switches, placement => placement.Instance.Name == "switch");
    }

    private static void AssertEnginePosition(Vector3d expectedUnits, Vector3d actualMetres)
    {
        const double toleranceUnits = 0.75;
        Assert.InRange(Math.Abs((actualMetres.X * WorldScene.UnitsPerMetre) - expectedUnits.X), 0, toleranceUnits);
        Assert.InRange(Math.Abs((actualMetres.Y * WorldScene.UnitsPerMetre) - expectedUnits.Y), 0, toleranceUnits);
        Assert.InRange(Math.Abs((actualMetres.Z * WorldScene.UnitsPerMetre) - expectedUnits.Z), 0, toleranceUnits);
    }

    [RequiresGameDataFact]
    public void ATaskWithNoPositionIsSkippedRatherThanLeftAtTheOrigin()
    {
        (WorldScene scene, _) = Build();

        // Doors use "Position start" and trains use a scalar position on a railroad spline. Both are now
        // resolved; neither may fall through to the world origin and drag the camera out with it.
        Assert.Equal(0, scene.UnplacedCount);

        foreach (WorldPlacement placement in scene.Placements)
        {
            Assert.NotEqual(default, placement.Position);
        }
    }

    [RequiresGameDataFact]
    public void ThePlacementsSitInsideOneCompactLevel()
    {
        (WorldScene scene, _) = Build();

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        foreach (WorldPlacement placement in scene.Placements)
        {
            minX = Math.Min(minX, placement.Position.X);
            maxX = Math.Max(maxX, placement.Position.X);
            minY = Math.Min(minY, placement.Position.Y);
            maxY = Math.Max(maxY, placement.Position.Y);
        }

        // Metres, after dividing by WORLD_METER. A level a couple of kilometres across is right; tens of
        // kilometres means the unit conversion has gone wrong.
        Assert.InRange(maxX - minX, 100.0, 5000.0);
        Assert.InRange(maxY - minY, 100.0, 5000.0);
    }

    [RequiresGameDataFact]
    public void EveryPlacementBuildsGeometryAtAPlausibleSize()
    {
        (WorldScene scene, _) = Build();

        int built = 0;
        foreach (WorldPlacement placement in scene.Placements)
        {
            MeshGeometry? geometry = MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
            if (geometry is null)
            {
                continue;
            }

            built++;
            float extent = 0f;
            foreach (Vertex3D vertex in geometry.Vertices)
            {
                extent = Math.Max(extent, Math.Abs(vertex.Position.X));
                extent = Math.Max(extent, Math.Abs(vertex.Position.Y));
                extent = Math.Max(extent, Math.Abs(vertex.Position.Z));
            }

            // Metres. A model reaching a kilometre would mean model vertices are not in world units.
            Assert.InRange(extent, 0.01f, 500f);
        }

        Assert.True(built > 400, $"expected geometry for the level's objects, got {built}");
    }

    [RequiresGameDataFact]
    public void TheSceneCarriesTheAttachedObjectsAndABindableLightmapForEachOne()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(1);
        WorldScene scene = WorldScene.Build(mission, levelRoot, magic);

        // Without the magic-object table the level is only its shells.
        WorldScene bare = WorldScene.Build(mission, levelRoot);
        Assert.Equal(0, bare.AttachedCount);
        Assert.True(
            scene.Placements.Count > bare.Placements.Count,
            $"attachments added nothing: {scene.Placements.Count} vs {bare.Placements.Count}");
        Assert.True(scene.AttachedCount > 100, $"expected the level's attached contents, got {scene.AttachedCount}");

        Assert.True(mission.TryReadLightmapArchive(resources, out IresArchive? archive));
        Dictionary<string, IresEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (IresEntry entry in archive!.Entries)
        {
            entries[entry.Name[(entry.Name.LastIndexOfAny(['/', '\\']) + 1)..]] = entry;
        }

        // The binding the renderer performs, checked here rather than by looking at pixels: the entry a
        // placement names must exist and must be sized like the model's own lightmap page.
        int bound = 0;
        List<string> failures = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!placement.HasLightmap)
            {
                continue;
            }

            string name = placement.LightmapEntryName();
            if (!entries.TryGetValue(name, out IresEntry? entry))
            {
                // A model with no faces is never baked, so its index is simply absent from the archive.
                continue;
            }

            bound++;
            ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data);
            IReadOnlyList<MefLightmapPage> pages = placement.Model.LightmapPages;
            if (pages.Count != lightmap.Images.Count
                || (pages.Count > 0
                    && (pages[0].Width != lightmap.Images[0].Width || pages[0].Height != lightmap.Images[0].Height)))
            {
                failures.Add($"{name}: {placement.Instance.Name} pages [{string.Join(",", pages)}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(8)));
        Assert.True(bound > 200, $"expected the level's baked objects to bind, got {bound}");
    }

    private static (WorldScene Scene, LoadedMission Mission) Build()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(1);

        return (WorldScene.Build(mission, levelRoot), mission);
    }
}
