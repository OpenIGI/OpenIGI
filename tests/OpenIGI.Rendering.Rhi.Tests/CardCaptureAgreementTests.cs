using OpenIGI.Rendering.Pipeline.SurfaceCache;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// That the card capture's four-target pass is accepted, and clears the same way, on both backends.
/// </summary>
/// <remarks>
/// Four colour attachments of three different formats and a depth buffer cleared the other way up from
/// everything else this renderer draws. None of that is exotic and all of it is validated: a browser
/// counts the bytes a sample writes across every attachment and refuses a pass that asks for too many,
/// and a depth clear outside the range is refused outright. What comes back is what the pages hold
/// before anything has been drawn, which is what a reader tests coverage against.
/// </remarks>
public sealed class CardCaptureAgreementTests(ITestOutputHelper output)
{
    private const int Size = 32;

    [Fact]
    public void Both_backends_open_the_capture_and_clear_every_page()
    {
        byte[]? vulkan = TryOnVulkan(out string? vulkanReason);
        byte[]? webgpu = TryOnWebGpu(out string? webgpuReason);

        if (vulkan is null || webgpu is null)
        {
            Assert.Fail(
                "Both backends are needed to compare them, and one was missing: "
                + (vulkan is null ? vulkanReason : webgpuReason));

            return;
        }

        output.WriteLine($"{vulkan.Length} bytes a page");

        Assert.Equal(vulkan, webgpu);

        // Nothing has been drawn, so every page reads as no coverage at all -- which is exactly what a
        // card reader tests, and what the gutters between pages keep holding forever.
        Assert.All(vulkan, value => Assert.Equal(0, value));
    }

    private static byte[]? TryOnVulkan(out string? reason)
    {
        reason = null;

        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(Size, Size, RhiTextureFormat.Rgba8UnormSrgb);
            return Run(vulkan.Device);
        }
        catch (Exception error)
        {
            reason = $"No Vulkan device here: {error.Message}";
            return null;
        }
    }

    private static byte[]? TryOnWebGpu(out string? reason)
    {
        reason = null;

        try
        {
            using WebGpuRhiDevice device = WebGpuRhiDevice.Create();
            return Run(device);
        }
        catch (Exception error)
        {
            reason = $"No WebGPU device here: {error.Message}";
            return null;
        }
    }

    /// <summary>Opens and closes a capture, and brings every page back.</summary>
    private static byte[] Run(IRhiDevice device)
    {
        using CardAtlas atlas = new(device, Size, Size);

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("capture"))
        {
            using (IRhiRenderPassEncoder pass = atlas.Begin(encoder))
            {
                // One page taken, though nothing is drawn into it: taking a page is a viewport and a
                // scissor, and a scissor outside the attachment would be refused.
                CardAtlas.SetPage(pass, 8, 8, 16, 16);
            }

            device.Submit([encoder.Finish()]);
        }

        List<byte> pages = [];

        for (int i = 0; i < CardAtlas.TargetCount; i++)
        {
            pages.AddRange(Read(device, atlas.Target(i)!));
        }

        return [.. pages];
    }

    private static byte[] Read(IRhiDevice device, IRhiTexture page)
    {
        const int BytesPerTexel = 4;
        int row = RhiBufferTextureCopy.AlignRow(Size * BytesPerTexel);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "page.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("read"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(page),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(10)), "The page never came back.");

        ReadOnlySpan<byte> padded = data.GetData();
        byte[] texels = new byte[Size * Size * BytesPerTexel];

        for (int y = 0; y < Size; y++)
        {
            padded.Slice(y * row, Size * BytesPerTexel).CopyTo(texels.AsSpan(y * Size * BytesPerTexel));
        }

        return texels;
    }
}
