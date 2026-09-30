using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The moved 2D path, drawing for real.
/// </summary>
/// <remarks>
/// <para>
/// The recording device proves the pass records the right frame; this proves the frame draws the right
/// picture. Both are needed and neither substitutes for the other: a pass can record a perfectly
/// well-formed frame that lands its geometry in the wrong half of the screen, and no amount of
/// assertion about commands would notice.
/// </para>
/// <para>
/// It draws a quad over the top-left quarter in green and reads the target back, so where the quad
/// lands is what is being asserted — which is the one thing about a 2D path that a port gets wrong.
/// </para>
/// </remarks>
public sealed class Sprite2DPassOnVulkanTests(ITestOutputHelper output)
{
    private const int Size = 64;
    private const int Half = Size / 2;

    [VulkanFact]
    public void A_quad_in_the_top_left_quarter_lands_in_the_top_left_quarter()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            byte[] pixels = DrawQuad(vulkan!.Rhi);

            output.WriteLine($"top-left: {Describe(pixels, 8, 8)}");
            output.WriteLine($"top-right: {Describe(pixels, Size - 8, 8)}");
            output.WriteLine($"bottom-left: {Describe(pixels, 8, Size - 8)}");

            Assert.Equal((0, 255, 0, 255), Pixel(pixels, 8, 8));
            Assert.Equal((0, 0, 0, 0), Pixel(pixels, Size - 8, 8));
            Assert.Equal((0, 0, 0, 0), Pixel(pixels, 8, Size - 8));
        }
    }

    private static string Describe(byte[] pixels, int x, int y)
    {
        (byte r, byte g, byte b, byte a) = Pixel(pixels, x, y);
        return $"r{r} g{g} b{b} a{a}";
    }

    private static (byte R, byte G, byte B, byte A) Pixel(byte[] pixels, int x, int y)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);
        int offset = (y * row) + (x * 4);
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    /// <summary>Draws one green quad over the top-left quarter and brings the target back.</summary>
    private static byte[] DrawQuad(VulkanRhiDevice device)
    {
        using WhiteTextureSource textures = new(device);
        using Sprite2DPass sprites = new(device, textures, RhiTextureFormat.Rgba8Unorm, framesInFlight: 1);

        using IRhiTexture target = device.CreateTexture(new RhiTextureDescriptor(
            Size,
            Size,
            RhiTextureFormat.Rgba8Unorm,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource,
            Label: "sprite2d.target"));

        int row = RhiBufferTextureCopy.AlignRow(Size * 4);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "sprite2d.readback"));

        Bgra32 green = new(0, 255, 0, 255);
        Vertex2D[] quad =
        [
            new(0f, 0f, 0f, 0f, green),
            new(Half, 0f, 1f, 0f, green),
            new(Half, Half, 1f, 1f, green),
            new(0f, Half, 0f, 1f, green),
        ];

        ushort[] indices = [0, 1, 2, 0, 2, 3];

        sprites.BeginFrame(0, Size, Size);

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("sprite2d"))
        {
            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [new RhiColorAttachment(target.View, RhiLoadOp.Clear, RhiStoreOp.Store)],
                Label: "sprite2d")))
            {
                pass.SetViewport(0, 0, Size, Size);
                pass.SetScissor(0, 0, Size, Size);
                sprites.DrawTriangles(pass, quad, indices, TextureHandle.None, BlendMode.Opaque);
            }

            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(target),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            // The ring is uploaded before the recorded work goes out, not after it.
            sprites.EndFrame();
            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(5)), "The target never came back.");

        return data.GetData().ToArray();
    }

    /// <summary>One white texel, which is what a draw with no texture of its own samples.</summary>
    private sealed class WhiteTextureSource : ITextureBindingSource, IDisposable
    {
        private readonly IRhiTexture _white;
        private readonly IRhiSampler _sampler;

        public WhiteTextureSource(IRhiDevice device)
        {
            _white = device.CreateTexture(new RhiTextureDescriptor(
                1,
                1,
                RhiTextureFormat.Rgba8Unorm,
                RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
                Label: "white"));

            device.WriteTexture<byte>(
                new RhiTextureRegion(_white),
                [255, 255, 255, 255],
                4,
                new RhiExtent3D(1, 1));

            _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "white"));
        }

        public RhiTextureBinding Resolve(TextureHandle handle) => new(0, _white.View, _sampler);

        public void Dispose()
        {
            _sampler.Dispose();
            _white.Dispose();
        }
    }
}
