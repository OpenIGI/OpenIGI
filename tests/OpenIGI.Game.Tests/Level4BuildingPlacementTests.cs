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
        (WorldScene scene, _, _) = Build(4);

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

    /// <summary>
    /// The attached windows must inherit the parent building's rotation through the transpose the reference
    /// editor applies to every ATTA sub-mesh (renderer_objects_atta.cpp: <c>attLocalRot = R_row^T</c>,
    /// composed as <c>parentRot * attLocalRot</c>). WorldScene draws attached children in
    /// <c>childTaskOrientation = parentDraw * child.Orientation.Transposed()</c>. Dropping that transpose put
    /// the windows on the wrong side of the building (issue #37). This locks the transpose frame for the 459
    /// windows against a regression back to the non-transposed draw frame.
    /// </summary>
    [RequiresGameDataFact]
    public void TheBuildingWindowsInheritRotationThroughTheTranspose()
    {
        (WorldScene scene, LoadedMission mission, MagicObjectRegistry magic) = Build(4);

        // Several distinct windows can share one model name (the 459_04_1 frames sit at different sides
        // of the building), each with its own raw attachment orientation. So match each placement to
        // whichever direct child's transposed frame it reproduces.
        bool sawDistinguishing = false;
        foreach (WorldPlacement building in scene.Placements.Where(static p => !p.IsAttached && p.Instance.Name == "459_01_1"))
        {
            // Rebuild the model's own attachment tree; each child's Orientation is the raw ATTA matrix.
            Assert.True(WorldObject.TryBuild(building.Instance.Name, mission.TryGetModel, magic, out WorldObject? root));
            Assert.NotNull(root);

            List<WorldPlacement> children = scene.Placements
                .Where(p => ReferenceEquals(p.Owner, building.Owner) && p.IsAttached)
                .ToList();
            Assert.NotEmpty(children);

            foreach (WorldPlacement window in children)
            {
                WorldObject? matched = root.Children
                    .FirstOrDefault(c => SquaredDifference(window.Orientation, building.Orientation * c.Orientation.Transposed()) < 1e-4);
                Assert.True(
                    matched is not null,
                    $"{window.Instance.Name} under {building.Instance.Name} draws in no child's transposed frame:"
                    + $" orientation {window.Orientation}, building {building.Orientation}");

                // The editor's frame is parentRot * R_row^T; childTaskOrientation is exactly that. Track
                // whether this case can tell the transpose apart from the dropped-transpose draw frame.
                Matrix3f nonTransposed = building.Orientation * matched.Orientation;
                if (SquaredDifference(window.Orientation, nonTransposed) > 1e-4)
                {
                    sawDistinguishing = true;
                }
            }
        }

        // The transpose is the point of the fix, so the level must actually exercise it: at least one
        // window has to land differently under the transposed frame than the non-transposed draw frame
        // (a fully symmetric attachment set would let the bug slip past this test).
        Assert.True(sawDistinguishing, "no Level 4 window distinguishes the transposed frame from the old draw frame");
    }

    private static double SquaredDifference(Matrix3f a, Matrix3f b)
    {
        double sum = 0.0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                double delta = a[row, column] - b[row, column];
                sum += delta * delta;
            }
        }

        return sum;
    }

    private static (WorldScene Scene, LoadedMission Mission, MagicObjectRegistry Magic) Build(int missionId)
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
        return (WorldScene.Build(mission, levelRoot, magic), mission, magic);
    }
}
