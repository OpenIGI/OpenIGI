using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The menu's showcase camera: rails travelled at their pace, and orbits that stay inside their room.</summary>
public sealed class MenuShowcaseCameraTests
{
    private const double U = 4096;

    [Fact]
    public void ARailIsTravelledAtItsPaceAndLoopsWhenItIsTheOnlyPlace()
    {
        var rail = new CameraRail([new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(10, 10, 0)], speed: 2.0);
        var camera = new MenuShowcaseCamera([new ShowcasePoint(rail.Waypoints[0], MenuCameraSpec.Default, rail)], default, MenuCameraSpec.Default);

        Camera3D start = camera.Update(0f);
        Camera3D later = camera.Update(1f);
        Assert.InRange((later.Position - start.Position).Length, 1.9, 2.0);
        Assert.False(camera.IsCutDue);

        for (int i = 0; i < 20 && !camera.IsCutDue; i++)
        {
            camera.Update(1f);
        }

        Assert.True(camera.IsCutDue);
        camera.CommitCut();
        Assert.False(camera.IsCutDue);
        Assert.True((camera.Update(0f).Position - rail.Waypoints[0]).Length < 1e-6);
    }

    [Fact]
    public void AWallAwareOrbitStopsShortOfAWall()
    {
        // A wall across +X, four metres from the centre: everything else is open.
        WorldRayCast walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            double wall = 4 * U;
            if (end.X <= wall || origin.X >= wall)
            {
                return false;
            }

            double t = (wall - origin.X) / (end.X - origin.X);
            hit = new WorldRayHit(origin + ((end - origin) * t), default, (end - origin).Length * t);
            return true;
        };

        var spec = new MenuCameraSpec(10, 2, 0.1, 0, 0f, WallAware: true);
        var camera = new MenuShowcaseCamera([new ShowcasePoint(default, spec)], default, spec, walls);
        Camera3D facingWall = camera.Update(0f);
        Assert.InRange(facingWall.Position.X, 0.0, 4.0 - MenuShowcaseCamera.WallMargin + 1e-6);

        var plain = new MenuShowcaseCamera([new ShowcasePoint(default, spec with { WallAware = false })], default, spec, walls);
        Assert.Equal(10.0, plain.Update(0f).Position.X, 6);
    }

    [RequiresGameDataFact]
    public void TheLastMissionsShowcaseNeverPassesThroughAWall()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "OpenIGI.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        MenuCameraLibrary library = MenuCameraLibrary.Load(Path.Combine(root, "apps", "OpenIGI.Composition", "Assets", "menu-cameras.json"));
        IReadOnlyList<ShowcasePoint> places = library.PointsFor(14, MenuCameraLibrary.DefaultTitle);
        Assert.Contains(places, place => place.Rail is not null);

        (_, _, WorldScene scene) = RetailTerrainHarness.Create().LoadMission(14);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        var camera = new MenuShowcaseCamera(places, default, MenuCameraSpec.Default, solids.Caster());
        for (int place = 0; place < places.Count; place++)
        {
            Camera3D previous = camera.Update(0f);
            int frames = 0;
            while (!camera.IsCutDue && frames < 30 * 120)
            {
                Camera3D next = camera.Update(1f / 30f);
                Assert.False(
                    solids.TryCast(previous.Position * U, next.Position * U, out _),
                    $"place {place} passed through a wall at {next.Position} after {frames / 30.0:F1} s");
                previous = next;
                frames++;
            }

            camera.CommitCut();
        }
    }
}
