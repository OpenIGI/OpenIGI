using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>That the RHI's occlusion queries reach the Vulkan renderer's verdicts, on its hysteresis.</summary>
public sealed class OcclusionQueriesTests
{
    private const int Slots = 2;

    [Fact]
    public void A_query_never_probed_is_visible()
    {
        // The direct path draws no geometry pass to probe in, and everything it asks about must draw.
        using RecordingRhiDevice rhi = new();
        using OcclusionQueries queries = Open(rhi);

        Assert.True(queries.Visible(queries.Create()));
        Assert.True(queries.Visible(OcclusionQueryHandle.None));
    }

    [Fact]
    public void Two_empty_results_hide_an_object_and_one_sample_shows_it_again()
    {
        using RecordingRhiDevice rhi = new();
        using OcclusionQueries queries = Open(rhi);
        OcclusionQueryHandle query = queries.Create();

        // One empty result is a camera-edge race, not a verdict.
        Probe(rhi, queries, query, slot: 0, samples: 0);
        queries.BeginFrame(1);
        queries.BeginFrame(0);
        Assert.True(queries.Visible(query));

        Probe(rhi, queries, query, slot: 0, samples: 0);
        queries.BeginFrame(1);
        queries.BeginFrame(0);
        Assert.False(queries.Visible(query));

        Probe(rhi, queries, query, slot: 0, samples: 12);
        queries.BeginFrame(1);
        queries.BeginFrame(0);
        Assert.True(queries.Visible(query));
    }

    [Fact]
    public void A_hidden_object_nothing_probes_any_more_is_drawn_again()
    {
        // A switch to the vanilla preset stops the probing -- its direct path has no geometry pass to draw a
        // box into. What was hidden at that moment must not stay hidden: the terrain behind a fence did, and
        // the frame's black showed through the fence's holes.
        using RecordingRhiDevice rhi = new();
        using OcclusionQueries queries = Open(rhi);
        OcclusionQueryHandle query = queries.Create();

        Probe(rhi, queries, query, slot: 0, samples: 0);
        queries.BeginFrame(1);
        Probe(rhi, queries, query, slot: 0, samples: 0);
        queries.BeginFrame(1);
        queries.BeginFrame(0);
        Assert.False(queries.Visible(query));

        for (int frame = 0; frame < 16; frame++)
        {
            queries.BeginFrame(frame % Slots);
        }

        Assert.True(queries.Visible(query));
    }

    [Fact]
    public void An_object_is_not_probed_again_while_its_result_is_on_its_way()
    {
        using RecordingRhiDevice rhi = new();
        using OcclusionQueries queries = Open(rhi);
        OcclusionQueryHandle query = queries.Create();

        queries.BeginFrame(0);
        IRhiRenderPassEncoder pass = OpenPass(rhi, queries, out IRhiCommandEncoder encoder);
        queries.Issue(pass, query, Matrix4f.Identity, Box);
        queries.Issue(pass, query, Matrix4f.Identity, Box);
        pass.EndPass();
        pass.Dispose();

        Assert.Equal(1, CountDraws(encoder));
    }

    [Fact]
    public void A_slot_whose_results_have_not_come_back_probes_nothing()
    {
        // A browser's map can take longer than a frame; the buffer it reads cannot be copied into meanwhile.
        using RecordingRhiDevice rhi = new() { HoldsReadbacks = true };
        using OcclusionQueries queries = Open(rhi);
        OcclusionQueryHandle first = queries.Create();
        OcclusionQueryHandle second = queries.Create();

        Probe(rhi, queries, first, slot: 0, samples: 0);
        queries.BeginFrame(1);
        queries.BeginFrame(0);

        IRhiRenderPassEncoder pass = OpenPass(rhi, queries, out IRhiCommandEncoder encoder);
        queries.Issue(pass, second, Matrix4f.Identity, Box);
        pass.EndPass();
        pass.Dispose();

        Assert.Equal(0, CountDraws(encoder));

        rhi.ReleaseReadbacks();
        queries.BeginFrame(1);
        queries.BeginFrame(0);
        Assert.True(queries.Visible(first));
    }

    private static readonly OcclusionBounds Box = new(new Vector3f(-1f, -1f, -1f), new Vector3f(1f, 1f, 1f));

    private static OcclusionQueries Open(RecordingRhiDevice rhi) =>
        new(rhi, Slots, [RhiTextureFormat.Rgba8Unorm], RhiTextureFormat.Depth32Float);

    /// <summary>One frame on a slot: its box probed, the resolve fed a sample count, the map asked for.</summary>
    private static void Probe(RecordingRhiDevice rhi, OcclusionQueries queries, OcclusionQueryHandle query, int slot, ulong samples)
    {
        queries.BeginFrame(slot);

        using (IRhiRenderPassEncoder pass = OpenPass(rhi, queries, out IRhiCommandEncoder encoder))
        {
            queries.Issue(pass, query, Matrix4f.Identity, Box);
            pass.EndPass();

            // The recorder's resolve moves no bytes, so the count is put where the GPU would write it.
            RecordedBuffer resolved = rhi.Resources.OfType<RecordedBuffer>().Single(b => b.Label == $"occlusion{slot}.resolved");
            BitConverter.TryWriteBytes(resolved.Contents.AsSpan(0, sizeof(ulong)), samples);

            queries.ResolveFrame(encoder);
        }

        queries.RequestReadback();
    }

    private static IRhiRenderPassEncoder OpenPass(RecordingRhiDevice rhi, OcclusionQueries queries, out IRhiCommandEncoder encoder)
    {
        IRhiTexture target = rhi.CreateTexture(new RhiTextureDescriptor(8, 8, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.RenderTarget));
        encoder = rhi.CreateCommandEncoder("frame");
        return encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(target.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            OcclusionQuerySet: queries.CurrentSet,
            Label: "geometry"));
    }

    private static int CountDraws(IRhiCommandEncoder encoder) =>
        ((RecordingRhiEncoder)encoder).Commands.OfType<RecordedRhiCommand.Draw>().Count();
}
