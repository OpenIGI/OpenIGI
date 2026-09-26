using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// That the recording device records what it was asked to do.
/// </summary>
/// <remarks>
/// It is about to be the thing every moved pass is judged against, so it is worth knowing that it sees
/// the frame the way a backend would: passes in the order they were begun, attachments as they were
/// stated, bind groups where they were bound, and a mistake that a driver would reject rejected here.
/// </remarks>
public sealed class RecordingRhiDeviceTests
{
    [Fact]
    public void The_pass_order_is_the_order_the_passes_were_begun()
    {
        using RecordingRhiDevice device = new();
        IRhiTexture target = device.CreateTexture(
            new RhiTextureDescriptor(8, 8, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.RenderTarget, Label: "lit"));

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("frame");

        using (IRhiComputePassEncoder cull = encoder.BeginComputePass(new RhiComputePassDescriptor(Label: "cull")))
        {
            cull.Dispatch(4, 2);
        }

        using (IRhiRenderPassEncoder draw = encoder.BeginRenderPass(
            new RhiRenderPassDescriptor([new RhiColorAttachment(target.View)], Label: "gbuffer")))
        {
            draw.Draw(3);
        }

        IRhiCommandBuffer commands = encoder.Finish();
        device.Submit([commands]);

        Assert.Equal(["cull", "gbuffer"], device.PassOrder());
        Assert.Equal(
            new RecordedRhiCommand.Dispatch(4, 2, 1),
            Assert.Single(device.CommandsOf<RecordedRhiCommand.Dispatch>()));
    }

    [Fact]
    public void A_render_pass_records_what_it_draws_into_and_what_it_clears()
    {
        using RecordingRhiDevice device = new();
        IRhiTexture colour = device.CreateTexture(
            new RhiTextureDescriptor(8, 8, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.RenderTarget, Label: "albedo"));
        IRhiTexture depth = device.CreateTexture(
            new RhiTextureDescriptor(8, 8, RhiTextureFormat.Depth32Float, RhiTextureUsage.RenderTarget, Label: "depth"));

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder();
        encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(colour.View, RhiLoadOp.Load)],
            new RhiDepthAttachment(depth.View),
            Label: "shade")).EndPass();

        device.Submit([encoder.Finish()]);

        RecordedRhiCommand.BeginRenderPass begun =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.Equal("shade", begun.Label);
        Assert.Equal(["albedo"], begun.ColorAttachments);
        Assert.Equal("depth", begun.DepthAttachment);
        Assert.Equal([false], begun.Clears);
    }

    [Fact]
    public void A_bind_group_is_recorded_where_it_was_bound_and_with_the_offsets_it_was_bound_at()
    {
        using RecordingRhiDevice device = new();
        IRhiBuffer uniforms = device.CreateBuffer(
            new RhiBufferDescriptor(1024, RhiBufferUsage.Uniform, "frame-uniforms"));

        IRhiBindGroupLayout layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.UniformBuffer, RhiShaderStages.Compute, HasDynamicOffset: true)],
            "per-frame"));

        IRhiBindGroup group = device.CreateBindGroup(new RhiBindGroupDescriptor(
            layout,
            [RhiBindGroupEntry.Of(0, uniforms, size: 256)],
            "per-frame"));

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder();

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass())
        {
            pass.SetBindGroup(1, group, [512u]);
        }

        device.Submit([encoder.Finish()]);

        RecordedRhiCommand.SetBindGroup bound =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.SetBindGroup>());

        Assert.Equal(1, bound.Index);
        Assert.Equal("per-frame", bound.Label);
        Assert.Equal([512u], bound.DynamicOffsets);
    }

    [Fact]
    public void Push_data_is_recorded_as_the_bytes_that_were_pushed()
    {
        using RecordingRhiDevice device = new();
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder();

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass())
        {
            pass.SetPushData(MemoryMarshal.AsBytes<int>([7, 9]));
        }

        device.Submit([encoder.Finish()]);

        RecordedRhiCommand.SetPushData pushed =
            Assert.Single(device.CommandsOf<RecordedRhiCommand.SetPushData>());

        Assert.Equal(8, pushed.Bytes.Count);
        Assert.Equal(0, pushed.Offset);
    }

    [Fact]
    public void A_buffer_gives_back_what_was_written_into_it()
    {
        using RecordingRhiDevice device = new();
        IRhiBuffer buffer = device.CreateBuffer(
            new RhiBufferDescriptor(16, RhiBufferUsage.Storage | RhiBufferUsage.MapRead, "results"));

        device.WriteBuffer<uint>(buffer, 0, [1, 2, 3, 4]);

        using IRhiReadback readback = device.ReadBuffer(buffer);

        Assert.True(readback.IsComplete);
        Assert.Equal<uint>([1, 2, 3, 4], MemoryMarshal.Cast<byte, uint>(readback.GetData()).ToArray());
    }

    [Fact]
    public void A_copy_between_buffers_moves_the_bytes_as_well_as_recording_itself()
    {
        using RecordingRhiDevice device = new();
        IRhiBuffer staging = device.CreateBuffer(new RhiBufferDescriptor(8, RhiBufferUsage.CopySource, "staging"));
        IRhiBuffer target = device.CreateBuffer(new RhiBufferDescriptor(8, RhiBufferUsage.CopyDestination, "target"));

        device.WriteBuffer<uint>(staging, 0, [42, 43]);

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder();
        encoder.CopyBufferToBuffer(staging, 0, target, 0, 8);
        device.Submit([encoder.Finish()]);

        using IRhiReadback readback = device.ReadBuffer(target);
        Assert.Equal<uint>([42, 43], MemoryMarshal.Cast<byte, uint>(readback.GetData()).ToArray());
    }

    [Fact]
    public void A_group_that_leaves_a_slot_of_its_layout_empty_is_refused()
    {
        using RecordingRhiDevice device = new();
        IRhiSampler sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "linear"));

        IRhiBindGroupLayout layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [
                new RhiBindGroupLayoutEntry(0, RhiBindingType.SampledTexture, RhiShaderStages.Fragment),
                new RhiBindGroupLayoutEntry(1, RhiBindingType.Sampler, RhiShaderStages.Fragment),
            ],
            "material"));

        ArgumentException refused = Assert.Throws<ArgumentException>(() => device.CreateBindGroup(
            new RhiBindGroupDescriptor(layout, [RhiBindGroupEntry.Of(1, sampler)], "material")));

        Assert.Contains("binding 0", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_that_binds_the_wrong_kind_of_thing_is_refused()
    {
        using RecordingRhiDevice device = new();
        IRhiBuffer buffer = device.CreateBuffer(new RhiBufferDescriptor(64, RhiBufferUsage.Uniform, "wrong"));

        IRhiBindGroupLayout layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.Sampler, RhiShaderStages.Fragment)],
            "material"));

        Assert.Throws<ArgumentException>(() => device.CreateBindGroup(
            new RhiBindGroupDescriptor(layout, [RhiBindGroupEntry.Of(0, buffer)], "material")));
    }

    [Fact]
    public void Recording_anything_while_a_pass_is_open_is_refused()
    {
        using RecordingRhiDevice device = new();
        IRhiBuffer buffer = device.CreateBuffer(new RhiBufferDescriptor(8, RhiBufferUsage.CopySource, "b"));

        using IRhiCommandEncoder encoder = device.CreateCommandEncoder();
        using IRhiComputePassEncoder pass = encoder.BeginComputePass();

        Assert.Throws<InvalidOperationException>(() => encoder.ClearBuffer(buffer));
    }

    [Fact]
    public void A_pass_that_ends_inside_a_debug_group_is_refused()
    {
        using RecordingRhiDevice device = new();
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder();

        IRhiComputePassEncoder pass = encoder.BeginComputePass();
        pass.PushDebugGroup("gather");

        Assert.Throws<InvalidOperationException>(pass.EndPass);
    }

    [Fact]
    public void The_frame_rotates_through_two_sets_of_per_frame_resources()
    {
        using RecordingRhiDevice device = new();
        List<int> slots = [];

        for (int i = 0; i < 4; i++)
        {
            Assert.True(device.BeginFrame(out RhiFrame frame));
            slots.Add(frame.FrameInFlight);
            Assert.Equal(i, frame.Index);
            Assert.True(device.EndFrame(frame));
        }

        Assert.Equal([0, 1, 0, 1], slots);
    }

    [Fact]
    public void A_frame_is_not_handed_out_when_the_surface_needs_reconfiguring()
    {
        using RecordingRhiDevice device = new() { FrameAvailable = false };

        Assert.False(device.BeginFrame(out RhiFrame frame));
        Assert.Equal(default, frame);

        device.ConfigureSurface(1920, 1080, vsync: true);
        device.FrameAvailable = true;

        Assert.True(device.BeginFrame(out RhiFrame after));
        Assert.Equal(1920, after.Target.Texture.Width);
        Assert.Equal(1080, after.Target.Texture.Height);
    }

    [Fact]
    public void A_pipeline_layout_that_asks_for_more_push_data_than_the_device_offers_is_refused()
    {
        using RecordingRhiDevice device = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => device.CreatePipelineLayout(
            new RhiPipelineLayoutDescriptor([], RhiLimits.WebGpuDefaults.MaxPushDataBytes + 1, Label: "too-much")));
    }

    [Fact]
    public void Every_resource_the_device_handed_out_is_disposed_with_it()
    {
        RecordingRhiDevice device = new();
        RecordedResource buffer = (RecordedResource)device.CreateBuffer(
            new RhiBufferDescriptor(8, RhiBufferUsage.Uniform, "u"));

        Assert.False(buffer.IsDisposed);

        device.Dispose();

        Assert.True(buffer.IsDisposed);
        Assert.Single(device.Resources);
    }
}
