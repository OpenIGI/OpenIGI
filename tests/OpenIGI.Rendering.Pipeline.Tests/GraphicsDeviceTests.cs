using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the renderer the game talks to draws the frame it says it draws.
/// </summary>
/// <remarks>
/// Asserted against the recording device, which is what makes the frame's SHAPE checkable: that a
/// clear becomes the next pass's clear rather than a command, that a 2D draw and a 3D draw land in one
/// pass, and that a frame which drew nothing presents nothing.
/// </remarks>
public sealed class GraphicsDeviceTests
{
    private static readonly Bgra32 White = new(255, 255, 255, 255);

    [Fact]
    public void It_reports_the_direct_path_and_names_what_it_does_not_do()
    {
        // The question everything above asks before drawing the remaster's frame. Answering it truthfully
        // is what makes a device that draws only the direct path usable rather than broken.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.False(device.Capabilities.SupportsSceneCapture);
        Assert.False(device.Capabilities.SupportsCompute);
        Assert.False(device.BeginSceneCapture(default));

        Assert.NotEmpty(device.Capabilities.UnimplementedSettings);
        Assert.Contains("r_shadows", device.Capabilities.UnimplementedSettings);
    }

    [Fact]
    public void A_frame_that_drew_nothing_presents_nothing()
    {
        // Opening a frame only to close it would be a blank flash rather than the held picture.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        device.SwapBuffers();

        Assert.Empty(rhi.CommandsOf<RecordedRhiCommand.BeginFrame>());
    }

    [Fact]
    public void A_clear_becomes_the_clear_of_the_pass_that_follows_it()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        device.Clear(new Bgra32(0, 0, 0, 255));
        Sprite(device);
        device.SwapBuffers();

        RecordedRhiCommand.BeginRenderPass pass =
            Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.Equal("present", pass.Label);
        Assert.Equal([true], pass.Clears);
    }

    [Fact]
    public void Everything_a_frame_draws_lands_in_one_pass()
    {
        // A pass per draw would be correct and ruinous; the direct path has exactly one.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        device.Clear(new Bgra32(0, 0, 0, 255));
        Sprite(device);
        World(device);
        Sprite(device);
        device.SwapBuffers();

        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>());
        Assert.Equal(3, rhi.CommandsOf<RecordedRhiCommand.Draw>().Count);
    }

    [Fact]
    public void A_clear_partway_through_a_frame_starts_a_second_pass()
    {
        // The only shape the interface has: a pass states what happens to its attachments as it begins.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Sprite(device);
        device.Clear(new Bgra32(0, 0, 0, 255));
        Sprite(device);
        device.SwapBuffers();

        IReadOnlyList<RecordedRhiCommand.BeginRenderPass> passes =
            rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>();

        Assert.Equal(2, passes.Count);
        Assert.Equal([false], passes[0].Clears);
        Assert.Equal([true], passes[1].Clears);
    }

    [Fact]
    public void A_frame_begins_and_ends_around_what_it_drew()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Sprite(device);
        device.SwapBuffers();

        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BeginFrame>());
        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.EndFrame>());
        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.Submit>());
    }

    [Fact]
    public void Successive_frames_alternate_the_resources_they_write()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        List<int> slots = [];

        for (int i = 0; i < 4; i++)
        {
            Sprite(device);
            device.SwapBuffers();
        }

        foreach (RecordedRhiCommand.BeginFrame frame in rhi.CommandsOf<RecordedRhiCommand.BeginFrame>())
        {
            slots.Add(frame.FrameInFlight);
        }

        Assert.Equal([0, 1, 0, 1], slots);
    }

    [Fact]
    public void A_destroyed_texture_takes_the_groups_that_named_it()
    {
        // Or the next frame binds a view that is gone.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        TextureHandle texture = device.CreateTexture(new TextureDescription(2, 2), Pixels(4));

        Sprite(device, texture);
        device.SwapBuffers();

        RecordedBindGroup group = rhi.Resources
            .OfType<RecordedBindGroup>()
            .Single(g => g.Label == $"texture-{texture.Id}");

        device.DestroyTexture(texture);

        Assert.True(group.IsDisposed);
    }

    [Fact]
    public void A_mesh_is_drawn_through_the_world_pipeline()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        World(device);
        device.SwapBuffers();

        RecordedRhiCommand.SetPipeline pipeline =
            Assert.Single(rhi.CommandsOf<RecordedRhiCommand.SetPipeline>());

        Assert.StartsWith("Mesh3D-", pipeline.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void The_depth_buffer_follows_the_mode()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        World(device);
        device.SwapBuffers();

        RecordedTexture depth = rhi.Resources.OfType<RecordedTexture>().Single(t => t.Label == "present.depth");
        Assert.Equal(640, depth.Width);

        device.SetMode(new DisplayMode(1280, 720, 32, 60));
        World(device);
        device.SwapBuffers();

        Assert.True(depth.IsDisposed);
        Assert.Equal(1280, rhi.Resources.OfType<RecordedTexture>().Last(t => t.Label == "present.depth").Width);
    }

    [Fact]
    public void Reaching_for_the_remaster_stack_says_which_member_and_why()
    {
        // Every one of these is guarded by a capability reported false, so reaching one means something
        // asked without asking first.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        GraphicsDeviceException refused =
            Assert.Throws<GraphicsDeviceException>(() => device.CreateStorageBuffer(64, "b"));

        Assert.Contains(nameof(IGraphicsDevice.CreateStorageBuffer), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_occlusion_query_nothing_answers_reads_as_visible()
    {
        // A false would hide geometry that is there; a true costs a draw that would have happened.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.OcclusionVisible(device.CreateOcclusionQuery()));
    }

    private static GraphicsDevice Open(RecordingRhiDevice rhi) =>
        new(rhi, new DisplayMode(640, 480, 32, 60));

    private static Bgra32[] Pixels(int count)
    {
        Bgra32[] pixels = new Bgra32[count];
        Array.Fill(pixels, White);
        return pixels;
    }

    private static void Sprite(GraphicsDevice device, TextureHandle texture = default)
    {
        Vertex2D[] quad =
        [
            new(0f, 0f, 0f, 0f, White),
            new(8f, 0f, 1f, 0f, White),
            new(8f, 8f, 1f, 1f, White),
            new(0f, 8f, 0f, 1f, White),
        ];

        device.DrawTriangles2D(quad, [0, 1, 2, 0, 2, 3], texture, BlendMode.AlphaBlend);
    }

    private static void World(GraphicsDevice device)
    {
        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
                new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
                new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
            ],
            [0, 1, 2]);

        device.SetDepthState(DepthState.TestAndWrite);
        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
    }
}
