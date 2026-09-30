using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// A compute module dispatched through a layout nobody wrote out, with the answer checked.
/// </summary>
/// <remarks>
/// <para>
/// The same module and the same answer as the WebGPU suite's dispatch test, which builds its layout by
/// hand: sixty lines restating what the shader already declares. Here the layout is read from the
/// library instead, and the point of running it is that a derived layout binding the wrong texture to
/// the wrong slot would produce a picture rather than an error.
/// </para>
/// <para>
/// The answer is worked out by hand rather than recorded: the median of each three-by-three
/// neighbourhood, then the minimum of those over three-by-three, which spreads a rejecting region
/// outwards by exactly one pixel.
/// </para>
/// </remarks>
public sealed class ComputeProgramOnVulkanTests(ITestOutputHelper output)
{
    private const int Width = 8;
    private const int Height = 8;

    [VulkanFact]
    public void The_dilation_spreads_a_rejecting_half_by_one_pixel()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            // The left half rejects outright and the right half does not. The median of a neighbourhood
            // wholly inside either half is that half's value; one straddling the boundary takes the
            // majority. The minimum that follows pulls the boundary one column further right.
            byte[] rejection = new byte[Width * Height];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    rejection[(y * Width) + x] = x < 4 ? (byte)0 : (byte)255;
                }
            }

            byte[] result = Dilate(vulkan!.Rhi, rejection);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte expected = x <= 4 ? (byte)0 : (byte)255;
                    byte actual = result[(y * Width) + x];

                    Assert.True(
                        Math.Abs(actual - expected) <= 2,
                        $"({x},{y}) came back {actual} and should be {expected}.");
                }
            }
        }
    }

    [VulkanFact]
    public void The_layout_it_derived_is_the_one_the_module_declares()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            using ComputeProgram program = new(vulkan!.Rhi, ShaderModuleId.UpscaleRejectionDilate);

            output.WriteLine($"workgroup {program.WorkgroupSize}, {program.GroupLayouts.Count} group(s)");

            Assert.Equal((8, 8, 1), program.WorkgroupSize);
            Assert.Equal((0, 0), program.Slot("uRejection"));
            Assert.Equal((0, 2), program.Slot("uBlend"));
            Assert.Equal((0, 3), program.Slot("p"));
        }
    }

    /// <summary>Runs the module over one field of rejection values and reads the answer back.</summary>
    /// <remarks>
    /// Nothing here states a binding index or a workgroup count: every resource is asked for by the name
    /// the shader gave it, and the dispatch is sized from what the module declared.
    /// </remarks>
    private static byte[] Dilate(VulkanRhiDevice device, byte[] rejection)
    {
        using ComputePass dilate = new(device, ShaderModuleId.UpscaleRejectionDilate, "upscale.dilate");

        using IRhiTexture source = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            Label: "upscale.rejection"));

        using IRhiTexture blend = device.CreateTexture(new RhiTextureDescriptor(
            Width,
            Height,
            RhiTextureFormat.R8Unorm,
            RhiTextureUsage.Storage | RhiTextureUsage.CopySource,
            Label: "upscale.blend"));

        device.WriteTexture<byte>(
            new RhiTextureRegion(source),
            rejection,
            Width,
            new RhiExtent3D(Width, Height));

        using IRhiBuffer parameters = device.CreateBuffer(new RhiBufferDescriptor(
            64,
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            "upscale.params"));

        device.WriteBuffer<float>(parameters, 0, [Width, Height, 1f / Width, 1f / Height]);

        int bytesPerRow = RhiBufferTextureCopy.AlignRow(Width);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(bytesPerRow * Height),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "upscale.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("upscale.dilate"))
        {
            using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(Label: "dilate")))
            {
                dilate.Bind("uRejection", source.View);
                dilate.Bind("uBlend", blend.View);
                dilate.Bind("p", parameters);

                dilate.Dispatch(pass, Width, Height);
            }

            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(blend),
                new RhiBufferTextureCopy(readback, 0, bytesPerRow, Height),
                new RhiExtent3D(Width, Height));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback answer = device.ReadBuffer(readback);
        Assert.True(answer.Wait(TimeSpan.FromSeconds(10)), "The readback did not complete.");

        ReadOnlySpan<byte> bytes = answer.GetData();
        byte[] result = new byte[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            bytes.Slice(y * bytesPerRow, Width).CopyTo(result.AsSpan(y * Width));
        }

        return result;
    }
}
