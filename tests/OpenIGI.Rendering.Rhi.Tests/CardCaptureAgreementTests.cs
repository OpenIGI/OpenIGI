using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Pipeline.SurfaceCache;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// That the card capture — four targets, its own depth, a page at a time — runs the same on both.
/// </summary>
/// <remarks>
/// <para>
/// Four colour attachments of three different formats, and a depth buffer cleared the other way up from
/// everything else this renderer draws. None of that is exotic and all of it is validated: a browser
/// counts the bytes a sample writes across every attachment and refuses a pass that asks for too many,
/// and a depth clear outside the range is refused outright.
/// </para>
/// <para>
/// What is compared is what a level's load leaves behind: a page with a model drawn into it, and the
/// gutters around it still holding no coverage at all, which is what every reader of the atlas tests
/// against before it trusts a normal or a depth.
/// </para>
/// </remarks>
public sealed class CardCaptureAgreementTests(ITestOutputHelper output)
{
    private const int Size = 32;

    [Fact]
    public void Both_backends_capture_the_same_page()
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

        int differing = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            if (vulkan[i] != webgpu[i])
            {
                differing++;
            }
        }

        output.WriteLine($"{differing} of {vulkan.Length} bytes differ");
        output.WriteLine($"vulkan page centre {Describe(vulkan)}");
        output.WriteLine($"webgpu page centre {Describe(webgpu)}");

        Assert.Equal(0, differing);

        // Something was captured: the albedo's fourth channel is the coverage mask, the atlas was
        // cleared to nought, and the draw is the only thing that could have raised it.
        Assert.True(
            vulkan[Centre + 3] > 0,
            "Nothing was covered, so the two agreed about a page nothing drew into.");

        // And the gutter outside the page is still empty, which is what the scissor is for: a model
        // drawn wider than its own card must not reach the page beside it.
        Assert.Equal(0, vulkan[3]);
    }

    private static byte[]? TryOnVulkan(out string? reason)
    {
        reason = null;

        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(Size, Size, GraphicsDevice.PresentFormat);
            return Capture(vulkan.Device);
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
            device.AttachOffscreen(Size, Size, GraphicsDevice.PresentFormat);
            return Capture(device);
        }
        catch (Exception error)
        {
            reason = $"No WebGPU device here: {error.Message}";
            return null;
        }
    }

    /// <summary>Captures one page, the way a level's load does, and brings the albedo back.</summary>
    private static byte[] Capture(IRhiDevice device)
    {
        using GraphicsDevice graphics = new(device, new DisplayMode(Size, Size, 32, 60));

        MeshHandle mesh = graphics.CreateMesh(
            [
                new Vertex3D(new Vector3f(-0.8f, -0.8f, 0.5f), Vector3f.UnitZ, 0f, 0f, colour: new Bgra32(0, 200, 0, 255)),
                new Vertex3D(new Vector3f(0.8f, -0.8f, 0.5f), Vector3f.UnitZ, 1f, 0f, colour: new Bgra32(0, 200, 0, 255)),
                new Vertex3D(new Vector3f(0f, 0.8f, 0.5f), Vector3f.UnitZ, 0.5f, 1f, colour: new Bgra32(0, 200, 0, 255)),
            ],
            [0, 1, 2]);

        Assert.True(graphics.BeginCardCapture(Size, Size), "The capture was refused.");

        // Half the atlas, so the gutter around it can be checked for the coverage it must not have.
        graphics.SetCardPage(Size / 4, Size / 4, Size / 2, Size / 2);

        graphics.SetProjection(Matrix4f.Identity);
        graphics.SetMaterialOverride(new SurfaceMaterial(Roughness: 0.25f, Metallic: 0.5f));
        graphics.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);

        graphics.EndCardCapture();

        Bgra32[] page = new Bgra32[Size * Size];
        graphics.ReadCardAtlas(0, page);

        byte[] bytes = new byte[page.Length * 4];
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(page.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    /// <summary>The middle texel of the atlas, which is the middle of the page the triangle covers.</summary>
    private static int Centre => ((((Size / 2) * Size) + (Size / 2)) * 4);

    private static string Describe(byte[] page) =>
        $"b{page[Centre]} g{page[Centre + 1]} r{page[Centre + 2]} a{page[Centre + 3]}";
}
