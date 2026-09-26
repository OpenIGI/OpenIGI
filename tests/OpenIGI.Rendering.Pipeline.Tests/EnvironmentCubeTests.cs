using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That a named cube makes the following draws reflect it.</summary>
public sealed class EnvironmentCubeTests
{
    [Fact]
    public void A_draw_reflects_only_while_a_cube_is_named()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));
        MeshHandle mesh = Triangle(device);

        TextureHandle cube = device.CreateCubeTexture(4, "water envmap");

        for (int face = 0; face < 6; face++)
        {
            device.UpdateCubeFace(cube, face, new Bgra32[16]);
        }

        device.Clear(new Bgra32(0, 0, 0, 255));
        Draw(device, mesh);
        device.SetEnvironmentCube(cube);
        Draw(device, mesh);
        device.SetEnvironmentCube(TextureHandle.None);
        Draw(device, mesh);
        device.SwapBuffers();

        List<string> meshPipelines = [.. rhi.CommandsOf<RecordedRhiCommand.SetPipeline>()
            .Select(p => p.Label)
            .Where(label => label.StartsWith("Mesh3D", StringComparison.Ordinal))];

        Assert.Equal(3, meshPipelines.Count);
        Assert.DoesNotContain("reflective", meshPipelines[0], StringComparison.Ordinal);
        Assert.EndsWith("-reflective", meshPipelines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("reflective", meshPipelines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_cube_the_device_did_not_make_or_a_short_face_says_so()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));
        TextureHandle cube = device.CreateCubeTexture(4, "envmap");

        Assert.Throws<GraphicsDeviceException>(() => device.UpdateCubeFace(new TextureHandle(9999), 0, new Bgra32[16]));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.UpdateCubeFace(cube, 6, new Bgra32[16]));
        Assert.Throws<ArgumentException>(() => device.UpdateCubeFace(cube, 0, new Bgra32[3]));
        Assert.Throws<GraphicsDeviceException>(() => device.CreateCubeTexture(0, "nothing"));
    }

    private static void Draw(GraphicsDevice device, MeshHandle mesh) =>
        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);

    private static MeshHandle Triangle(GraphicsDevice device) => device.CreateMesh(
        [
            new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
            new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
            new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
        ],
        [0, 1, 2]);
}
