using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// Works out how much of the sun reaches every pixel by asking the geometry, rather than by comparing
/// depths in a map the light rendered.
/// </summary>
/// <remarks>
/// <para>
/// A shadow map answers the question indirectly: it renders the scene from the light, then a surface
/// compares its own depth against what the light recorded. Everything awkward about shadow maps follows
/// from that — the answer is only as precise as the map's resolution, it needs a bias that trades acne
/// against shadows detaching from their casters, and covering any distance means splitting the map into
/// cascades that have to be blended between.
/// </para>
/// <para>
/// A ray answers it directly. Start at the surface, point at the sun, and see whether anything is in the
/// way. There is no resolution to run out of, no bias beyond nudging off the surface, and a thin object
/// casts a shadow exactly as wide as the object.
/// </para>
/// <para>
/// The terrain reaches the scene by a route of its own. The pages it is rasterised from hold every
/// chunk ever streamed into them at every level of detail, at positions the vertex stage then slides,
/// so they are not traced; the terrain renderer offers the chunks it drew, morphed as drawn, as a
/// one-frame caster instead (<c>VulkanWorldScene.ObserveCaster</c>). Foliage is traced through its
/// opacity mask, so a canopy lets the sun through between its leaves.
/// </para>
/// <para>
/// It runs between the pass that fills the G-buffer and the pass that shades it. Tracing in the shading
/// pass itself would work, but tracing here costs one ray a pixel however many surfaces were drawn over
/// it, and it puts the answer in a buffer that the denoisers and the softening this will grow can read.
/// The mask is written and read within the same frame, so nothing lags.
/// </para>
/// </remarks>
internal sealed class RayTracedShadowPass : IDisposable
{
    private const int ParamSize = SunShadowParameters.TraceFloats * sizeof(float);

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,          // the scene depth
        ComputeBindingKind.SampledImage,          // the world normal, to offset off the surface
        ComputeBindingKind.AccelerationStructure, // the world to trace against
        ComputeBindingKind.StorageImage,          // how much sun reaches each pixel
        ComputeBindingKind.SampledImage,          // the cut-out masks, one layer per cut-out texture
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    /// <summary>The pass's GLSL.</summary>
    /// <remarks>
    /// Internal rather than private so a test can pin the one thing this shares with the reflection's hit
    /// shading: both reach into the SAME per-geometry material record by a hand-written word offset, and
    /// the two offsets landing on each other is a fault neither shader can detect.
    /// </remarks>
    /// <summary>Initialises a new instance of the <see cref="RayTracedShadowPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public RayTracedShadowPass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        int framesInFlight)
    {
        _pass = new VulkanComputePass(
            vk,
            device,
            allocator,
            ShaderModuleId.SunShadow,
            Bindings,
            ParamSize,
            framesInFlight);
    }

    /// <summary>Records the shadow trace for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="depthView">The scene depth the geometry pass stored.</param>
    /// <param name="normalView">The world-space normals the geometry pass stored.</param>
    /// <param name="scene">The world to trace against.</param>
    /// <param name="shadowView">The mask to write.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="cameraPosition">Where the camera is, in absolute world space.</param>
    /// <param name="sunDirection">The world direction towards the sun.</param>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <param name="sunAngleDegrees">How wide the sun's disc is, which is what gives a shadow its penumbra.</param>
    /// <param name="frameIndex">A number that changes every frame, so the one ray lands somewhere new.</param>
    /// <param name="maskView">The cut-out mask array a candidate hit tests its alpha against.</param>
    /// <param name="recordAddress">
    /// Where the material records live, or zero when the world scene has none — in which case every hit is
    /// taken at face value, which is what the pass did before it could test one.
    /// </param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView normalView,
        AccelerationStructureKHR scene,
        ImageView shadowView,
        ImageView maskView,
        ulong recordAddress,
        int width,
        int height,
        Matrix3f viewToWorld,
        Vector3f cameraPosition,
        Vector3f sunDirection,
        DepthUnprojection unprojection,
        float sunAngleDegrees,
        float frameIndex)
    {
        Span<float> data = stackalloc float[ParamSize / sizeof(float)];

        SunShadowParameters.WriteTrace(
            data,
            width,
            height,
            viewToWorld,
            cameraPosition,
            sunDirection,
            unprojection,
            sunAngleDegrees,
            frameIndex,
            recordAddress);

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(normalView),
            ComputeResource.Scene(scene),
            new ComputeResource(shadowView),
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
