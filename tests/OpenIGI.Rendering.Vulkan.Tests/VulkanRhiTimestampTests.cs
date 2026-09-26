using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The stage timings' queries on Vulkan, written, reused and resolved the way the renderer does every frame.
/// </summary>
/// <remarks>
/// The end of a pass was never written and the resolve waited for every query, so the first frame the
/// timings were on hung the GPU: the game froze on a white window, or the driver called it a device fault.
/// </remarks>
public sealed class VulkanRhiTimestampTests(ITestOutputHelper output)
{
    private const int Queries = 6;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [VulkanFact]
    public void A_timed_render_pass_and_compute_pass_each_record_a_beginning_and_an_end()
    {
        using HeadlessVulkan vulkan = Open(output);
        using Timing timing = new(vulkan.Rhi);

        ulong[] ticks = timing.Frame(resolveCount: 4);

        Assert.True(ticks[0] > 0 && ticks[1] >= ticks[0], $"render pass {ticks[0]}..{ticks[1]}");
        Assert.True(ticks[2] > 0 && ticks[3] >= ticks[2], $"compute pass {ticks[2]}..{ticks[3]}");
        Assert.True(ticks[2] >= ticks[1], "The compute pass came after the render pass.");
    }

    /// <summary>A pass that was handed timestamps and never recorded must not hold the resolve up.</summary>
    [VulkanFact]
    public void Resolving_a_query_nothing_wrote_still_finishes()
    {
        using HeadlessVulkan vulkan = Open(output);
        using Timing timing = new(vulkan.Rhi);

        // Queries four and five are in the resolved range and were never written.
        ulong[] ticks = timing.Frame(resolveCount: Queries);

        Assert.True(ticks[1] >= ticks[0]);
    }

    /// <summary>Written again the next frame without the host resetting anything, as every frame does.</summary>
    [VulkanFact]
    public void The_same_queries_written_again_report_the_later_frame()
    {
        using HeadlessVulkan vulkan = Open(output);
        using Timing timing = new(vulkan.Rhi);

        ulong[] first = timing.Frame(resolveCount: 4);
        ulong[] second = timing.Frame(resolveCount: 4);

        Assert.True(second[0] > first[3], $"second frame began at {second[0]}, the first ended at {first[3]}");
        Assert.True(second[3] >= second[2] && second[1] >= second[0]);
    }

    private static HeadlessVulkan Open(ITestOutputHelper output)
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);
        output.WriteLine($"adapter {vulkan!.AdapterName}");
        return vulkan;
    }

    /// <summary>One render pass and one compute pass, timed, resolved and read back.</summary>
    private sealed class Timing(IRhiDevice device) : IDisposable
    {
        private readonly IRhiQuerySet _queries =
            device.CreateQuerySet(new RhiQuerySetDescriptor(RhiQueryType.Timestamp, Queries, "timestamps"));

        private readonly IRhiBuffer _resolved = device.CreateBuffer(new RhiBufferDescriptor(
            Queries * sizeof(ulong), RhiBufferUsage.QueryResolve | RhiBufferUsage.CopySource, "resolved"));

        private readonly IRhiBuffer _readback = device.CreateBuffer(new RhiBufferDescriptor(
            Queries * sizeof(ulong), RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead, "readback"));

        private readonly IRhiTexture _target = device.CreateTexture(new RhiTextureDescriptor(
            8, 8, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.RenderTarget, Label: "target"));

        public ulong[] Frame(int resolveCount)
        {
            using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("timed"))
            {
                using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                    [new RhiColorAttachment(_target.View, ClearValue: RhiClearColor.Transparent)],
                    Timestamps: new RhiTimestampWrites(_queries, 0, 1),
                    Label: "timed.render")))
                {
                    pass.EndPass();
                }

                // Left to the using block, as the renderer leaves its compute passes.
                using (encoder.BeginComputePass(new RhiComputePassDescriptor(new RhiTimestampWrites(_queries, 2, 3), "timed.compute")))
                {
                }

                encoder.ResolveQuerySet(_queries, 0, resolveCount, _resolved, 0);
                encoder.CopyBufferToBuffer(_resolved, 0, _readback, 0, (ulong)(resolveCount * sizeof(ulong)));
                device.Submit([encoder.Finish("timed")]);
            }

            using IRhiReadback data = device.ReadBuffer(_readback, 0, (ulong)(resolveCount * sizeof(ulong)));
            Assert.True(data.Wait(Patience), "The frame never finished.");
            return MemoryMarshal.Cast<byte, ulong>(data.GetData()).ToArray();
        }

        public void Dispose()
        {
            _target.Dispose();
            _readback.Dispose();
            _resolved.Dispose();
            _queries.Dispose();
        }
    }
}
