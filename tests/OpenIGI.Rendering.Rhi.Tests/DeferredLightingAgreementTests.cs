using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The deferred shading, run on both backends, shading the same G-buffer into the same colour.
/// </summary>
/// <remarks>
/// <para>
/// The largest shader in the renderer and the one with the most to bind: thirty-odd slots, a probe
/// field, an arrayed depth map read through a comparison sampler, two constant blocks and a push block.
/// Nearly all of it is bound to a stand-in here, because that is the state the renderer over the RHI is
/// in — and the stand-ins are the part most likely to be of the wrong shape, which is a thing only one
/// of these two backends will say out loud.
/// </para>
/// <para>
/// The G-buffer is cleared to a chosen colour rather than drawn, so the two runs start from bytes that
/// cannot differ, and what is compared is the lit half-float colour the shading produced from them.
/// </para>
/// </remarks>
public sealed class DeferredLightingAgreementTests(ITestOutputHelper output)
{
    private const int Size = 32;

    [Fact]
    public void Both_backends_shade_the_same_g_buffer_the_same_way()
    {
        Half[]? vulkan = TryShadeOnVulkan(out string? vulkanReason);
        Half[]? webgpu = TryShadeOnWebGpu(out string? webgpuReason);

        if (vulkan is null || webgpu is null)
        {
            Assert.Fail(
                "Both backends are needed to compare them, and one was missing: "
                + (vulkan is null ? vulkanReason : webgpuReason));

            return;
        }

        float worst = 0f;
        int differing = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            float left = (float)vulkan[i];
            float right = (float)webgpu[i];

            Assert.False(float.IsNaN(left) || float.IsNaN(right), $"Channel {i} shaded to NaN.");

            float difference = Math.Abs(left - right);

            if (difference > 0f)
            {
                differing++;
                worst = Math.Max(worst, difference);
            }
        }

        output.WriteLine($"{differing} channel(s) differ, worst by {worst}");
        output.WriteLine($"vulkan centre {Describe(vulkan)}");
        output.WriteLine($"webgpu centre {Describe(webgpu)}");

        // One ulp of a half float at the magnitudes this frame produces, and no more: the two run the
        // same shader over the same texels, so anything larger is a difference of meaning.
        Assert.True(worst <= 0.002f, $"The two backends shade differently, worst by {worst}.");

        // And that something was actually shaded. Two frames of nothing agree perfectly, so the
        // comparison above is worth only as much as this: the lit target was cleared to black and the
        // draw is the only thing that touched it.
        Assert.True(
            (float)vulkan[Centre] > 0f,
            "The lit frame is black, so the two backends agreed about nothing having been drawn.");
    }

    /// <summary>The red channel of the middle texel, which the triangle certainly covers.</summary>
    private static int Centre => ((((Size / 2) * Size) + (Size / 2)) * 4);

    /// <summary>Shades one frame, written once and run twice.</summary>
    private static void Shade(IRhiDevice device, SceneTargetSet targets, DeferredLighting lighting)
    {
        // A G-buffer of chosen bytes. Cleared rather than drawn, so nothing about a rasteriser can make
        // the two runs start from different texels.
        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("gbuffer"))
        {
            using (encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [
                    Cleared(targets, SceneTargetId.BaseColour, new RhiClearColor(0.5f, 0.25f, 0.125f, 1f)),
                    Cleared(targets, SceneTargetId.Normal, new RhiClearColor(0.5f, 0.5f, 1f, 1f)),
                    Cleared(targets, SceneTargetId.Material, new RhiClearColor(0f, 0.5f, 0.8f, 0f)),
                    Cleared(targets, SceneTargetId.Baked, new RhiClearColor(0.2f, 0.2f, 0.2f, 1f)),
                ],
                new RhiDepthAttachment(targets.View(SceneTargetId.Depth), DepthClearValue: 0.5f),
                Label: "gbuffer")))
            {
            }

            device.Submit([encoder.Finish()]);
        }

        lighting.BindFrame(targets, frameInFlight: 0, GiBlock(), LampBlock());

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("shading"))
        {
            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [Cleared(targets, SceneTargetId.Lit, RhiClearColor.Transparent)],
                Label: "shading")))
            {
                pass.SetViewport(0, 0, Size, Size, 0f, 1f);
                pass.SetScissor(0, 0, Size, Size);
                lighting.Draw(pass, PushBlock(lighting.PushDataBytes));
            }

            device.Submit([encoder.Finish()]);
        }
    }

    private static RhiColorAttachment Cleared(SceneTargetSet targets, SceneTargetId id, RhiClearColor colour) =>
        new(targets.View(id), RhiLoadOp.Clear, RhiStoreOp.Store, colour);

    /// <summary>The GI block, filled only where a zero would divide.</summary>
    /// <remarks>
    /// The depth unprojection is the one part that cannot be left at nought: the shading turns a depth
    /// into a view position with it, and a near and far plane of zero produce a NaN that spreads through
    /// every term after it. Everything else left at nought is the field switched off, which is the state
    /// this renderer is actually in.
    /// </remarks>
    private static float[] GiBlock()
    {
        float[] block = new float[DeferredLightingBlocks.GiBytes / sizeof(float)];

        block[0] = 1f;
        block[1] = 1f;
        block[2] = 0.1f;
        block[3] = 1000f;

        return block;
    }

    private static float[] LampBlock() => new float[DeferredLightingBlocks.LampBytes / sizeof(float)];

    /// <summary>The per-draw block: a sun overhead, and nothing else.</summary>
    private static byte[] PushBlock(int bytes)
    {
        byte[] push = new byte[bytes];

        // The first vector is the view-space sun direction and the lighting model, and the model is
        // nought -- baked -- which is the one that needs nothing else in the block to be meaningful.
        Span<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(push.AsSpan());
        floats[1] = 1f;

        return push;
    }

    private static Half[]? TryShadeOnVulkan(out string? reason)
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

    private static Half[]? TryShadeOnWebGpu(out string? reason)
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

    private static Half[] Run(IRhiDevice device)
    {
        using SceneTargetSet targets = new(device, Size, Size);
        using DeferredLighting lighting = new(device, framesInFlight: 1, RhiTextureFormat.Rgba16Float);

        Shade(device, targets, lighting);

        return Read(device, targets[SceneTargetId.Lit]);
    }

    /// <summary>Brings the lit frame back to the host as the half floats it is.</summary>
    private static Half[] Read(IRhiDevice device, IRhiTexture lit)
    {
        const int BytesPerTexel = 8;
        int row = RhiBufferTextureCopy.AlignRow(Size * BytesPerTexel);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "lit.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("read"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(lit),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(10)), "The lit frame never came back.");

        ReadOnlySpan<byte> bytes = data.GetData();
        Half[] texels = new Half[Size * Size * 4];

        for (int y = 0; y < Size; y++)
        {
            ReadOnlySpan<Half> line = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(
                bytes.Slice(y * row, Size * BytesPerTexel));

            line.CopyTo(texels.AsSpan(y * Size * 4));
        }

        return texels;
    }

    private static string Describe(Half[] texels) =>
        $"r{(float)texels[Centre]} g{(float)texels[Centre + 1]} b{(float)texels[Centre + 2]}";
}
