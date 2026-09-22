using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The whole renderer, drawing a whole frame, on a real device.
/// </summary>
/// <remarks>
/// <para>
/// Everything before this proved a piece: a quad, a triangle, a dispatch. This runs the device the game
/// talks to — <c>Clear</c>, a world draw, a sprite over it, <c>SwapBuffers</c> — and reads the presented
/// frame back. It is the first thing in the port that is a renderer rather than a part of one.
/// </para>
/// <para>
/// It draws offscreen because there is no window, which is the same path a headless capture takes.
/// </para>
/// </remarks>
public sealed class GraphicsDeviceOnVulkanTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [VulkanFact]
    public void A_frame_clears_draws_the_world_and_puts_the_interface_over_it()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            VulkanRhiDevice rhi = vulkan!.Rhi;
            rhi.AttachOffscreen(Size, Size, GraphicsDevice.DefaultPresentFormat);

            using GraphicsDevice device = new(rhi, new DisplayMode(Size, Size, 32, 60));

            // A red clear, a green triangle over the middle of it, and a blue sprite in the corner:
            // three colours no two of which could be confused for one another.
            device.Clear(new Bgra32(0, 0, 255, 255));
            device.SetViewport(new Viewport(0, 0, Size, Size, 0f, 1f));

            DrawWorld(device);
            DrawSprite(device);

            device.SwapBuffers();

            byte[] pixels = Read(rhi, 0);

            output.WriteLine($"clear: {Describe(pixels, Size - 4, 4)}");
            output.WriteLine($"world: {Describe(pixels, Size / 2, Size / 2)}");
            output.WriteLine($"sprite: {Describe(pixels, 4, 4)}");

            // The clear reached the corner nothing drew over.
            Assert.Equal((0, 0, 255), Pixel(pixels, Size - 4, 4));

            // The world triangle is green where it covers.
            Assert.Equal((0, 255, 0), Pixel(pixels, Size / 2, Size / 2));

            // The sprite is over the top left.
            Assert.Equal((255, 0, 0), Pixel(pixels, 4, 4));
        }
    }

    [VulkanFact]
    public void Successive_frames_each_present_what_they_drew()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            VulkanRhiDevice rhi = vulkan!.Rhi;
            rhi.AttachOffscreen(Size, Size, GraphicsDevice.DefaultPresentFormat);

            using GraphicsDevice device = new(rhi, new DisplayMode(Size, Size, 32, 60));

            // Two frames of different colours, into different slots of the rotation: the second must not
            // be looking at what the first left, which is what a shared slot would give.
            device.Clear(new Bgra32(0, 0, 255, 255));
            DrawSprite(device);
            device.SwapBuffers();

            device.Clear(new Bgra32(0, 255, 0, 255));
            DrawSprite(device);
            device.SwapBuffers();

            Assert.Equal((0, 0, 255), Pixel(Read(rhi, 0), Size - 4, Size - 4));
            Assert.Equal((0, 255, 0), Pixel(Read(rhi, 1), Size - 4, Size - 4));
        }
    }

    private static void DrawWorld(GraphicsDevice device)
    {
        Bgra32 green = new(0, 255, 0, 255);

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
    }

    private static void DrawSprite(GraphicsDevice device)
    {
        Bgra32 blue = new(255, 0, 0, 255);

        Vertex2D[] quad =
        [
            new(0f, 0f, 0f, 0f, blue),
            new(12f, 0f, 1f, 0f, blue),
            new(12f, 12f, 1f, 1f, blue),
            new(0f, 12f, 0f, 1f, blue),
        ];

        device.Set2DProjection(Size, Size);
        device.DrawTriangles2D(quad, [0, 1, 2, 0, 2, 3], TextureHandle.None, BlendMode.Opaque);
    }

    /// <summary>The blue, green and red of one pixel, which is the order the presented format stores.</summary>
    private static (int B, int G, int R) Pixel(byte[] pixels, int x, int y)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);
        int offset = (y * row) + (x * 4);
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static string Describe(byte[] pixels, int x, int y)
    {
        (int b, int g, int r) = Pixel(pixels, x, y);
        return $"b{b} g{g} r{r}";
    }

    /// <summary>Brings one offscreen frame back to the host.</summary>
    private static byte[] Read(VulkanRhiDevice device, int index)
    {
        IRhiTexture target = device.OffscreenTarget(index);
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
        Assert.True(data.Wait(TimeSpan.FromSeconds(5)), "The frame never came back.");

        return data.GetData().ToArray();
    }
}
