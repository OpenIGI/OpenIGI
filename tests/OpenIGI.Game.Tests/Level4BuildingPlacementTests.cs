using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Locks Level 4's building and window (ATTA sub-mesh) placements to the positions the reference
/// level editor actually places them at. The editor's level loader logs the final world position of
/// every top-level object; issue #31 reported that sub-attached objects such as the 459_02_1 building's
/// windows were at risk of rendering at their original model location instead of inheriting the parent
/// building's modified transform. This test asserts the building roots sit exactly where the editor
/// places them and that their attached windows follow the building rather than collapsing to the origin.
/// </summary>
public sealed class Level4BuildingPlacementTests
{
    [RequiresGameDataFact]
    public void The459BuildingAndItsWindowsMatchTheEditorPlacement()
    {
        (WorldScene scene, _) = Build(4);

        // Editor ground truth (igi1ed --verify-level 4, [LevelLoader] Object Loaded for ModelID=459_01_1):
        //   (112865928, -54890220, 175462208) with a small ~3.07deg Z rotation
        //   (112958152, -54890076, 175462208) rotated 180deg about Z
        // Positions are in engine units; WorldScene stores them in metres.
        Vector3d[] expectedUnits =
        [
            new(112865928, -54890220, 175462208),
            new(112958152, -54890076, 175462208),
        ];

        List<WorldPlacement> buildings = scene.Placements
            .Where(p => !p.IsAttached && p.Instance.Name == "459_01_1")
            .ToList();

        Assert.Equal(expectedUnits.Length, buildings.Count);
        foreach (Vector3d expected in expectedUnits)
        {
            Vector3d expectedMetres = expected / WorldScene.UnitsPerMetre;
            Assert.True(
                buildings.Any(b => (b.Position - expectedMetres).Length < 0.01),
                $"no 459_01_1 building root at editor position {expected}");
        }

        // Every window / side-mesh attached to one of these buildings must follow its parent: it sits
        // within ~50m of the building root (the model's own extent), never at the engine origin.
        foreach (WorldPlacement building in buildings)
        {
            List<WorldPlacement> children = scene.Placements
                .Where(p => ReferenceEquals(p.Owner, building.Owner) && p.IsAttached)
                .ToList();
            Assert.NotEmpty(children);
            foreach (WorldPlacement child in children)
            {
                double distance = (child.Position - building.Position).Length;
                Assert.True(
                    distance < 50,
                    $"{child.Instance.Name} attached to {building.Instance.Name} sits {distance:F1}m away —"
                    + " it did not inherit the parent building's transform");
                Assert.True(
                    child.Position.Length > 1000,
                    $"{child.Instance.Name} collapsed to the origin instead of inheriting its parent building");
            }
        }
    }

    private static (WorldScene Scene, LoadedMission Mission) Build(int missionId)
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
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(missionId);
        return (WorldScene.Build(mission, levelRoot, magic), mission);
    }
}
