using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// Drawing, without a window.
/// </summary>
/// <remarks>
/// <para>
/// The occlusion pass is the module used, because it is the one raster module that needs no vertex
/// buffers at all — it builds a box from its vertex index and the bounds in its push block — and
/// because what it produces is a number rather than a picture. That number is the point: a draw that
/// covers the viewport counts samples, one that is moved off-screen counts none, and the difference
/// proves the whole path rather than the absence of an error.
/// </para>
/// <para>
/// Every piece is exercised: a render pipeline with a depth state, a render pass with a depth
/// attachment, push data through the uniform ring WebGPU makes necessary, an occlusion query, the
/// resolve into a buffer and the readback.
/// </para>
/// </remarks>
public sealed class WebGpuDrawTests(ITestOutputHelper output)
{
    private const int Size = 64;

    /// <summary>The box's thirty-six indices, as the module builds it.</summary>
    private const int BoxVertices = 36;

    [WebGpuFact]
    public void A_box_over_the_viewport_counts_samples()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        ulong covered = Occlude(device, offsetX: 0f);
        output.WriteLine($"covered: {covered} samples");

        Assert.True(covered > 0, "A box covering the viewport counted no samples at all.");
    }

    [WebGpuFact]
    public void A_box_moved_off_the_viewport_counts_none()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        // The same draw, the same pipeline, the same everything except four bytes of push data. If this
        // came back non-zero the push never reached the shader and the first test proved nothing.
        ulong away = Occlude(device, offsetX: 4f);
        output.WriteLine($"off-screen: {away} samples");

        Assert.Equal(0UL, away);
    }

    [WebGpuFact]
    public void The_push_ring_hands_out_a_distinct_aligned_slice_each_time()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();
        WebGpuPushRing ring = device.PushRing;

        byte[] block = new byte[64];
        uint first = ring.Write(block).Offset;
        uint second = ring.Write(block).Offset;

        Assert.NotEqual(first, second);
        Assert.Equal((uint)ring.SliceBytes, second - first);
        Assert.Equal(0u, first % 256u);

        ring.Reset();
        Assert.Equal(first, ring.Write(block).Offset);
    }

    [WebGpuFact]
    public void The_push_ring_grows_a_chunk_for_a_frame_that_outruns_it_and_keeps_it()
    {
        // Capturing the surface cache pushes thousands of times before a frame ends; the ring grows to
        // it rather than refusing, and the next frame starts over in the first chunk.
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();
        using WebGpuPushRing ring = new(device, slices: 2);

        byte[] block = new byte[64];
        WebGpuPushSlice[] landed = [ring.Write(block), ring.Write(block), ring.Write(block)];

        Assert.Equal(2, ring.Chunks);
        Assert.Same(landed[0].Group, landed[1].Group);
        Assert.NotSame(landed[1].Group, landed[2].Group);
        Assert.Equal(0u, landed[2].Offset);

        ring.Reset();
        Assert.Same(landed[0].Group, ring.Write(block).Group);
        Assert.Equal(2, ring.Chunks);
    }

    [WebGpuFact]
    public void The_library_agrees_where_push_data_goes()
    {
        // Read from the compiled modules rather than assumed, the same way the storage formats are. A
        // library whose modules disagreed would be one no single pipeline layout could serve, and
        // WebGpuPushBlocks refuses to answer at all in that case.
        output.WriteLine($"push block at group {WebGpuPushBlocks.Group} binding {WebGpuPushBlocks.Binding}");
        output.WriteLine($"largest block {WebGpuPushBlocks.LargestBlock} bytes");

        Assert.True(WebGpuPushBlocks.Group >= 0);
        Assert.True(WebGpuPushBlocks.LargestBlock > 0);
    }

    /// <summary>Draws the occlusion box and returns how many samples it wrote.</summary>
    private static ulong Occlude(WebGpuRhiDevice device, float offsetX)
    {
        using IRhiShaderModule module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.Occlusion, Label: "occlusion"));

        using IRhiTexture depth = device.CreateTexture(new RhiTextureDescriptor(
            Size,
            Size,
            RhiTextureFormat.Depth32Float,
            RhiTextureUsage.RenderTarget,
            Label: "occlusion.depth"));

        // The push group is the one the shader library puts its push block in, with a dynamic offset so
        // one buffer serves every draw. This is what a push constant costs on a backend that has none.
        using IRhiBindGroupLayout pushLayout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [
                new RhiBindGroupLayoutEntry(
                    WebGpuPushBlocks.Binding,
                    RhiBindingType.UniformBuffer,
                    RhiShaderStages.Graphics,
                    HasDynamicOffset: true),
            ],
            "push"));

        using IRhiBindGroup pushGroup = device.CreateBindGroup(new RhiBindGroupDescriptor(
            pushLayout,
            [RhiBindGroupEntry.Of(WebGpuPushBlocks.Binding, device.PushRing.Buffer, 0, 96)],
            "push"));

        // Empty layouts fill the groups before it: WebGPU takes a pipeline layout as an array, so a push
        // block at group three means four entries whatever the first three hold.
        List<IRhiBindGroupLayout> groups = [];
        List<IRhiBindGroupLayout> empties = [];

        for (int i = 0; i < WebGpuPushBlocks.Group; i++)
        {
            IRhiBindGroupLayout empty = device.CreateBindGroupLayout(
                new RhiBindGroupLayoutDescriptor([], $"empty{i}"));

            empties.Add(empty);
            groups.Add(empty);
        }

        groups.Add(pushLayout);

        try
        {
            using IRhiPipelineLayout layout = device.CreatePipelineLayout(
                new RhiPipelineLayoutDescriptor(groups, Label: "occlusion"));

            using IRhiRenderPipeline pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
                layout,
                new RhiShaderStage(module, "vertexMain"),
                new RhiShaderStage(module, "fragmentMain"),
                VertexBuffers: [],
                ColorTargets: [],
                DepthStencil: new RhiDepthStencilState(
                    RhiTextureFormat.Depth32Float,
                    DepthWrite: false,

                    // Always, because what is being asked is whether the box covers anything at all, not
                    // whether it is in front of something. A reverse-Z test against a cleared buffer
                    // would reject every fragment and the answer would always be nought.
                    RhiCompare.Always),
                Primitive: new RhiPrimitiveState(CullMode: RhiCullMode.None),
                Label: "occlusion"));

            using IRhiQuerySet queries = device.CreateQuerySet(
                new RhiQuerySetDescriptor(RhiQueryType.Occlusion, 1, "occlusion"));

            // Eight bytes a result, and the resolve destination has to be one a copy can read out of.
            using IRhiBuffer results = device.CreateBuffer(new RhiBufferDescriptor(
                8, RhiBufferUsage.QueryResolve | RhiBufferUsage.CopySource, "occlusion.results"));

            using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
                8, RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead, "occlusion.readback"));

            uint pushOffset = device.PushRing.Write(PushBlock(offsetX)).Offset;

            using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("occlusion"))
            {
                using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                    ColorAttachments: [],
                    DepthAttachment: new RhiDepthAttachment(depth.View, DepthClearValue: 0f),
                    OcclusionQuerySet: queries,
                    Label: "occlusion")))
                {
                    pass.SetPipeline(pipeline);
                    pass.SetBindGroup(WebGpuPushBlocks.Group, pushGroup, [pushOffset]);
                    pass.BeginOcclusionQuery(0);
                    pass.Draw(BoxVertices);
                    pass.EndOcclusionQuery();
                    pass.EndPass();
                }

                encoder.ResolveQuerySet(queries, 0, 1, results, 0);
                encoder.CopyBufferToBuffer(results, 0, readback, 0, 8);

                using IRhiCommandBuffer commands = encoder.Finish("occlusion");
                device.Submit([commands]);
            }

            using IRhiReadback answer = device.ReadBuffer(readback);
            Assert.True(answer.Wait(TimeSpan.FromSeconds(10)), "The occlusion readback did not complete.");

            return BitConverter.ToUInt64(answer.GetData());
        }
        finally
        {
            foreach (IRhiBindGroupLayout empty in empties)
            {
                empty.Dispose();
            }
        }
    }

    /// <summary>The module's push block: a transform and the box's two corners.</summary>
    /// <param name="offsetX">How far to slide the box along x, in clip space.</param>
    private static byte[] PushBlock(float offsetX)
    {
        // The identity with a translation, so the box's corners are already in clip space: a unit box
        // from (-1,-1,0) to (1,1,1) fills the viewport, and sliding it by four takes it entirely off.
        float[] values =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            offsetX, 0f, 0f, 1f,

            -1f, -1f, 0.1f, 0f,
            1f, 1f, 0.9f, 0f,
        ];

        byte[] block = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, block, 0, block.Length);
        return block;
    }
}
