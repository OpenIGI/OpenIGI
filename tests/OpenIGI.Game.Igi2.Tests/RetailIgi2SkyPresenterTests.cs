using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Every shipped level's sky, drawn.</summary>
/// <remarks>
/// The dome's own arithmetic is pinned in <c>RetailIgi2SkyDomeTests</c> and the shared geometry has its
/// own suite; what is left to say here is that the presenter puts the two together the way
/// <c>FlatSky_EnterHandler</c> does — the dome first, then a layer per <c>FlatSkyLayer</c> child, all of
/// it about the viewer with the depth buffer out of the way.
/// </remarks>
public sealed class RetailIgi2SkyPresenterTests
{
    /// <summary>Every level's sky draws its dome and then its layers, behind everything.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_levels_sky_draws_its_dome_and_then_its_layers()
    {
        int skies = 0;
        int layers = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (FlatSkyTask sky in Skies(level.Root))
            {
                RecordingGraphicsDevice device = new();
                using Igi2SkyPresenter presenter = new(device, sky);

                skies++;
                layers += presenter.LayerCount;

                presenter.Draw(new Camera3D(new Vector3d(1_000_000, 2_000_000, 40_000), Camera3D.LookRotation(0.7f, -0.2f)));

                // The dome, then one draw per layer, in that order.
                Assert.Equal(1 + presenter.LayerCount, device.MeshDraws.Count);

                RecordingGraphicsDevice.MeshDrawCall dome = device.MeshDraws[0];
                Assert.Equal(SkyDomeGeometry.IndexCount, dome.IndexCount);
                Assert.Equal(BlendMode.Opaque, dome.Blend);
                Assert.Equal(TextureHandle.None, dome.Diffuse);

                // A backdrop: no depth at all, and both facings drawn, so a camera inside the dome sees
                // its far wall rather than nothing.
                Assert.All(device.MeshDraws, draw => Assert.Equal(DepthState.Disabled, draw.DepthState));
                Assert.All(device.MeshDraws, draw => Assert.Equal(CullMode.None, draw.CullMode));

                // Each layer is the shared cloud grid, ADDED over the dome: the strip the original
                // registers for a layer carries state word 0x41480008, whose bit 3 is SRCALPHA/ONE in
                // Direct3DQVertex_SetAlphaState (igi2ed 0x6653D0). Blended instead, location1/level1's
                // star layer -- opaque black between its stars -- painted the whole dome black.
                foreach (RecordingGraphicsDevice.MeshDrawCall cloud in device.MeshDraws.Skip(1))
                {
                    Assert.Equal(SkyCloudPlane.IndexCount, cloud.IndexCount);
                    Assert.Equal(BlendMode.Additive, cloud.Blend);
                }
            }
        }

        // Every shipped sky, and the cloud layers they carry between them.
        Assert.Equal(24, skies);
        Assert.True(layers > 0, "not one shipped sky declared a cloud layer, which would make the layer path dead.");
    }

    /// <summary>A layer's grid is rebuilt for the camera rather than uploaded once.</summary>
    /// <remarks>
    /// The fade is a function of view depth, so the vertices are camera-dependent even though the plane
    /// is not. A presenter that uploaded the grid once would draw a sky whose horizon haze stayed where
    /// the player first stood — visible only after walking, which is exactly the kind of thing a
    /// screenshot test would pass.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Turning_the_camera_moves_the_haze()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (FlatSkyTask sky in Skies(level.Root))
            {
                if (sky.FogAmount < SkyCloudPlane.MinimumFogAmount)
                {
                    continue;
                }

                RecordingGraphicsDevice device = new();
                using Igi2SkyPresenter presenter = new(device, sky);
                if (presenter.LayerCount == 0)
                {
                    continue;
                }

                Vector3d position = new(1_000_000, 2_000_000, 40_000);
                presenter.Draw(new Camera3D(position, Camera3D.LookRotation(0f, 0f)));

                uint mesh = device.MeshDraws[1].Mesh.Id;
                Vertex3D[] facingNorth = [.. device.Meshes[mesh].Vertices];

                presenter.Draw(new Camera3D(position, Camera3D.LookRotation(MathF.PI, 0f)));
                Vertex3D[] facingSouth = device.Meshes[mesh].Vertices;

                // Same plane, same positions — the geometry does not turn with the camera.
                for (int i = 0; i < facingNorth.Length; i++)
                {
                    Assert.Equal(facingNorth[i].Position, facingSouth[i].Position);
                }

                // But the depth-driven alpha does, because what was ahead is now behind.
                Assert.Contains(
                    Enumerable.Range(0, facingNorth.Length),
                    i => facingNorth[i].Colour.A != facingSouth[i].Colour.A);
                return;
            }
        }
    }

    /// <summary>A tick scrolls every layer, and the scroll stays inside one turn.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Ticking_scrolls_the_layers_within_one_turn()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (FlatSkyTask sky in Skies(level.Root))
            {
                RecordingGraphicsDevice device = new();
                using Igi2SkyPresenter presenter = new(device, sky);

                bool scrolls = false;
                foreach (GameTask child in sky.Children)
                {
                    scrolls |= child is FlatSkyLayerTask layer && (layer.SpeedX != 0f || layer.SpeedY != 0f);
                }

                if (!scrolls)
                {
                    continue;
                }

                Camera3D camera = new(default, Camera3D.LookRotation(0f, 0f));
                presenter.Draw(camera);
                uint mesh = device.MeshDraws[1].Mesh.Id;
                Vertex3D[] before = [.. device.Meshes[mesh].Vertices];

                // An hour of ticks: the offsets wrap rather than growing, so the coordinates stay small.
                for (int i = 0; i < 60 * 60 * 60; i++)
                {
                    presenter.Tick();
                }

                presenter.Draw(camera);
                Vertex3D[] after = device.Meshes[mesh].Vertices;

                float widest = 0f;
                bool moved = false;
                for (int i = 0; i < before.Length; i++)
                {
                    moved |= before[i].DiffuseU != after[i].DiffuseU || before[i].DiffuseV != after[i].DiffuseV;
                    widest = MathF.Max(widest, MathF.Abs(after[i].DiffuseU - before[i].DiffuseU));
                }

                Assert.True(moved, "an hour of ticks did not move a layer that declares a speed.");

                // The whole drift is inside one texture width, which is what the wrap buys.
                Assert.True(widest < 1f, $"the scroll drifted by {widest} turns rather than wrapping.");
                return;
            }
        }
    }

    private static IEnumerable<FlatSkyTask> Skies(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is FlatSkyTask sky)
            {
                yield return sky;
            }

            foreach (FlatSkyTask descendant in Skies(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void The_dome_turns_to_its_sun_as_the_originals_world_matrix_does()
    {
        // SkyDome_Draw: angle atan2(x, y) + pi/2, and the row-vector world matrix [c -s; s c] carries (x, y) to (x c + y s, -x s + y c).
        OpenIGI.Core.Numerics.Vector3f sun = new(0.6f, 0.7f, 0.2f);
        float angle = MathF.Atan2(sun.X, sun.Y) + (MathF.PI / 2f);
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        OpenIGI.Core.Graphics.Camera3D eye = new(OpenIGI.Core.Numerics.Vector3d.Zero, OpenIGI.Core.Numerics.Matrix3f.Identity);
        OpenIGI.Core.Numerics.Matrix4f view = eye.CreateModelView(OpenIGI.Core.Numerics.Vector3d.Zero, OpenIGI.Presentation.Igi2.World.Igi2SkyPresenter.DomeOrientation(sun));

        OpenIGI.Core.Numerics.Vector3f moved = view.TransformPoint(new OpenIGI.Core.Numerics.Vector3f(3f, 5f, 2f));
        Assert.Equal((3f * c) + (5f * s), moved.X, 4);
        Assert.Equal((-3f * s) + (5f * c), moved.Y, 4);
        Assert.Equal(2f, moved.Z, 4);
    }
}
