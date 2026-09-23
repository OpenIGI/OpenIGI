using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That the selection glow is drawn as the Vulkan renderer draws it: a mask, then the shells.</summary>
public sealed class OutlineTests
{
    [Fact]
    public void A_glow_is_a_mask_then_eight_shells_over_every_range()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));
        MeshHandle mesh = Triangle(device);

        device.Clear(new Bgra32(0, 0, 0, 255));
        device.DrawOutline(mesh, [new MeshDrawRange(0, 3)], Matrix4f.Identity, new Bgra32(255, 200, 0, 255), 6f, throughWalls: true);
        device.SwapBuffers();

        List<string> pipelines = [.. rhi.CommandsOf<RecordedRhiCommand.SetPipeline>().Select(p => p.Label)];
        Assert.Equal(2, pipelines.Count(label => label.StartsWith("Outline", StringComparison.Ordinal)));
        Assert.StartsWith("Outline-Mask", pipelines.First(label => label.StartsWith("Outline", StringComparison.Ordinal)), StringComparison.Ordinal);

        // One mask draw and eight shells, each over the one range.
        Assert.Equal(9, rhi.CommandsOf<RecordedRhiCommand.Draw>().Count(draw => draw.Indexed));
    }

    [Fact]
    public void Nothing_is_drawn_for_an_invisible_glow_or_no_ranges()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));
        MeshHandle mesh = Triangle(device);

        device.Clear(new Bgra32(0, 0, 0, 255));
        device.DrawOutline(mesh, [new MeshDrawRange(0, 3)], Matrix4f.Identity, new Bgra32(255, 200, 0, 0), 6f, throughWalls: true);
        device.DrawOutline(mesh, [], Matrix4f.Identity, new Bgra32(255, 200, 0, 255), 6f, throughWalls: true);
        device.DrawOutline(mesh, [new MeshDrawRange(0, 3)], Matrix4f.Identity, new Bgra32(255, 200, 0, 255), 0f, throughWalls: true);
        device.SwapBuffers();

        Assert.DoesNotContain(rhi.CommandsOf<RecordedRhiCommand.SetPipeline>(), p => p.Label.StartsWith("Outline", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_outside_the_mesh_says_so()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = new(rhi, new DisplayMode(640, 480, 32, 60));
        MeshHandle mesh = Triangle(device);

        Assert.Throws<ArgumentException>(() => device.DrawOutline(
            mesh, [new MeshDrawRange(0, 30)], Matrix4f.Identity, new Bgra32(255, 200, 0, 255), 6f, throughWalls: true));
    }

    private static MeshHandle Triangle(GraphicsDevice device) => device.CreateMesh(
        [
            new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
            new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
            new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
        ],
        [0, 1, 2]);
}
