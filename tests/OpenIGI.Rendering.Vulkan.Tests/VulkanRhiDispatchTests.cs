using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The same shader, the same inputs and the same expected answer as the WebGPU suite runs.
/// </summary>
/// <remarks>
/// <para>
/// This is what the interface was built for. The body below is the WebGPU dispatch test with the device
/// swapped and nothing else changed — same module, same bind group shape, same dispatch, same readback —
/// and it asserts the same numbers, worked out by hand from what the shader does rather than from what
/// either backend produced.
/// </para>
/// <para>
/// Two backends agreeing on a number they were both told to compute is the only evidence that the
/// abstraction is real. Everything else — that both compile, that both create a texture — would be just
/// as true of two interfaces that merely looked alike.
/// </para>
/// </remarks>
public sealed class VulkanRhiDispatchTests(ITestOutputHelper output)
{
    private const int RejectionBinding = 0;
    private const int BlendBinding = 2;
    private const int ParamsBinding = 3;

    private const int Width = 8;
    private const int Height = 8;

    [VulkanFact]
    public void The_rejection_dilation_spreads_a_rejecting_half_by_one_pixel()
    {
        using HeadlessVulkan vulkan = Open(output);

        // The left half rejects outright and the right half does not. The median of a neighbourhood
        // wholly inside either half is that half's value; the minimum that follows pulls the boundary
        // one column further right.
        float[] rejection = new float[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                rejection[(y * Width) + x] = x < 4 ? 0f : 1f;
            }
        }

        float[] result = Dilate(vulkan.Rhi, rejection);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float expected = x <= 4 ? 0f : 1f;
                Assert.True(
                    Math.Abs(result[(y * Width) + x] - expected) < 0.01f,
                    $"({x},{y}) came back {result[(y * Width) + x]} and should be {expected}.");
            }
        }
    }

    [VulkanFact]
    public void A_lone_rejecting_texel_is_removed_by_the_median()
    {
        using HeadlessVulkan vulkan = Open(output);

        float[] rejection = new float[Width * Height];
        Array.Fill(rejection, 1f);
        rejection[(4 * Width) + 4] = 0f;

        float[] result = Dilate(vulkan.Rhi, rejection);

        Assert.All(result, value => Assert.True(value > 0.99f, $"A lone outlier survived: {value}."));
    }

    [VulkanFact]
    public void The_storage_texture_keeps_the_format_it_was_asked_for()
    {
        // The difference from the other backend, stated rather than assumed: Vulkan's storage images
        // take the narrow format as declared, so nothing is widened and a one-byte mask costs one byte.
        using HeadlessVulkan vulkan = Open(output);

        using IRhiTexture blend = vulkan.Rhi.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Storage | RhiTextureUsage.CopySource,
            Label: "upscale.blend"));

        Assert.Equal(RhiTextureFormat.R8Unorm, blend.Format);
    }

    private static HeadlessVulkan Open(ITestOutputHelper output)
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        output.WriteLine($"adapter {vulkan!.AdapterName}");
        return vulkan;
    }

    /// <summary>Runs the module over one field of rejection values and reads the answer back.</summary>
    /// <remarks>
    /// The one difference from the WebGPU version, and it is in the data rather than the calls: the
    /// storage texture here is the r8 the shader declares, because SPIR-V keeps the narrow format that
    /// WGSL has to widen. So the texels are bytes rather than sets of four floats.
    /// </remarks>
    private static float[] Dilate(VulkanRhiDevice device, float[] rejection)
    {
        const RhiTextureFormat Storage = RhiTextureFormat.R8Unorm;

        using IRhiShaderModule module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.UpscaleRejectionDilate, Label: "upscale.dilate"));

        using IRhiTexture source = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            Storage,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            Label: "upscale.rejection"));

        using IRhiTexture blend = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            Storage,
            RhiTextureUsage.Storage | RhiTextureUsage.CopySource,
            Label: "upscale.blend"));

        byte[] texels = new byte[Width * Height];
        for (int i = 0; i < rejection.Length; i++)
        {
            texels[i] = (byte)Math.Round(rejection[i] * 255f);
        }

        device.WriteTexture<byte>(
            new RhiTextureRegion(source),
            texels,
            Width,
            new RhiExtent3D(Width, Height));

        using IRhiBuffer parameters = device.CreateBuffer(new RhiBufferDescriptor(
            64,
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            "upscale.params"));

        device.WriteBuffer<float>(parameters, 0, [Width, Height, 1f / Width, 1f / Height]);

        using IRhiBindGroupLayout layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
        [
            new RhiBindGroupLayoutEntry(
                RejectionBinding,
                RhiBindingType.SampledTexture,
                RhiShaderStages.Compute,
                StorageFormat: Storage),
            new RhiBindGroupLayoutEntry(
                BlendBinding,
                RhiBindingType.StorageTexture,
                RhiShaderStages.Compute,
                RhiStorageAccess.WriteOnly,
                Storage),
            new RhiBindGroupLayoutEntry(ParamsBinding, RhiBindingType.UniformBuffer, RhiShaderStages.Compute),
        ], "upscale.dilate"));

        using IRhiBindGroup group = device.CreateBindGroup(new RhiBindGroupDescriptor(
            layout,
            [
                RhiBindGroupEntry.Of(RejectionBinding, source.View),
                RhiBindGroupEntry.Of(BlendBinding, blend.View),
                RhiBindGroupEntry.Of(ParamsBinding, parameters),
            ],
            "upscale.dilate"));

        using IRhiPipelineLayout pipelineLayout = device.CreatePipelineLayout(
            new RhiPipelineLayoutDescriptor([layout], Label: "upscale.dilate"));

        using IRhiComputePipeline pipeline = device.CreateComputePipeline(new RhiComputePipelineDescriptor(
            pipelineLayout,
            new RhiShaderStage(module, "computeMain"),
            "upscale.dilate"));

        int bytesPerRow = RhiBufferTextureCopy.AlignRow(Width);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(bytesPerRow * Height),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "upscale.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("upscale.dilate"))
        {
            using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(Label: "dilate")))
            {
                pass.SetPipeline(pipeline);
                pass.SetBindGroup(0, group);
                pass.Dispatch((Width + 7) / 8, (Height + 7) / 8);
                pass.EndPass();
            }

            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(blend),
                new RhiBufferTextureCopy(readback, 0, bytesPerRow, Height),
                new RhiExtent3D(Width, Height));

            using IRhiCommandBuffer commands = encoder.Finish("upscale.dilate");
            device.Submit([commands]);
        }

        using IRhiReadback answer = device.ReadBuffer(readback);
        Assert.True(answer.Wait(TimeSpan.FromSeconds(10)), "The readback did not complete.");

        ReadOnlySpan<byte> bytes = answer.GetData();
        float[] result = new float[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                result[(y * Width) + x] = bytes[(y * bytesPerRow) + x] / 255f;
            }
        }

        return result;
    }
}
