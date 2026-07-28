using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
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
    public void ATaskWithNoPositionIsSkippedRatherThanLeftAtTheOrigin()
    {
        (WorldScene scene, _) = Build();

        // A Door calls its position "Position start". Placing those at the origin instead put objects a
        // kilometre outside the level and dragged the camera out with them.
        Assert.True(scene.UnplacedCount > 0, "the level has types whose position is named differently");

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
