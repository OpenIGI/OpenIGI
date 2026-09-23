using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the card capture opens where it should, refuses where it cannot, and takes the mesh path with it.
/// </summary>
public sealed class CardCaptureTests
{
    [Fact]
    public void A_capture_is_taken_while_a_frame_is_being_drawn()
    {
        // The menu loads a mission while it goes on drawing, so a frame is open whenever that level's
        // cards are captured. Refused, as it once was, every mission entered from the menu had no surface
        // cache: its light held at the baked daylight, and a night chosen there came up lit like noon.
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        graphics.Clear(new Bgra32(0, 0, 0, 255));
        graphics.DrawTriangles2D(Quad(), [0, 1, 2], TextureHandle.None, BlendMode.Opaque);

        Assert.True(graphics.BeginCardCapture(64, 64));
        graphics.EndCardCapture();

        // And the frame it was taken inside still goes out.
        graphics.SwapBuffers();
    }

    [Fact]
    public void A_capture_is_refused_inside_a_scene_capture()
    {
        // The scene capture owns the per-draw state the cards would be drawn through.
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        Assert.True(graphics.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));

        Assert.False(graphics.BeginCardCapture(64, 64));

        graphics.EndSceneCapture();
    }

    [Fact]
    public void A_capture_opened_twice_says_so()
    {
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        Assert.True(graphics.BeginCardCapture(64, 64));
        Assert.Throws<GraphicsDeviceException>(() => graphics.BeginCardCapture(64, 64));

        graphics.EndCardCapture();
    }

    [Fact]
    public void Taking_a_page_or_closing_without_a_capture_says_so()
    {
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        Assert.Throws<GraphicsDeviceException>(() => graphics.SetCardPage(0, 0, 8, 8));
        Assert.Throws<GraphicsDeviceException>(graphics.EndCardCapture);
    }

    [Fact]
    public void Reading_an_atlas_that_was_never_captured_says_so()
    {
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        Assert.Throws<GraphicsDeviceException>(() => graphics.ReadCardAtlas(0, new Bgra32[16]));
    }

    [Fact]
    public void Reading_an_attachment_the_atlas_has_not_got_says_so()
    {
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        Assert.True(graphics.BeginCardCapture(4, 4));
        graphics.EndCardCapture();

        Assert.Throws<GraphicsDeviceException>(() => graphics.ReadCardAtlas(4, new Bgra32[16]));

        // And a destination too short for the atlas, which would otherwise be a partial dump read as a
        // whole one.
        Assert.Throws<GraphicsDeviceException>(() => graphics.ReadCardAtlas(0, new Bgra32[4]));
    }

    [Fact]
    public void The_capture_records_into_an_encoder_of_its_own()
    {
        // Not the frame's: the frame loop is not running, and what it submits is waited on so the caller
        // can read the atlas back straight after.
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        Assert.True(graphics.BeginCardCapture(16, 16));
        graphics.SetCardPage(0, 0, 8, 8);
        graphics.EndCardCapture();

        RecordedRhiCommand.BeginRenderPass pass =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.Equal(4, pass.ColorAttachments.Count);

        Assert.Equal(
            new RecordedRhiCommand.SetViewport(0, 0, 8, 8, 0f, 1f),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.SetViewport>()));

        Assert.True(device.WaitIdleCount > 0, "The capture was left in flight.");
    }

    [Fact]
    public void A_mesh_drawn_during_a_capture_goes_into_the_page_rather_than_the_frame()
    {
        using RecordingRhiDevice device = new();
        using GraphicsDevice graphics = Device(device);

        MeshHandle mesh = graphics.CreateMesh(
            [
                new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitZ, 0f, 0f),
                new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitZ, 1f, 0f),
                new Vertex3D(new Vector3f(0f, 1f, 0f), Vector3f.UnitZ, 0f, 1f),
            ],
            [0, 1, 2]);

        Assert.True(graphics.BeginCardCapture(16, 16));
        graphics.SetCardPage(0, 0, 16, 16);
        graphics.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
        graphics.EndCardCapture();

        // One pass, the capture's: a draw that had opened the present pass instead would make two.
        Assert.Single(device.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.Equal(
            new RecordedRhiCommand.Draw(3, 1, Indexed: true),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.Draw>()));

        // And with the card pipeline, which is the one thing that decides what a page holds.
        RecordedRhiCommand.SetPipeline pipeline =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.SetPipeline>());

        Assert.StartsWith("Card", pipeline.Label, StringComparison.Ordinal);
    }

    private static GraphicsDevice Device(RecordingRhiDevice device) =>
        new(device, new DisplayMode(64, 64, 32, 60));

    private static Vertex2D[] Quad() =>
    [
        new(0f, 0f, 0f, 0f, new Bgra32(255, 255, 255, 255)),
        new(8f, 0f, 1f, 0f, new Bgra32(255, 255, 255, 255)),
        new(8f, 8f, 1f, 1f, new Bgra32(255, 255, 255, 255)),
    ];
}
