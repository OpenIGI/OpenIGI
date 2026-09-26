using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That the frame is copied into a texture where the menu's dissolve asks for it.</summary>
public sealed class FrameCopyTests
{
    [Fact]
    public void The_frame_is_copied_mid_frame_and_the_pass_resumes_on_top_of_it()
    {
        // The menu holds the outgoing level after it has drawn and before the interface goes over it; the
        // interface then draws into the same frame, so the pass that follows has to load, not clear.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);
        TextureHandle held = device.CreateTexture(new TextureDescription(64, 48), new Bgra32[64 * 48]);

        device.Clear(new Bgra32(0, 0, 0, 255));
        device.DrawTriangles2D(Quad(), [0, 1, 2], TextureHandle.None, BlendMode.Opaque);
        device.CopyFrameToTexture(held, 64, 48);
        device.DrawTriangles2D(Quad(), [0, 1, 2], TextureHandle.None, BlendMode.Opaque);
        device.SwapBuffers();

        RecordedRhiCommand.CopyTextureToTexture copy =
            Assert.Single(rhi.CommandsOf<RecordedRhiCommand.CopyTextureToTexture>());
        Assert.Equal("swapchain", copy.Source);

        IReadOnlyList<RecordedRhiCommand.BeginRenderPass> passes = rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>();
        Assert.Equal(2, passes.Count);
        Assert.False(passes[1].Clears[0]);
    }

    [Fact]
    public void A_copy_takes_the_frames_channel_order()
    {
        // A copy moves bytes and cannot swizzle: a red-first canvas needs a red-first texture.
        Assert.Equal(RhiTextureFormat.Bgra8UnormSrgb, GraphicsDevice.FrameCopyFormat(RhiTextureFormat.Bgra8Unorm));
        Assert.Equal(RhiTextureFormat.Rgba8UnormSrgb, GraphicsDevice.FrameCopyFormat(RhiTextureFormat.Rgba8Unorm));
        Assert.Equal(RhiTextureFormat.Rgba8UnormSrgb, GraphicsDevice.FrameCopyFormat(RhiTextureFormat.Rgba8UnormSrgb));
    }

    [Fact]
    public void Outside_a_frame_nothing_is_copied()
    {
        // The last frame's image belongs to the presentation engine once it has gone out.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);
        TextureHandle held = device.CreateTexture(new TextureDescription(64, 48), new Bgra32[64 * 48]);

        device.CopyFrameToTexture(held, 64, 48);

        Assert.Empty(rhi.CommandsOf<RecordedRhiCommand.CopyTextureToTexture>());
    }

    private static GraphicsDevice Open(RecordingRhiDevice rhi) => new(rhi, new DisplayMode(640, 480, 32, 60));

    private static Vertex2D[] Quad()
    {
        Bgra32 white = new(255, 255, 255, 255);
        return [new(0, 0, 0, 0, white), new(10, 0, 1, 0, white), new(10, 10, 1, 1, white)];
    }
}
