using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// Averages the traced sun shadow over time — the temporal half of the reference's shadow denoiser.
/// </summary>
/// <remarks>
/// See the ShadowAccumulate module for why the mask has to be averaged here rather than
/// left to the temporal filter downstream. It runs immediately after the spatial reconstruction and
/// before anything turns the mask into light.
/// </remarks>
internal sealed class ShadowAccumulatePass : IDisposable
{
    private const int ParamSize = SunShadowParameters.AccumulateFloats * sizeof(float);

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,         // the scene depth, to reproject and to validate against
        ComputeBindingKind.SampledStorageImage,  // the previous frame slot's accumulated mask
        ComputeBindingKind.StorageImage,         // this frame's reconstructed mask, read and written
        ComputeBindingKind.StorageImage,         // the accumulation carried forward
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShadowAccumulatePass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public ShadowAccumulatePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight)
        => _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.ShadowAccumulate,
            Bindings,
            ParamSize,
            framesInFlight);

    /// <summary>Records the accumulation for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="depthView">The scene depth.</param>
    /// <param name="previousHistoryView">The previous frame slot's accumulated mask.</param>
    /// <param name="shadowView">The reconstructed mask, read and written in place.</param>
    /// <param name="historyView">The accumulation to carry forward.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="viewToWorld">This frame's view rotation.</param>
    /// <param name="previousViewToWorld">The previous frame's view rotation.</param>
    /// <param name="cameraDelta">This frame's camera position less the previous frame's.</param>
    /// <param name="unprojection">This frame's projection scales and planes.</param>
    /// <param name="previousUnprojection">The previous frame's projection scales and planes.</param>
    /// <param name="frames">How many frames the running mean spans.</param>
    /// <param name="hasHistory">Whether the previous slot holds an accumulation at this size.</param>
    /// <param name="jitterX">This frame's horizontal sub-pixel projection offset, in history-space pixels.</param>
    /// <param name="jitterY">This frame's vertical sub-pixel projection offset, in history-space pixels.</param>
    /// <param name="previousJitterX">The previous frame's, which is where its answers were stored.</param>
    /// <param name="previousJitterY">The previous frame's, which is where its answers were stored.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView previousHistoryView,
        ImageView shadowView,
        ImageView historyView,
        int width,
        int height,
        Matrix3f viewToWorld,
        Matrix3f previousViewToWorld,
        Vector3f cameraDelta,
        DepthUnprojection unprojection,
        DepthUnprojection previousUnprojection,
        float frames,
        bool hasHistory,
        float jitterX,
        float jitterY,
        float previousJitterX,
        float previousJitterY)
    {
        Span<float> data = stackalloc float[ParamSize / sizeof(float)];

        SunShadowParameters.WriteAccumulate(
            data,
            new TemporalFrameState(
                viewToWorld,
                previousViewToWorld,
                cameraDelta,
                unprojection,
                previousUnprojection,
                jitterX,
                jitterY,
                previousJitterX,
                previousJitterY,
                width,
                height),
            frames,
            hasHistory);

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(previousHistoryView),
            new ComputeResource(shadowView),
            new ComputeResource(historyView),
        ];

        _pass.Record(cmd, frame, resources, data, (uint)((width + 7) / 8), (uint)((height + 7) / 8));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pass.Dispose();
    }
}
