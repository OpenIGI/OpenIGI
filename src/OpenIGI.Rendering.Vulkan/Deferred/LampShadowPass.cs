using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// How much of each lamp reaches each pixel, traced once per pixel rather than once per lamp.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a pass and not a loop in the shading.</b> Tracing a shadow ray per lamp per pixel
/// costs what it costs: measured on mission 1's office at 1440p, 1.07 ms for every ray-per-pixel over a
/// 3.3 ms base. Two lamps at two taps is 7.6 ms of a frame whose whole shading budget is 0.8, and eight
/// lamps is 20.5 — and the levels have sixty-four. Capping how many lamps may be shadowed does not
/// rescue it either, because the lamps past the cap are then lit UNSHADOWED, which is light straight
/// through a wall. Cost that scales with the light count and correctness that scales with the light
/// count are the same problem, and neither is fixable by choosing a better cap.
/// </para>
/// <para>
/// <b>What the reference does instead.</b> It never shades a light per ray. It picks ONE light
/// per pixel by weighted reservoir sampling, traces one ray at it, and spends the rest of its budget
/// denoising the answer — so the work is one ray whether the level has four lights or four thousand,
/// and every light is shadowed, so nothing leaks. This is that, at the scale this game needs: the
/// reservoir, one ray, and the sun's own accumulation pass reused to average the result over time.
/// </para>
/// <para>
/// <b>At half resolution</b>, which is where the performance actually comes from and which is what the
/// reference does too (its downsampled depth and normal are the frame's extent halved). A quarter of
/// the rays puts the trace inside its budget; a shadow is low-frequency enough to carry the loss, and
/// what would not carry it — the contact darkening right under an object — is the ambient occlusion's
/// job and is already resolved at full resolution.
/// </para>
/// </remarks>
internal sealed class LampShadowPass : IDisposable
{
    /// <summary>The parameter block: the camera, the frame, and the lamps themselves.</summary>
    /// <remarks>
    /// The lamps ride in the parameters rather than in a binding of their own because a compute pass
    /// here carries images and one block, and duplicating the lighting pass's uniform buffer as a second
    /// bindable resource would be a new kind of binding for one caller. The layout matches the shading
    /// pass's block exactly, so the two cannot disagree about which lamp is which.
    /// </remarks>
    /// <remarks>
    /// A <c>mat4</c> and three <c>vec4</c>s of camera is 112 bytes, and the lamp block that follows is
    /// the shading pass's own, byte for byte. The two numbers have to be exactly right: the arrays are
    /// read at a fixed offset into this buffer, so a header even one <c>vec4</c> longer than the shader
    /// believes silently shifts every lamp and the pass reads a count of nought.
    /// </remarks>
    private const int ParamSize = LampShadowParameters.TraceFloats * sizeof(float);

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,          // the scene depth, full resolution
        ComputeBindingKind.SampledImage,          // the world normal, to offset off the surface
        ComputeBindingKind.AccelerationStructure, // the world the ray is traced against
        ComputeBindingKind.StorageImage,          // visibility and the reservoir's weight, half resolution
        ComputeBindingKind.SampledImage,          // the cut-out masks, one layer per cut-out texture
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="LampShadowPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing its parameter buffers.</param>
    /// <param name="framesInFlight">How many frames the descriptor sets are cycled over.</param>
    public LampShadowPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight)
    {
        _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.LampShadow,
            Bindings,
            ParamSize,
            framesInFlight);
    }

    /// <summary>Records the lamp visibility trace for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="depthView">The scene depth the geometry pass stored, at full resolution.</param>
    /// <param name="normalView">The world normals, for the ray's origin offset and the falloff.</param>
    /// <param name="scene">The acceleration structure to trace against.</param>
    /// <param name="visibilityView">The half-resolution target the answer is written to.</param>
    /// <param name="width">The half-resolution width.</param>
    /// <param name="height">The half-resolution height.</param>
    /// <param name="viewToWorld">The view-space to camera-relative-world rotation.</param>
    /// <param name="camera">The camera's world position, which the structure's space is measured from.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="frameIndex">Which frame this is, so the reservoir and the ray move between them.</param>
    /// <param name="lamps">The lamps, packed exactly as the shading pass packs them.</param>
    /// <param name="maskView">The cut-out masks the rays test cut-out surfaces against.</param>
    /// <param name="cutoutRecords">Where the hit records naming each surface's mask live; nought for none.</param>
    public unsafe void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView normalView,
        AccelerationStructureKHR scene,
        ImageView visibilityView,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in Vector3f camera,
        in DepthUnprojection unprojection,
        int frameIndex,
        ReadOnlySpan<float> lamps,
        ImageView maskView,
        ulong cutoutRecords)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<float> data = stackalloc float[ParamSize / sizeof(float)];
        LampShadowParameters.WriteTrace(
            data, width, height, viewToWorld, camera, unprojection, frameIndex, lamps, cutoutRecords);

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(normalView),
            ComputeResource.Scene(scene),
            new ComputeResource(visibilityView),
            new ComputeResource(maskView),
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
