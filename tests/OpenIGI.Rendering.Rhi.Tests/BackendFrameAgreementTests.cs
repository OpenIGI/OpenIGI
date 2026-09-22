using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The same frame, drawn by the same renderer, on both backends — and the same picture.
/// </summary>
/// <remarks>
/// <para>
/// This is the claim the whole port rests on. Everything else proves that a backend can do a thing;
/// this proves the thing is the SAME thing. One <see cref="GraphicsDevice"/>, one sequence of calls,
/// two implementations of the interface underneath it, and two images compared texel by texel.
/// </para>
/// <para>
/// Nothing in the frame is chosen to be easy. A clear, a world mesh through the forward pipeline with
/// its push block and its specialisation constants, and a sprite over it through the 2D path — which
/// between them exercise the clip-space convention, the push emulation, the derived layouts and the
/// texture bindings, every one of which the two backends reach differently.
/// </para>
/// </remarks>
public sealed class BackendFrameAgreementTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [Fact]
    public void Both_backends_draw_the_same_frame()
    {
        byte[]? vulkan = TryDrawOnVulkan(out string? vulkanReason);
        byte[]? webgpu = TryDrawOnWebGpu(out string? webgpuReason);

        if (vulkan is null || webgpu is null)
        {
            // Reported rather than asserted: a machine with only one of the two still runs every other
            // test in the suite, and a failure here would say nothing about the code.
            Assert.Fail(
                "Both backends are needed to compare them, and one was missing: "
                + (vulkan is null ? vulkanReason : webgpuReason));

            return;
        }

        int differing = 0;
        int worst = 0;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int difference = Math.Abs(At(vulkan, x, y, channel) - At(webgpu, x, y, channel));

                    if (difference > 0)
                    {
                        differing++;
                        worst = Math.Max(worst, difference);
                    }
                }
            }
        }

        output.WriteLine($"{differing} channel(s) differ, worst by {worst}");
        output.WriteLine($"vulkan centre {Describe(vulkan, Size / 2, Size / 2)}");
        output.WriteLine($"webgpu centre {Describe(webgpu, Size / 2, Size / 2)}");

        // One is the tolerance a different rasteriser's edge rules earn, and no more: the two run the
        // same shader over the same geometry, so anything larger is a difference of meaning.
        Assert.True(worst <= 1, $"The two backends disagree by {worst}, over {differing} channel(s).");
    }

    /// <summary>Draws the frame everything else here compares.</summary>
    /// <remarks>
    /// Written once and run twice, because a frame described separately for each backend would be two
    /// frames that happen to look alike.
    /// </remarks>
    private static void DrawFrame(GraphicsDevice device)
    {
        Bgra32 green = new(0, 255, 0, 255);
        Bgra32 blue = new(255, 0, 0, 255);

        device.Clear(new Bgra32(0, 0, 255, 255));
        device.SetViewport(new Viewport(0, 0, Size, Size, 0f, 1f));

        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(-0.7f, -0.7f, 0f), Vector3f.UnitZ, 0f, 0f, colour: green),
                new Vertex3D(new Vector3f(0.7f, -0.7f, 0f), Vector3f.UnitZ, 1f, 0f, colour: green),
                new Vertex3D(new Vector3f(0f, 0.7f, 0f), Vector3f.UnitZ, 0.5f, 1f, colour: green),
            ],
            [0, 1, 2]);

        device.SetProjection(Matrix4f.Identity);
        device.SetDepthState(DepthState.TestAndWrite);
        device.SetCullMode(CullMode.None);
        device.SetModelAmbient(Vector3f.One);
        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);

        Vertex2D[] quad =
        [
            new(0f, 0f, 0f, 0f, blue),
            new(16f, 0f, 1f, 0f, blue),
            new(16f, 16f, 1f, 1f, blue),
            new(0f, 16f, 0f, 1f, blue),
        ];

        device.Set2DProjection(Size, Size);
        device.DrawTriangles2D(quad, [0, 1, 2, 0, 2, 3], TextureHandle.None, BlendMode.Opaque);

        device.SwapBuffers();
    }

    private static byte[]? TryDrawOnVulkan(out string? reason)
    {
        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(Size, Size, GraphicsDevice.PresentFormat);

            using (GraphicsDevice device = new(vulkan.Device, new DisplayMode(Size, Size, 32, 60)))
            {
                DrawFrame(device);
            }

            reason = null;
            return Read(vulkan.Device, vulkan.OffscreenTarget(0));
        }
        catch (Exception error)
        {
            reason = $"No Vulkan device here: {error.Message}";
            return null;
        }
    }

    private static byte[]? TryDrawOnWebGpu(out string? reason)
    {
        try
        {
            using WebGpuRhiDevice rhi = WebGpuRhiDevice.Create();
            rhi.AttachOffscreen(Size, Size, GraphicsDevice.PresentFormat);

            using (GraphicsDevice device = new(rhi, new DisplayMode(Size, Size, 32, 60)))
            {
                DrawFrame(device);
            }

            reason = null;
            return Read(rhi, rhi.OffscreenTarget(0));
        }
        catch (Exception error)
        {
            reason = $"No WebGPU device here: {error.Message}";
            return null;
        }
    }

    /// <summary>Brings one frame back to the host, un-padded.</summary>
    private static byte[] Read(IRhiDevice device, IRhiTexture target)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "frame.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("read"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(target),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(10)), "The frame never came back.");

        ReadOnlySpan<byte> bytes = data.GetData();
        byte[] pixels = new byte[Size * Size * 4];

        for (int y = 0; y < Size; y++)
        {
            bytes.Slice(y * row, Size * 4).CopyTo(pixels.AsSpan(y * Size * 4));
        }

        return pixels;
    }

    private static int At(byte[] pixels, int x, int y, int channel) =>
        pixels[(((y * Size) + x) * 4) + channel];

    private static string Describe(byte[] pixels, int x, int y) =>
        $"b{At(pixels, x, y, 0)} g{At(pixels, x, y, 1)} r{At(pixels, x, y, 2)}";
}
