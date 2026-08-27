using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// Averages the traced sun shadow over time — the temporal half of the reference's shadow denoiser.
/// </summary>
/// <remarks>
/// See <see cref="ShadowAccumulateShaderSource"/> for why the mask has to be averaged here rather than
/// left to the temporal filter downstream. It runs immediately after the spatial reconstruction and
/// before anything turns the mask into light.
/// </remarks>
internal sealed class ShadowAccumulatePass : IDisposable
{
    /// <summary>How far apart two view depths may be before they are called different surfaces.</summary>
    /// <remarks>
    /// A fraction of the depth rather than a distance in metres, for the same reason every other
    /// reprojection test here is relative: an absolute tolerance that tells two surfaces apart nearby
    /// tells nothing apart in the distance, and the distance is where this pass earns its keep.
    /// </remarks>
    public const float DepthTolerance = 0.02f;

    // Two 4x4 matrices and five 4-vectors.
    private const int ParamSize = 208;

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
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public ShadowAccumulatePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ShaderCompiler compiler,
        int framesInFlight)
        => _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            compiler,
            "shadow.accumulate.comp",
            ShadowAccumulateShaderSource.Source,
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
        bool hasHistory)
    {
        Span<float> data = stackalloc float[ParamSize / sizeof(float)];

        // The same pair the velocity pass is handed, packed by the same routine: a rotation's transpose
        // is its inverse, so this is the previous frame's world-to-view. The two passes have to agree
        // exactly about where a pixel was, or the mask and the colour would reproject to different places
        // and the shadow would slide across the surface it belongs to.
        Matrix3f previousWorldToView = previousViewToWorld.Transposed();
        WriteRotation(data, 0, viewToWorld);
        WriteRotation(data, 16, previousWorldToView);

        data[32] = unprojection.XScale;
        data[33] = unprojection.YScale;
        data[34] = unprojection.Near;
        data[35] = unprojection.Far;

        data[36] = width;
        data[37] = height;
        data[38] = 1f / Math.Max(1, width);
        data[39] = 1f / Math.Max(1, height);

        data[40] = cameraDelta.X;
        data[41] = cameraDelta.Y;
        data[42] = cameraDelta.Z;
        data[43] = 0f;

        data[44] = previousUnprojection.XScale;
        data[45] = previousUnprojection.YScale;
        data[46] = previousUnprojection.Near;
        data[47] = previousUnprojection.Far;

        data[48] = Math.Max(1f, frames);
        data[49] = hasHistory ? 1f : 0f;
        data[50] = DepthTolerance;
        data[51] = 0f;

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

    private static void WriteRotation(Span<float> data, int offset, in Matrix3f m)
    {
        data[offset + 0] = m.M11; data[offset + 1] = m.M21; data[offset + 2] = m.M31; data[offset + 3] = 0f;
        data[offset + 4] = m.M12; data[offset + 5] = m.M22; data[offset + 6] = m.M32; data[offset + 7] = 0f;
        data[offset + 8] = m.M13; data[offset + 9] = m.M23; data[offset + 10] = m.M33; data[offset + 11] = 0f;
        data[offset + 12] = 0f; data[offset + 13] = 0f; data[offset + 14] = 0f; data[offset + 15] = 1f;
    }
}
