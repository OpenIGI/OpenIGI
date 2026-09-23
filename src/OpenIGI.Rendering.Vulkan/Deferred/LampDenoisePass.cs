using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// Turns the lamp trace's one-ray-per-pixel answer into the shadow the shading actually reads.
/// </summary>
/// <remarks>
/// <para>
/// The trace fires a single ray, so what it leaves is a coin toss per pixel and looks like it: grain,
/// not a shadow. Nothing may read that directly, and the reference does not — it runs a
/// temporal accumulation and a spatial filter over its sampled visibility before anything shades with
/// it. This owns both, because they are never useful apart.
/// </para>
/// <para>
/// <b>Temporal first, then spatial</b>, which is the reference's own order and is worth stating because
/// the obvious guess is the other way round. What makes the shadow answer immediately when a lamp is
/// switched is not that the spatial filter runs first; it is that the temporal pass clamps its history
/// into the range this frame's neighbourhood spans before blending onto it. A history of a room that
/// was dark is nowhere near that range, so it is dragged across in one frame, and the distance it moved
/// collapses the accumulation's cap and reopens the spatial filter — which then carries that frame on
/// its own. From the next frame on the accumulation has the picture again and the spatial filter turns
/// itself back off, so a settled shadow is never softened.
/// </para>
/// <para>
/// Both halves run at half resolution, which is where the cost stays reasonable: together they are
/// roughly a third of a millisecond at 1440p against the ray they are cleaning up after.
/// </para>
/// </remarks>
internal sealed class LampDenoisePass : IDisposable
{
    /// <summary>Two <c>mat4</c>s of camera and six <c>vec4</c>s of frame.</summary>
    private const int TemporalParamSize = LampShadowParameters.TemporalFloats * sizeof(float);

    /// <summary>One <c>mat4</c> of camera and three <c>vec4</c>s of frame.</summary>
    private const int SpatialParamSize = LampShadowParameters.SpatialFloats * sizeof(float);

    private static readonly ComputeBindingKind[] TemporalBindings =
    [
        ComputeBindingKind.SampledStorageImage, // the raw trace, half resolution, left in general
        ComputeBindingKind.SampledImage,        // the scene depth, full resolution
        ComputeBindingKind.SampledStorageImage, // the previous frame's accumulation
        ComputeBindingKind.StorageImage,        // this frame's accumulation
    ];

    private static readonly ComputeBindingKind[] SpatialBindings =
    [
        ComputeBindingKind.SampledStorageImage, // the accumulation the temporal half just wrote
        ComputeBindingKind.SampledImage,        // the scene depth, full resolution
        ComputeBindingKind.SampledImage,        // the world normals, full resolution
        ComputeBindingKind.StorageImage,        // what the shading reads
    ];

    private readonly VulkanComputePass _temporal;
    private readonly VulkanComputePass _spatial;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="LampDenoisePass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the passes live on.</param>
    /// <param name="allocator">The allocator backing their parameter buffers.</param>
    /// <param name="framesInFlight">How many frames the descriptor sets are cycled over.</param>
    public LampDenoisePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight)
    {
        _temporal = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.LampDenoiseTemporal,
            TemporalBindings,
            TemporalParamSize,
            framesInFlight);

        _spatial = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.LampDenoiseSpatial,
            SpatialBindings,
            SpatialParamSize,
            framesInFlight);
    }

    /// <summary>Records the temporal accumulation for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="rawView">The trace's one-ray-per-pixel answer.</param>
    /// <param name="depthView">The scene depth, at full resolution.</param>
    /// <param name="historyView">The previous frame's accumulation, to reproject onto.</param>
    /// <param name="outputView">This frame's accumulation.</param>
    /// <param name="width">The half-resolution width.</param>
    /// <param name="height">The half-resolution height.</param>
    /// <param name="viewToWorld">This frame's view-space to camera-relative-world rotation.</param>
    /// <param name="previousViewToWorld">The previous frame's, transposed here to go the other way.</param>
    /// <param name="cameraDelta">This frame's camera position less the previous frame's.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="previousUnprojection">The same for the previous frame.</param>
    /// <param name="hasHistory">Whether the history slot holds the previous frame at this size.</param>
    /// <param name="accumulate">Whether to blend onto that history at all; off is for measurement.</param>
    /// <param name="jitterX">This frame's horizontal jitter, in half-resolution pixels.</param>
    /// <param name="jitterY">This frame's vertical jitter, in half-resolution pixels.</param>
    /// <param name="previousJitterX">The previous frame's, in the same units.</param>
    /// <param name="previousJitterY">The previous frame's, in the same units.</param>
    public void RecordTemporal(
        CommandBuffer cmd,
        int frame,
        ImageView rawView,
        ImageView depthView,
        ImageView historyView,
        ImageView outputView,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in Matrix3f previousViewToWorld,
        in Vector3f cameraDelta,
        in DepthUnprojection unprojection,
        in DepthUnprojection previousUnprojection,
        bool hasHistory,
        float jitterX,
        float jitterY,
        float previousJitterX,
        float previousJitterY,
        bool accumulate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> data = stackalloc float[TemporalParamSize / sizeof(float)];
        LampShadowParameters.WriteTemporal(
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
            hasHistory,
            accumulate);

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(rawView),
            new ComputeResource(depthView),
            new ComputeResource(historyView),
            new ComputeResource(outputView),
        ];

        _temporal.Record(cmd, frame, resources, data, (uint)((width + 7) / 8), (uint)((height + 7) / 8));
    }

    /// <summary>Records the spatial filter for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="accumulatedView">What the temporal half just wrote.</param>
    /// <param name="depthView">The scene depth, at full resolution.</param>
    /// <param name="normalView">The world normals, at full resolution.</param>
    /// <param name="outputView">The buffer the shading reads.</param>
    /// <param name="width">The half-resolution width.</param>
    /// <param name="height">The half-resolution height.</param>
    /// <param name="viewToWorld">The view-space to camera-relative-world rotation.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="jitterX">This frame's horizontal jitter, in half-resolution pixels.</param>
    /// <param name="jitterY">This frame's vertical jitter, in half-resolution pixels.</param>
    /// <param name="frameIndex">Which frame this is, so the disk's rotation moves between them.</param>
    /// <param name="filter">Whether to filter at all; off is for measurement.</param>
    public void RecordSpatial(
        CommandBuffer cmd,
        int frame,
        ImageView accumulatedView,
        ImageView depthView,
        ImageView normalView,
        ImageView outputView,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in DepthUnprojection unprojection,
        float jitterX,
        float jitterY,
        int frameIndex,
        bool filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> data = stackalloc float[SpatialParamSize / sizeof(float)];
        LampShadowParameters.WriteSpatial(data, width, height, viewToWorld, unprojection, jitterX, jitterY, frameIndex, filter);

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(accumulatedView),
            new ComputeResource(depthView),
            new ComputeResource(normalView),
            new ComputeResource(outputView),
        ];

        _spatial.Record(cmd, frame, resources, data, (uint)((width + 7) / 8), (uint)((height + 7) / 8));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _temporal.Dispose();
        _spatial.Dispose();
    }
}
