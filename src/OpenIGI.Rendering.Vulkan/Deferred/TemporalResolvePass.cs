using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The temporal resolve: accumulates the jittered lit frames into a stable, anti-aliased image, the
/// core of the reference's temporal upscaler ported onto the Vulkan deferred path.
/// </summary>
/// <remarks>
/// One compute dispatch that reads this frame's lit colour, the velocity pass's motion vectors, and the
/// previous frame's accumulated history, and writes the resolved colour (plus its running sample count in
/// alpha) for the tone-map resolve to read. See <see cref="TemporalResolveShaderSource"/> for the maths
/// and its provenance.
/// </remarks>
internal sealed class TemporalResolvePass : IDisposable
{
    private const int ParamSize = 32;

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,          // this frame's lit colour, left shader-readable by its pass
        ComputeBindingKind.SampledStorageImage,   // the motion vectors, which the velocity pass keeps in General
        ComputeBindingKind.SampledImage,          // the previous frame's history
        ComputeBindingKind.StorageImage,          // the resolved colour written out
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TemporalResolvePass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <exception cref="GraphicsDeviceException">The pass could not be created.</exception>
    public TemporalResolvePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ShaderCompiler compiler,
        int framesInFlight)
    {
        _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            compiler,
            "temporal.comp",
            TemporalResolveShaderSource.Compute,
            Bindings,
            ParamSize,
            framesInFlight);
    }

    /// <summary>Records the temporal resolve dispatch for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="currentView">This frame's lit-colour view.</param>
    /// <param name="velocityView">The motion-vector view.</param>
    /// <param name="historyView">The previous frame's history view.</param>
    /// <param name="outputView">The resolved-colour image to write.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="temporalOn">Whether accumulation runs; off makes the pass a passthrough.</param>
    /// <param name="maxSamples">The history's sample-count cap (the reference's default 16).</param>
    /// <param name="sharpening">Reserved for the history unsharp step.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView currentView,
        ImageView velocityView,
        ImageView historyView,
        ImageView outputView,
        int width,
        int height,
        bool temporalOn,
        float maxSamples,
        float sharpening)
    {
        ReadOnlySpan<float> data =
        [
            width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height),
            temporalOn ? 1f : 0f, maxSamples, sharpening, 0f,
        ];

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(currentView),
            new ComputeResource(velocityView),
            new ComputeResource(historyView),
            new ComputeResource(outputView),
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
