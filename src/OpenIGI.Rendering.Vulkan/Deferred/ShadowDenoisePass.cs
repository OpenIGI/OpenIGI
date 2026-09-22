using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// Spreads the one shadow ray each pixel traced across the width its penumbra should actually have —
/// the reference's screen-space shadow denoiser, reconstruction stage.
/// </summary>
/// <remarks>
/// <para>
/// The trace before this sends a single ray per pixel at a random point on the sun's disc, which is what
/// the reference does too. One sample cannot resolve a soft edge: it answers "lit" or "not lit", so a
/// penumbra that should be a smooth ramp arrives as noise that merely averages to the right thing.
/// Something has to do that averaging. The reference does it here, spatially, before its temporal pass
/// ever sees the mask — and the point of this pass is that we had been leaving all of it to the temporal
/// one, which works on a still camera and has nothing to fall back on when the view moves.
/// </para>
/// <para>
/// <b>How wide to spread is not a guess.</b> The sun is a disc of known angular size, so the penumbra a
/// caster throws is exactly <c>tan(half-angle) × distance from the surface to that caster</c> — which is
/// why the trace now returns how far its ray travelled before it hit something. A contact shadow gets a
/// filter a fraction of a pixel wide and stays hard; one thrown from thirty metres up gets a broad one.
/// A fixed blur radius cannot express that, which is why faked soft shadows usually resort to a distance
/// fade and look wrong at both ends.
/// </para>
/// <para>
/// <b>Substitution.</b> The reference runs this through a general signal framework — compressed scene
/// metadata, configurable kernels, four sample sets of fifty-six — that exists so one body of code can
/// serve reflections, ambient occlusion, diffuse indirect and shadows. Ported here is the shadow path
/// through it: its penumbra formula, the first eight offsets of its Stackowiak set verbatim, and the
/// quadrant track offset that decorrelates neighbouring pixels. The sample weight is the one real
/// deviation and is documented at the site.
/// </para>
/// </remarks>
internal sealed class ShadowDenoisePass : IDisposable
{
    // Three 4-vectors.
    private const int ParamSize = 48;

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledStorageImage,  // the traced mask: visibility and occluder distance
        ComputeBindingKind.SampledImage,         // the scene depth, to keep the filter on one surface
        // SampledStorageImage, not SampledImage: the accumulation history is a storage image and lives in
        // General, so a descriptor that promised ShaderReadOnlyOptimal is a layout mismatch the validation
        // layers reject. The temporal ambient occlusion hit the same wall.
        ComputeBindingKind.SampledStorageImage,  // last frame's accumulation, for how converged it is
        ComputeBindingKind.StorageImage,         // the reconstructed visibility the lighting reads
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShadowDenoisePass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public ShadowDenoisePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight)
        => _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.ShadowDenoise,
            Bindings,
            ParamSize,
            framesInFlight);

    /// <summary>Records the reconstruction for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="rawView">The traced mask: visibility and occluder distance.</param>
    /// <param name="depthView">The scene depth, to keep the filter on one surface.</param>
    /// <param name="historyView">Last frame's accumulation; only its frame count is read.</param>
    /// <param name="shadowView">The reconstructed visibility the lighting reads.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <param name="sunAngleDegrees">The sun's angular diameter, which is what sets a penumbra's width.</param>
    /// <param name="accumulationCeiling">How many frames the temporal pass averages at most.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView rawView,
        ImageView depthView,
        ImageView historyView,
        ImageView shadowView,
        int width,
        int height,
        DepthUnprojection unprojection,
        float sunAngleDegrees,
        float accumulationCeiling)
    {
        Span<float> data = stackalloc float[ParamSize / sizeof(float)];

        data[0] = width;
        data[1] = height;
        data[2] = 1f / Math.Max(1, width);
        data[3] = 1f / Math.Max(1, height);

        data[4] = unprojection.XScale;
        data[5] = unprojection.YScale;
        data[6] = unprojection.Near;
        data[7] = unprojection.Far;

        // The reference's penumbra formula for a directional light, with its trigonometry folded onto the
        // host: sourceRadius / sqrt(1 - sourceRadius^2) is the tangent of the half-angle whose sine is
        // sourceRadius, and that tangent times the distance to the occluder is the penumbra's radius.
        data[8] = MathF.Tan(0.5f * float.DegreesToRadians(Math.Max(sunAngleDegrees, 0f)));

        // One over the ceiling the temporal pass counts to, so the shader reads a stored frame count as a
        // fraction of converged without needing to know what the ceiling is.
        data[9] = 1f / MathF.Max(accumulationCeiling, 1f);
        data[10] = 0f;
        data[11] = 0f;

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(rawView),
            new ComputeResource(depthView),
            new ComputeResource(historyView),
            new ComputeResource(shadowView),
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
