using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Pipeline.Sky;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Sky;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The deferred pair — the shading and the resolve — run on both backends, producing the same frame.
/// </summary>
/// <remarks>
/// <para>
/// The shading is the largest shader in the renderer and the one with the most to bind: thirty-six
/// slots, a probe field, an arrayed depth map read through a comparison sampler, two constant blocks
/// and a push block. Nearly all of it is bound to a stand-in here, because that is the state the
/// renderer over the RHI is in — and the stand-ins are the part most likely to be of the wrong shape,
/// which is a thing only one of these two backends will say out loud.
/// </para>
/// <para>
/// The G-buffer is cleared to a chosen colour rather than drawn, so the two runs start from bytes that
/// cannot differ, and what is compared is what the shading made of them: the half-float lit colour on
/// its own, and then the eight-bit frame the resolve tone-maps it into.
/// </para>
/// </remarks>
public sealed class DeferredAgreementTests(ITestOutputHelper output)
{
    private const int Size = 32;

    /// <summary>The format the resolve draws the frame in, which is what a swapchain holds.</summary>
    private const RhiTextureFormat FrameFormat = RhiTextureFormat.Rgba8UnormSrgb;

    [BothBackendsFact]
    public void Both_backends_shade_the_same_g_buffer_the_same_way() =>
        Compare(throughResolve: false, tolerance: 0.002f);

    [BothBackendsFact]
    public void Both_backends_resolve_the_lit_scene_into_the_same_frame() =>
        Compare(throughResolve: true, tolerance: 1f / 255f);

    private void Compare(bool throughResolve, float tolerance)
    {
        float[]? vulkan = TryOnVulkan(throughResolve, out string? vulkanReason);
        float[]? webgpu = TryOnWebGpu(throughResolve, out string? webgpuReason);

        if (vulkan is null || webgpu is null)
        {
            // Reported rather than skipped: a machine that can reach one backend and not the other is
            // one where this comparison would have said something, and silence would hide that.
            Assert.Fail(
                "Both backends are needed to compare them, and one was missing: "
                + (vulkan is null ? vulkanReason : webgpuReason));

            return;
        }

        float worst = 0f;
        int differing = 0;

        for (int i = 0; i < vulkan.Length; i++)
        {
            Assert.False(
                float.IsNaN(vulkan[i]) || float.IsNaN(webgpu[i]),
                $"Channel {i} came out NaN.");

            float difference = Math.Abs(vulkan[i] - webgpu[i]);

            if (difference > 0f)
            {
                differing++;
                worst = Math.Max(worst, difference);
            }
        }

        output.WriteLine($"{differing} channel(s) differ, worst by {worst}");
        output.WriteLine($"vulkan centre {Describe(vulkan)}");
        output.WriteLine($"webgpu centre {Describe(webgpu)}");

        // One step of the format's own precision, and no more: the two run the same shader over the
        // same texels, so anything larger is a difference of meaning.
        Assert.True(worst <= tolerance, $"The two backends disagree, worst by {worst}.");

        // And that something was actually produced. Two frames of nothing agree perfectly, so the
        // comparison above is worth only as much as this: every target was cleared to black and the
        // draws are the only things that touched them.
        Assert.True(
            vulkan[Centre] > 0f,
            "The frame is black, so the two backends agreed about nothing having been drawn.");
    }

    /// <summary>Draws the frame everything here compares, written once and run twice.</summary>
    private static float[] Run(IRhiDevice device, bool throughResolve)
    {
        using SceneTargetSet targets = new(device, Size, Size);
        using DeferredLighting lighting = new(device, framesInFlight: 1, RhiTextureFormat.Rgba16Float);
        using SkyAtmosphere sky = new(device, framesInFlight: 1);

        // The atmosphere first, and bound into the shading, so three of its slots hold a marched sky
        // rather than a stand-in. It is the only one of the shading's inputs that exists over the RHI
        // yet, and having it here is what keeps this test honest as the others arrive.
        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("sky"))
        {
            using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(Label: "sky")))
            {
                sky.Record(pass, frameInFlight: 0, new SkyConditions(
                    new Vector3f(0.3f, 0.9f, 0.31f),
                    new Vector3f(10f, 10f, 10f),
                    SkyConditions.ViewHeightFor(altitudeMetres: 2f),
                    SkyLightIntensity: 1f));
            }

            device.Submit([encoder.Finish()]);
        }

        lighting.BindSky(sky.SkyViewTable, sky.TransmittanceTable, sky.IrradianceTable, sky.Sampler);

        // A G-buffer of chosen bytes. Cleared rather than drawn, so nothing about a rasteriser can make
        // the two runs start from different texels.
        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("gbuffer"))
        {
            using (encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [
                    Cleared(targets.View(SceneTargetId.BaseColour), new RhiClearColor(0.5f, 0.25f, 0.125f, 1f)),
                    Cleared(targets.View(SceneTargetId.Normal), new RhiClearColor(0.5f, 0.5f, 1f, 1f)),
                    Cleared(targets.View(SceneTargetId.Material), new RhiClearColor(0f, 0.5f, 0.8f, 0f)),
                    Cleared(targets.View(SceneTargetId.Baked), new RhiClearColor(0.2f, 0.2f, 0.2f, 1f)),
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
                [Cleared(targets.View(SceneTargetId.Lit), RhiClearColor.Transparent)],
                Label: "shading")))
            {
                pass.SetViewport(0, 0, Size, Size, 0f, 1f);
                pass.SetScissor(0, 0, Size, Size);
                lighting.Draw(pass, LightPushBlock(lighting.PushDataBytes));
            }

            device.Submit([encoder.Finish()]);
        }

        if (!throughResolve)
        {
            return ReadHalves(device, targets[SceneTargetId.Lit]);
        }

        using IRhiTexture frame = device.CreateTexture(new RhiTextureDescriptor(
            Size,
            Size,
            FrameFormat,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource,
            Label: "frame"));

        using DeferredResolve resolve = new(device, FrameFormat);
        resolve.BindFrame(targets.View(SceneTargetId.Lit));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("resolve"))
        {
            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [Cleared(frame.View, RhiClearColor.Transparent)],
                Label: "resolve")))
            {
                pass.SetViewport(0, 0, Size, Size, 0f, 1f);
                pass.SetScissor(0, 0, Size, Size);
                resolve.Draw(pass, ResolvePushBlock(resolve.PushDataBytes));
            }

            device.Submit([encoder.Finish()]);
        }

        return ReadBytes(device, frame);
    }

    private static RhiColorAttachment Cleared(IRhiTextureView view, RhiClearColor colour) =>
        new(view, RhiLoadOp.Clear, RhiStoreOp.Store, colour);

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

    /// <summary>The shading's per-draw block: a sun overhead, and nothing else.</summary>
    private static byte[] LightPushBlock(int bytes)
    {
        byte[] push = new byte[bytes];

        // The first vector is the view-space sun direction and the lighting model, and the model is
        // nought -- baked -- which is the one that needs nothing else in the block to be meaningful.
        MemoryMarshal.Cast<byte, float>(push.AsSpan())[1] = 1f;

        return push;
    }

    /// <summary>The resolve's per-draw block: an exposure of one, the grade at its identity.</summary>
    /// <remarks>
    /// Both are multiplies whose identity is one rather than nought, and a block of zeros is therefore
    /// not "no grading" but an exposure of nothing and a saturation of nothing — a black frame, or a
    /// grey one. Two backends agree perfectly about a grey frame and it proves nothing, so the two are
    /// set: the first float is the exposure and the seventeenth and eighteenth are the contrast and the
    /// saturation, whose own shader skips them when they are one.
    /// </remarks>
    private static byte[] ResolvePushBlock(int bytes)
    {
        byte[] push = new byte[bytes];
        Span<float> floats = MemoryMarshal.Cast<byte, float>(push.AsSpan());

        floats[0] = 1f;
        floats[16] = 1f;
        floats[17] = 1f;

        return push;
    }

    private static float[]? TryOnVulkan(bool throughResolve, out string? reason)
    {
        reason = null;

        try
        {
            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(Size, Size, FrameFormat);
            return Run(vulkan.Device, throughResolve);
        }
        catch (Exception error)
        {
            reason = $"No Vulkan device here: {error.Message}";
            return null;
        }
    }

    private static float[]? TryOnWebGpu(bool throughResolve, out string? reason)
    {
        reason = null;

        try
        {
            using WebGpuRhiDevice device = WebGpuRhiDevice.Create();
            return Run(device, throughResolve);
        }
        catch (Exception error)
        {
            reason = $"No WebGPU device here: {error.Message}";
            return null;
        }
    }

    /// <summary>Brings the lit frame back as the half floats it is.</summary>
    private static float[] ReadHalves(IRhiDevice device, IRhiTexture lit)
    {
        byte[] bytes = Read(device, lit, bytesPerTexel: 8);
        ReadOnlySpan<Half> halves = MemoryMarshal.Cast<byte, Half>(bytes);

        float[] channels = new float[halves.Length];

        for (int i = 0; i < halves.Length; i++)
        {
            channels[i] = (float)halves[i];
        }

        return channels;
    }

    /// <summary>Brings the resolved frame back as the eight-bit channels it is.</summary>
    private static float[] ReadBytes(IRhiDevice device, IRhiTexture frame)
    {
        byte[] bytes = Read(device, frame, bytesPerTexel: 4);
        float[] channels = new float[bytes.Length];

        for (int i = 0; i < bytes.Length; i++)
        {
            channels[i] = bytes[i] / 255f;
        }

        return channels;
    }

    /// <summary>Copies a texture back to the host, un-padded.</summary>
    private static byte[] Read(IRhiDevice device, IRhiTexture texture, int bytesPerTexel)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * bytesPerTexel);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "frame.readback"));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("read"))
        {
            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(texture),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(10)), "The frame never came back.");

        ReadOnlySpan<byte> padded = data.GetData();
        byte[] texels = new byte[Size * Size * bytesPerTexel];

        for (int y = 0; y < Size; y++)
        {
            padded.Slice(y * row, Size * bytesPerTexel).CopyTo(texels.AsSpan(y * Size * bytesPerTexel));
        }

        return texels;
    }

    /// <summary>The red channel of the middle texel, which the triangle certainly covers.</summary>
    private static int Centre => (((Size / 2) * Size) + (Size / 2)) * 4;

    private static string Describe(float[] channels) =>
        $"r{channels[Centre]} g{channels[Centre + 1]} b{channels[Centre + 2]}";
}
