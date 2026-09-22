using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// A shader from the library, run on the GPU through the RHI, with the answer read back.
/// </summary>
/// <remarks>
/// <para>
/// The first end-to-end thing this backend does, and it goes through every piece at once: a module
/// loaded as WGSL, a bind group layout translated from the RHI's, a texture allocated at the format the
/// module was compiled with, a pipeline built, a pass recorded, a dispatch, a copy, and a readback that
/// completes through the event loop.
/// </para>
/// <para>
/// The module is the temporal resolve's rejection dilation, chosen because what it computes can be
/// worked out by hand: the median of each three-by-three neighbourhood, then the minimum of those over
/// three-by-three, which spreads a rejecting region outwards by exactly one pixel. The expected answer
/// below is derived from that, not from running it.
/// </para>
/// </remarks>
public sealed class WebGpuDispatchTests(ITestOutputHelper output)
{
    /// <summary>The module under test, and what its three bindings are.</summary>
    private const int RejectionBinding = 0;
    private const int BlendBinding = 2;
    private const int ParamsBinding = 3;

    private const int Width = 8;
    private const int Height = 8;

    [WebGpuFact]
    public void The_rejection_dilation_spreads_a_rejecting_half_by_one_pixel()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        // The left half rejects outright and the right half does not. The median of a neighbourhood
        // wholly inside either half is that half's value; a neighbourhood straddling the boundary takes
        // the majority. The minimum that follows then pulls the boundary one column further right.
        float[] rejection = new float[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                rejection[(y * Width) + x] = x < 4 ? 0f : 1f;
            }
        }

        float[] result = Dilate(device, rejection);

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

    [WebGpuFact]
    public void A_lone_rejecting_texel_is_removed_by_the_median()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        // Everything believes its history except one texel. The median of nine values with one outlier
        // is not the outlier, so the whole field comes back believing — which is the documented point
        // of taking a median before the minimum, and would not happen if the minimum ran alone.
        float[] rejection = new float[Width * Height];
        Array.Fill(rejection, 1f);
        rejection[(4 * Width) + 4] = 0f;

        float[] result = Dilate(device, rejection);

        Assert.All(result, value => Assert.True(value > 0.99f, $"A lone outlier survived: {value}."));
    }

    [WebGpuFact]
    public void The_storage_texture_is_allocated_at_the_format_the_module_was_compiled_with()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        using IRhiTexture blend = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Storage | RhiTextureUsage.CopySource,
            Label: "upscale.blend"));

        // The module declares rgba32float because that is what the shader compiler emitted for a WGSL
        // target; a texture allocated as the r8 that was asked for would not bind to it.
        Assert.Equal(WebGpuStorageFormats.ForStorage(RhiTextureFormat.R8Unorm), blend.Format);
        Assert.Equal(RhiTextureFormat.R8Unorm, ((WebGpuTexture)blend).RequestedFormat);
        Assert.NotEmpty(device.WidenedFormats);

        output.WriteLine(device.WidenedFormats[0]);
    }

    /// <summary>Runs the module over one field of rejection values and reads the answer back.</summary>
    private static float[] Dilate(WebGpuRhiDevice device, float[] rejection)
    {
        RhiTextureFormat storage = WebGpuStorageFormats.ForStorage(RhiTextureFormat.R8Unorm);

        using IRhiShaderModule module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.UpscaleRejectionDilate, Label: "upscale.dilate"));

        using IRhiTexture source = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            storage,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            Label: "upscale.rejection"));

        using IRhiTexture blend = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            storage,
            RhiTextureUsage.Storage | RhiTextureUsage.CopySource,
            Label: "upscale.blend"));

        // Four floats a texel, because that is the format the module reads and writes. The value the
        // shader looks at is the red channel; the rest are along for the ride.
        float[] texels = new float[Width * Height * 4];
        for (int i = 0; i < rejection.Length; i++)
        {
            texels[i * 4] = rejection[i];
        }

        device.WriteTexture<float>(
            new RhiTextureRegion(source),
            texels,
            Width * 4 * sizeof(float),
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
                StorageFormat: storage),
            new RhiBindGroupLayoutEntry(
                BlendBinding,
                RhiBindingType.StorageTexture,
                RhiShaderStages.Compute,
                RhiStorageAccess.WriteOnly,
                storage),
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

        // Padded to the alignment a copy out of a texture requires. Sixteen bytes a texel over eight
        // texels is 128, which is under the 256 WebGPU insists on, so every row is padded and the read
        // below un-pads it.
        int bytesPerRow = RhiBufferTextureCopy.AlignRow(Width * storage.BytesPerTexel());

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
            ReadOnlySpan<byte> row = bytes.Slice(y * bytesPerRow, Width * 16);
            for (int x = 0; x < Width; x++)
            {
                result[(y * Width) + x] = BitConverter.ToSingle(row.Slice(x * 16, 4));
            }
        }

        return result;
    }
}
