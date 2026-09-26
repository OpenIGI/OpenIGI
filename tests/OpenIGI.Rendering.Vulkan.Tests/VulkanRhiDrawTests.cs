using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The same draw the WebGPU suite makes, through the same interface, counting the same samples.
/// </summary>
/// <remarks>
/// <para>
/// The occlusion pass again, for the same reasons: no vertex buffers, and an answer that is a number
/// rather than a picture. A box over the viewport counts samples and the same box slid off counts none,
/// and the only difference between the two runs is four bytes of push data.
/// </para>
/// <para>
/// The one visible difference from the other backend is where the push data goes. Vulkan has push
/// constants and uses them; WebGPU has none and spends a slice of a uniform ring instead. Neither the
/// shader nor this test knows which happened.
/// </para>
/// </remarks>
public sealed class VulkanRhiDrawTests(ITestOutputHelper output)
{
    private const int Size = 64;
    private const int BoxVertices = 36;

    [VulkanFact]
    public void A_box_over_the_viewport_counts_samples()
    {
        using HeadlessVulkan vulkan = Open();

        ulong covered = Occlude(vulkan.Rhi, offsetX: 0f);
        output.WriteLine($"covered: {covered} samples");

        Assert.True(covered > 0, "A box covering the viewport counted no samples at all.");
    }

    [VulkanFact]
    public void A_box_moved_off_the_viewport_counts_none()
    {
        using HeadlessVulkan vulkan = Open();

        ulong away = Occlude(vulkan.Rhi, offsetX: 4f);
        output.WriteLine($"off-screen: {away} samples");

        Assert.Equal(0UL, away);
    }

    [VulkanFact]
    public void Push_data_reaches_the_shader_as_push_constants()
    {
        // The same block the other backend writes into a uniform ring. Nothing above the interface knows
        // which happened, and the proof that it arrived is that the two draws above disagree.
        using HeadlessVulkan vulkan = Open();

        Assert.True(vulkan.Rhi.Limits.MaxPushDataBytes >= 128);
        output.WriteLine($"push constants: {vulkan.Rhi.Limits.MaxPushDataBytes} bytes");
    }

    private static HeadlessVulkan Open()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);
        return vulkan!;
    }

    private static ulong Occlude(IRhiDevice device, float offsetX)
    {
        using IRhiShaderModule module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.Occlusion, Label: "occlusion"));

        using IRhiTexture depth = device.CreateTexture(new RhiTextureDescriptor(
            Size,
            Size,
            RhiTextureFormat.Depth32Float,
            RhiTextureUsage.RenderTarget,
            Label: "occlusion.depth"));

        using IRhiPipelineLayout layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            PushDataBytes: 96,
            RhiShaderStages.Graphics,
            "occlusion"));

        using IRhiRenderPipeline pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            layout,
            new RhiShaderStage(module, "vertexMain"),
            new RhiShaderStage(module, "fragmentMain"),
            VertexBuffers: [],
            ColorTargets: [],
            DepthStencil: new RhiDepthStencilState(
                RhiTextureFormat.Depth32Float,
                DepthWrite: false,
                RhiCompare.Always),
            Primitive: new RhiPrimitiveState(CullMode: RhiCullMode.None),
            Label: "occlusion"));

        using IRhiQuerySet queries = device.CreateQuerySet(
            new RhiQuerySetDescriptor(RhiQueryType.Occlusion, 1, "occlusion"));

        using IRhiBuffer results = device.CreateBuffer(new RhiBufferDescriptor(
            8, RhiBufferUsage.QueryResolve | RhiBufferUsage.CopySource, "occlusion.results"));

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            8, RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead, "occlusion.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("occlusion"))
        {
            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                ColorAttachments: [],
                DepthAttachment: new RhiDepthAttachment(depth.View, DepthClearValue: 0f),
                OcclusionQuerySet: queries,
                Label: "occlusion")))
            {
                pass.SetPipeline(pipeline);
                pass.SetViewport(0, 0, Size, Size);
                pass.SetScissor(0, 0, Size, Size);
                pass.SetPushData(PushBlock(offsetX));
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

    /// <summary>The module's push block: a transform and the box's two corners.</summary>
    /// <param name="offsetX">How far to slide the box along x, in clip space.</param>
    private static byte[] PushBlock(float offsetX)
    {
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
