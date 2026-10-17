using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The shared world renderer composites I.G.I. 2's objects the way the title says to, sub-mesh by
/// sub-mesh, and Project I.G.I.'s the way it always did.
/// </summary>
/// <remarks>
/// <para>
/// Drawn from beside location1/level1's storage tank — a model in the lightmapped format that the
/// archive holds no bake for — so that one frame carries both kinds of sub-mesh: those with a page,
/// which must be doubled and take no sun, and those without, which must be drawn at one and lit from
/// the level's sun in their own frame. A renderer that applied either term to the wrong kind would
/// pass a test that looked at only one.
/// </para>
/// </remarks>
public sealed class RetailIgi2ObjectShadingDrawTests
{
    /// <summary>The storage tank on location1/level1, <c>414_03_1</c>, in metres.</summary>
    private static readonly Vector3d Tank = new(-476.25, 209.59, 17.55);

    /// <summary>A bake is doubled and takes no sun; a sub-mesh without one is drawn at one and lit by it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Baked_sub_meshes_are_doubled_and_unbaked_ones_are_sunlit()
    {
        Igi2Level level = LevelOne();
        ObjectShading? shading = Igi2PresentationProvider.Instance.ReadObjectShading(level.Root);
        Assert.NotNull(shading);
        Assert.NotNull(shading.Value.Sun);

        RecordingGraphicsDevice device = Draw(level, shading.Value);

        List<RecordingGraphicsDevice.MeshDrawCall> baked = device.MeshDraws.Where(draw => draw.Lightmap != TextureHandle.None).ToList();
        List<RecordingGraphicsDevice.MeshDrawCall> unbaked = device.MeshDraws.Where(draw => draw.Lightmap == TextureHandle.None).ToList();
        Assert.NotEmpty(baked);
        Assert.NotEmpty(unbaked);

        Assert.All(baked, draw =>
        {
            Assert.Equal(2f, draw.LightmapScale);
            Assert.Equal(Vector3f.Zero, draw.VertexSun);
        });

        // In the placement's own frame the direction is still a unit vector: an orientation is a
        // rotation, and a rotation does not change a length. Its components do change with the
        // placement, which is the point of carrying it per draw.
        Assert.All(unbaked, draw =>
        {
            Assert.Equal(1f, draw.LightmapScale);
            Assert.Equal(1f, draw.VertexSun.Length, 4);
        });

        // And the device is left clean for whatever draws after the world: the terrain lights itself.
        Assert.Equal(1f, device.LightmapScale);
        Assert.Equal(Vector3f.Zero, device.VertexSun);
    }

    /// <summary>The default shading is the compositing the port always had: gain one and no sun anywhere.</summary>
    /// <remarks>
    /// This is Project I.G.I.'s case, drawn through the sequel's data because the assertion is about
    /// the renderer rather than the level: with nothing said at the seam, no draw carries either term.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_default_shading_changes_no_draw()
    {
        RecordingGraphicsDevice device = Draw(LevelOne(), ObjectShading.Default);

        Assert.NotEmpty(device.MeshDraws);
        Assert.All(device.MeshDraws, draw =>
        {
            Assert.Equal(1f, draw.LightmapScale);
            Assert.Equal(Vector3f.Zero, draw.VertexSun);
        });
    }

    private static RecordingGraphicsDevice Draw(Igi2Level level, ObjectShading shading)
    {
        (Igi2DrawableSceneResult built, ResourceRegistry resources) = Igi2LevelScene.Build(level);
        RecordingGraphicsDevice device = new();
        using WorldRenderer renderer = new(device, resources, AssetFormatProfile.Igi2) { Shading = shading };

        // Twenty-five metres south of the tank, looking north at it and at the lightmapped sheds
        // behind it. Yaw zero looks along +Y.
        Camera3D camera = new(Tank - new Vector3d(0, 25, -2), Camera3D.LookRotation(0f, 0f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.1f, 400f);
        renderer.Draw(built.Scene, camera, projection);

        return device;
    }

    private static Igi2Level LevelOne() =>
        Igi2Level.All().First(level =>
            level.Directory.Replace('\\', '/').EndsWith("location1/level1", StringComparison.OrdinalIgnoreCase));
}
