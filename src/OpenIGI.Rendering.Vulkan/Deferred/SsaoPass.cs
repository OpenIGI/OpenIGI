using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// A compute pass that estimates ambient occlusion from the scene depth: for each pixel it reconstructs
/// the view position and a face normal, samples a hemisphere of nearby points, and counts how many are
/// hidden behind the geometry — the fraction that are is how occluded the pixel is.
/// </summary>
/// <remarks>
/// <para>
/// It runs <em>after</em> the deferred pass, reading the depth that pass stored and writing an occlusion
/// image the <em>next</em> frame's lighting reads. That one-frame lag is what a single-render-pass, on-tile
/// deferred renderer trades for never spilling the G-buffer to memory: the depth cannot be sampled at
/// neighbouring pixels inside the pass that writes it, so the occlusion is gathered once the pass has
/// closed and consumed the frame after. The occlusion image belongs to the scene target, one per frame in
/// flight, so a read never races a write.
/// </para>
/// <para>
/// The estimate is stochastic and TEMPORAL: the sample directions change every frame, and the result is
/// blended onto the previous frame's occlusion, reprojected by the camera's own motion and validated
/// against the previous depth. A fixed sixteen-sample pattern reads as a static film of grain glued to
/// the screen — it cannot be averaged away by any later pass, because it never changes; rotating the
/// pattern per frame turns the same sixteen samples into an estimator whose error integrates out over
/// the blend's window. The reprojection is judged by the distance ALONG THE SURFACE NORMAL, not by a
/// point distance: on a surface seen at a grazing angle the previous frame's reconstruction is far away
/// laterally while still being the same surface, and a point test there rejects everything the moment
/// the camera moves — the same failure the screen-probe gather had.
/// </para>
/// <para>
/// The image is HALF the frame's resolution. The radius in use is room-scale, so the occlusion is a
/// low-frequency field; computing it per pixel bought nothing but cost, and the reference renderer
/// gathers its occlusion at half resolution too. The lighting's bilinear read upsamples it.
/// </para>
/// </remarks>
internal sealed class SsaoPass : IDisposable
{
    // Two 4x4 matrices and six 4-vectors.
    private const int ParamSize = (2 * 64) + (6 * 16);

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,          // the scene depth
        ComputeBindingKind.StorageImage,          // the occlusion written out
        ComputeBindingKind.SampledStorageImage,   // the previous frame's occlusion — lives in General
        ComputeBindingKind.SampledImage,          // the previous frame's depth
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SsaoPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <exception cref="GraphicsDeviceException">The pass could not be created.</exception>
    public SsaoPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight)
    {
        _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.Ssao,
            Bindings,
            ParamSize,
            framesInFlight);
    }

    /// <summary>Records the occlusion dispatch for one frame into a command buffer.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index, selecting the set and parameter buffer.</param>
    /// <param name="depthView">The scene depth view to read.</param>
    /// <param name="occlusionView">The occlusion image to write, at half the frame's resolution.</param>
    /// <param name="previousOcclusionView">The previous frame's occlusion, the temporal history.</param>
    /// <param name="previousDepthView">The previous frame's depth, validating the reprojection.</param>
    /// <param name="aoWidth">The occlusion image's width.</param>
    /// <param name="aoHeight">The occlusion image's height.</param>
    /// <param name="width">The frame's render width.</param>
    /// <param name="height">The frame's render height.</param>
    /// <param name="unprojection">The projection scales and planes.</param>
    /// <param name="radius">The sample radius in metres.</param>
    /// <param name="intensity">How strongly occlusion darkens.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="previousViewToWorld">The previous frame's view-to-world rotation.</param>
    /// <param name="cameraDelta">This frame's camera position minus the previous frame's.</param>
    /// <param name="frameIndex">A monotonically rising frame counter, for the per-frame noise.</param>
    /// <param name="hasHistory">Whether the previous occlusion and depth hold a real frame.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView occlusionView,
        ImageView previousOcclusionView,
        ImageView previousDepthView,
        int aoWidth,
        int aoHeight,
        int width,
        int height,
        DepthUnprojection unprojection,
        float radius,
        float intensity,
        Matrix3f viewToWorld,
        Matrix3f previousViewToWorld,
        Vector3f cameraDelta,
        int frameIndex,
        bool hasHistory)
    {
        Matrix3f previousWorldToView = previousViewToWorld.Transposed();

        Span<float> data = stackalloc float[ParamSize / sizeof(float)];
        WriteRotation(data, 0, viewToWorld);
        WriteRotation(data, 16, previousWorldToView);

        data[32] = aoWidth;
        data[33] = aoHeight;
        data[34] = 1f / Math.Max(1, aoWidth);
        data[35] = 1f / Math.Max(1, aoHeight);

        data[36] = width;
        data[37] = height;
        data[38] = 1f / Math.Max(1, width);
        data[39] = 1f / Math.Max(1, height);

        data[40] = unprojection.XScale;
        data[41] = unprojection.YScale;
        data[42] = unprojection.Near;
        data[43] = unprojection.Far;

        data[44] = radius;
        data[45] = intensity;
        data[46] = 0.02f;
        data[47] = 1.5f;

        data[48] = cameraDelta.X;
        data[49] = cameraDelta.Y;
        data[50] = cameraDelta.Z;

        // The noise phase repeats after a prime-ish cycle: long enough that the blend window never sees
        // the same pattern twice, small enough that the hash's sine stays in a well-conditioned range.
        data[51] = frameIndex % 1021;

        data[52] = hasHistory ? 1f : 0f;
        data[53] = 0f;
        data[54] = 0f;
        data[55] = 0f;

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(occlusionView),
            new ComputeResource(previousOcclusionView),
            new ComputeResource(previousDepthView),
        ];

        _pass.Record(cmd, frame, resources, data, (uint)((aoWidth + 7) / 8), (uint)((aoHeight + 7) / 8));
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

    /// <summary>
    /// Writes a three-by-three rotation into the four-by-four a shader reads, column by column.
    /// </summary>
    /// <param name="data">The parameter block.</param>
    /// <param name="offset">Where in the block the matrix starts.</param>
    /// <param name="m">The rotation, stored row-major with vectors as columns.</param>
    private static void WriteRotation(Span<float> data, int offset, in Matrix3f m)
    {
        data[offset + 0] = m.M11; data[offset + 1] = m.M21; data[offset + 2] = m.M31; data[offset + 3] = 0f;
        data[offset + 4] = m.M12; data[offset + 5] = m.M22; data[offset + 6] = m.M32; data[offset + 7] = 0f;
        data[offset + 8] = m.M13; data[offset + 9] = m.M23; data[offset + 10] = m.M33; data[offset + 11] = 0f;
        data[offset + 12] = 0f; data[offset + 13] = 0f; data[offset + 14] = 0f; data[offset + 15] = 1f;
    }
}
