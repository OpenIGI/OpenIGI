using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.Temporal;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The whole temporal chain, built and dispatched on a real device.
/// </summary>
/// <remarks>
/// <para>
/// Eight modules, their layouts derived from what they declare, over twenty-odd targets of a dozen
/// formats. What this proves that the recording device cannot is that every one of those layouts is one
/// a driver accepts, that every texture is allocated in a format its binding will take, and that the
/// dispatches run without losing the device.
/// </para>
/// <para>
/// It does not check the picture. What the chain computes is a temporal answer — it needs a moving
/// camera and a history several frames deep to mean anything — and a number read off one frame of a
/// still scene would be a number nobody could interpret. Running it is what is being checked here;
/// what it produces is for the capture comparison.
/// </para>
/// </remarks>
public sealed class TemporalResolveOnVulkanTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [VulkanFact]
    public void The_chain_builds_and_runs_over_the_scene_targets()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            VulkanRhiDevice device = vulkan!.Rhi;

            using SceneTargetSet targets = new(device, Size, Size);
            using ScreenSpacePasses screen = new(device, framesInFlight: 2);
            using TemporalResolve resolve = new(device, framesInFlight: 2);

            // Two frames, because the second is the first that has a history to read: a chain that only
            // worked on the frame where every history was empty would pass a single run.
            for (int frame = 0; frame < 2; frame++)
            {
                using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("upscale"))
                {
                    using (IRhiComputePassEncoder pass = encoder.BeginComputePass(
                        new RhiComputePassDescriptor(Label: "upscale")))
                    {
                        // The whole of what a deferred frame runs between the geometry and the lighting,
                        // in the order it runs it.
                        screen.RecordVelocity(pass, targets, State(), frame);

                        screen.RecordOcclusion(
                            pass,
                            targets,
                            State(),
                            new AmbientOcclusionSettings(),
                            frame,
                            frame,
                            hasHistory: frame > 0);
                    }

                    // In a pass of its own, which it opens after emptying the occluder scatter.
                    resolve.Record(encoder, targets, State(), TemporalResolveSettings.Default, frame);

                    device.Submit([encoder.Finish()]);
                }

                targets.AdvanceFrame();
            }

            device.WaitIdle();
            output.WriteLine($"ran the screen-space and temporal chain twice over {SceneTargetSet.Layout.Count} target(s)");
        }
    }

    [VulkanFact]
    public void A_resize_rebuilds_the_targets_and_the_chain_still_runs()
    {
        // The groups name textures that are gone after a resize, so every one of them has to be rebuilt.
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            VulkanRhiDevice device = vulkan!.Rhi;

            using SceneTargetSet targets = new(device, Size, Size);
            using TemporalResolve resolve = new(device, framesInFlight: 2);

            Run(device, resolve, targets, 0);

            device.WaitIdle();
            targets.Resize(Size * 2, Size / 2);

            Run(device, resolve, targets, 1);
            device.WaitIdle();
        }
    }

    private static void Run(VulkanRhiDevice device, TemporalResolve resolve, SceneTargetSet targets, int frame)
    {
        using IRhiCommandEncoder encoder = device.CreateCommandEncoder("upscale");
        resolve.Record(encoder, targets, State(targets.Width, targets.Height), TemporalResolveSettings.Default, frame);
        device.Submit([encoder.Finish()]);
    }

    private static TemporalFrameState State(int width = Size, int height = Size) => new(
        Matrix3f.Identity,
        Matrix3f.Identity,
        Vector3f.Zero,
        new DepthUnprojection(1f, 1f, 0.1f, 1000f),
        new DepthUnprojection(1f, 1f, 0.1f, 1000f),
        JitterX: 0f,
        JitterY: 0f,
        PreviousJitterX: 0f,
        PreviousJitterY: 0f,
        width,
        height);
}
