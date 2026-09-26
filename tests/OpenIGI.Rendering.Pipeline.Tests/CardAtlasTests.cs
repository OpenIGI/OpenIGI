using OpenIGI.Rendering.Pipeline.SurfaceCache;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the card capture opens a pass of the shape the card pipeline was built for.
/// </summary>
public sealed class CardAtlasTests
{
    [Fact]
    public void The_capture_writes_four_pages_and_its_own_depth()
    {
        using RecordingRhiDevice device = new();
        using CardAtlas atlas = new(device, 64, 32);

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("capture");

        using (atlas.Begin(encoder))
        {
        }

        device.Submit([encoder.Finish()]);

        RecordedRhiCommand.BeginRenderPass pass =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.Equal(CardAtlas.TargetCount, pass.ColorAttachments.Count);
        Assert.NotNull(pass.DepthAttachment);
    }

    [Fact]
    public void Every_page_is_cleared_to_nothing_and_the_depth_to_the_far_plane()
    {
        // Coverage is zero everywhere no triangle reaches, which is what a reader tests. And a card's
        // depth runs the ordinary way -- near at nought, far at one -- which is the opposite of the
        // reverse-Z the rest of this renderer draws with, so the empty value is one rather than nought.
        using RecordingRhiDevice device = new();
        using CardAtlas atlas = new(device, 64, 32);

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("capture");

        using (atlas.Begin(encoder))
        {
        }

        device.Submit([encoder.Finish()]);

        RecordedRhiCommand.BeginRenderPass pass =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.All(pass.Clears, cleared => Assert.True(cleared));
    }

    [Fact]
    public void The_capture_states_its_own_depth_because_a_card_is_not_reverse_z()
    {
        // A card's depth runs the ordinary way -- near at nought, far at one -- which is the opposite of
        // the reverse-Z the rest of this renderer draws with, so the value standing for "nothing here
        // yet" is one. Stated on the descriptor rather than recorded, so it is read from there.
        using RecordingRhiDevice device = new();
        using CardAtlas atlas = new(device, 16, 16);
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("capture");

        using IRhiRenderPassEncoder pass = atlas.Begin(encoder);

        RecordingRhiRenderPass recorded = Assert.IsType<RecordingRhiRenderPass>(pass);
        RhiDepthAttachment depth = recorded.Descriptor.DepthAttachment!.Value;

        Assert.Equal(RhiLoadOp.Clear, depth.DepthLoad);
        Assert.Equal(1f, depth.DepthClearValue);

        // And thrown away afterwards: the depth a card wants was written to a page of its own.
        Assert.Equal(RhiStoreOp.Discard, depth.DepthStore);

        foreach (RhiColorAttachment attachment in recorded.Descriptor.ColorAttachments)
        {
            Assert.Equal(RhiStoreOp.Store, attachment.Store);
            Assert.Equal(RhiClearColor.Transparent, attachment.ClearValue);
        }
    }

    [Fact]
    public void The_pages_are_the_formats_the_capture_pipeline_was_built_for()
    {
        // A pipeline whose targets do not match its pass is refused, and the two are stated in one place
        // so they cannot drift.
        using RecordingRhiDevice device = new();
        using CardAtlas atlas = new(device, 16, 16);

        for (int i = 0; i < CardAtlas.TargetCount; i++)
        {
            Assert.Equal(CardAtlas.Formats[i], atlas.Target(i)!.Format);
        }

        Assert.Null(atlas.Target(CardAtlas.TargetCount));
        Assert.Null(atlas.Target(-1));
    }

    [Fact]
    public void A_page_is_taken_by_both_the_viewport_and_the_scissor()
    {
        // The viewport decides where the card's clip space lands; the scissor is what stops a model drawn
        // wider than its own card from writing over the page beside it.
        using RecordingRhiDevice device = new();
        using CardAtlas atlas = new(device, 64, 64);

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("capture");

        using (IRhiRenderPassEncoder pass = atlas.Begin(encoder))
        {
            CardAtlas.SetPage(pass, 16, 32, 8, 4);
        }

        device.Submit([encoder.Finish()]);

        Assert.Equal(
            new RecordedRhiCommand.SetViewport(16, 32, 8, 4, 0f, 1f),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.SetViewport>()));

        Assert.Equal(
            new RecordedRhiCommand.SetScissor(16, 32, 8, 4),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.SetScissor>()));
    }
}
