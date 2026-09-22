using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// A whole deferred frame, described once and drawn on both backends, arriving at the same picture.
/// </summary>
/// <remarks>
/// <para>
/// The sequence the game itself drives: a scene capture opened, a geometry pass, world geometry through
/// the ordinary mesh path, the capture closed — which is where the shading runs — and a resolve onto
/// the frame. Nothing here says which backend it is for, and nothing here knows there are three passes
/// rather than one.
/// </para>
/// <para>
/// This is the gate the whole move is for. Everything else proves a pass can run; this proves the frame
/// a player would see is the same frame.
/// </para>
/// </remarks>
public sealed class DeferredFrameAgreementTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [Fact]
    public void Both_backends_draw_the_same_deferred_frame()
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
        int worst = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            int difference = Math.Abs(vulkan[i] - webgpu[i]);

            if (difference > 0)
            {
                differing++;
                worst = Math.Max(worst, difference);
            }
        }

        output.WriteLine($"{differing} channel(s) differ, worst by {worst}");
        output.WriteLine($"vulkan centre {Describe(vulkan)}");
        output.WriteLine($"vulkan corner {Describe(vulkan, 2, 2)}");
        output.WriteLine($"webgpu centre {Describe(webgpu)}");

        // One is what a different rasteriser's edge rules earn, and no more: the two run the same
        // shaders over the same geometry.
        Assert.True(worst <= 1, $"The two backends disagree by {worst}, over {differing} channel(s).");

        // And a triangle was drawn, shaded and resolved rather than the frame being one flat colour: the
        // middle is the surface's own green and the corner, which no triangle reached, is the sky. Two
        // backends agree about a uniform frame perfectly, so the comparison above is worth only as much
        // as this.
        Assert.True(vulkan[Centre + 1] > vulkan[Centre], "The middle of the frame is not the green that was drawn.");
        Assert.True(vulkan[Centre + 1] > vulkan[Centre + 2], "The middle of the frame is not the green that was drawn.");

        int corner = ((2 * Size) + 2) * 4;
        Assert.True(
            Math.Abs(vulkan[corner + 1] - vulkan[Centre + 1]) > 8,
            "The corner and the middle shaded the same, so nothing was drawn into the G-buffer.");
    }

    /// <summary>The frame everything here compares, written once and run twice.</summary>
    private static void DrawFrame(GraphicsDevice device)
    {
        Assert.True(
            device.BeginSceneCapture(new SceneTargetDescription(Size, Size, Deferred: true)),
            "The capture was declined.");

        // What a frame tells the device before it draws: where the eye is, and how a depth becomes a
        // position. Both arrive with passes this path does not run, which is why they are set here
        // rather than assumed.
        device.SetShadows(
            ShadowSettings.Off,
            [],
            new DepthUnprojection(1f, 1f, 0.1f, 1000f),
            Matrix3f.Identity);

        device.SetTemporalAntiAliasing(
            default,
            Matrix4f.Identity,
            Vector3d.Zero,
            Matrix3f.Identity);

        device.SetLighting(SceneLighting.Baked);
        device.SetProjection(Matrix4f.Identity);
        device.SetDepthState(DepthState.TestAndWrite);
        device.SetCullMode(CullMode.None);
        device.SetModelAmbient(Vector3f.One);

        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(-0.7f, -0.7f, 0.5f), Vector3f.UnitZ, 0f, 0f, colour: new Bgra32(0, 220, 40, 255)),
                new Vertex3D(new Vector3f(0.7f, -0.7f, 0.5f), Vector3f.UnitZ, 1f, 0f, colour: new Bgra32(0, 220, 40, 255)),
                new Vertex3D(new Vector3f(0f, 0.7f, 0.5f), Vector3f.UnitZ, 0.5f, 1f, colour: new Bgra32(0, 220, 40, 255)),
            ],
            [0, 1, 2]);

        device.BeginPass(ScenePass.Geometry);
        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
        device.EndPass();

        device.EndSceneCapture();

        // Stated rather than defaulted: PostProcessSettings is a record struct, so `new()` runs the
        // ZEROING constructor and none of its declared defaults -- an exposure of nought, a saturation
        // of nought, a white point of nought. That is not "no grading"; it is a grey frame, and two
        // backends agree about a grey frame perfectly.
        device.ResolveScene(new PostProcessSettings(
            ToneMapOperator.Neutral,
            Exposure: 1f,
            WhitePoint: 1.6f,
            Contrast: 1f,
            Saturation: 1f));
        device.SwapBuffers();
    }

    private static byte[]? TryOnVulkan(out string? reason)
    {
        reason = null;

        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(Size, Size, GraphicsDevice.DefaultPresentFormat);

            using (GraphicsDevice device = new(vulkan.Device, new DisplayMode(Size, Size, 32, 60), deferredFrame: true))
            {
                DrawFrame(device);
            }

            return Read(vulkan.Device, vulkan.OffscreenTarget(0));
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
            using WebGpuRhiDevice rhi = WebGpuRhiDevice.Create();
            rhi.AttachOffscreen(Size, Size, GraphicsDevice.DefaultPresentFormat);

            using (GraphicsDevice device = new(rhi, new DisplayMode(Size, Size, 32, 60), deferredFrame: true))
            {
                DrawFrame(device);
            }

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

        ReadOnlySpan<byte> padded = data.GetData();
        byte[] pixels = new byte[Size * Size * 4];

        for (int y = 0; y < Size; y++)
        {
            padded.Slice(y * row, Size * 4).CopyTo(pixels.AsSpan(y * Size * 4));
        }

        return pixels;
    }

    private static int Centre => ((((Size / 2) * Size) + (Size / 2)) * 4);

    private static string Describe(byte[] pixels) => Describe(pixels, Size / 2, Size / 2);

    private static string Describe(byte[] pixels, int x, int y)
    {
        int at = (((y * Size) + x) * 4);
        return $"b{pixels[at]} g{pixels[at + 1]} r{pixels[at + 2]}";
    }
}
