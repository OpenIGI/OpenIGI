using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
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
/// Two things it does not yet see. The terrain is drawn from a mesh the world streams into, whose
/// contents change under the structure that would describe them, so it is not offered to the scene — the
/// cascaded map still covers it, and the shading takes the tighter of the two answers. And foliage is
/// traced as solid: a canopy that the rasteriser cuts away with an opacity mask stops a ray as if it were
/// a board, which over a forest and a low sun costs more light than it should. Both want the same next
/// piece of work — the geometry behind an instance reachable from the shader — which is also what the
/// indirect gathers need.
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
    // How far a shadow ray looks. Past this the sun is taken as unobstructed, which for a level whose
    // world is a few thousand units across means only the horizon is given the benefit of the doubt.
    private const float RayLength = 4000f;

    // One 4x4 rotation and four 4-vectors.
    private const int ParamSize = 64 + (4 * 16);

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,          // the scene depth
        ComputeBindingKind.SampledImage,          // the world normal, to offset off the surface
        ComputeBindingKind.AccelerationStructure, // the world to trace against
        ComputeBindingKind.StorageImage,          // how much sun reaches each pixel
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    private const string Source =
        """
        // 4.60, not 4.50: ray query is defined against the later language, and a 4.50 shader is told the
        // type does not exist rather than that the version is too low.
        #version 460
        #extension GL_EXT_ray_query : require

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uDepth;
        layout(set = 0, binding = 1) uniform sampler2D uNormal;
        layout(set = 0, binding = 2) uniform accelerationStructureEXT uScene;
        layout(set = 0, binding = 3, r8) uniform writeonly image2D uShadow;
        layout(set = 0, binding = 4) uniform Params
        {
            mat4 uViewToWorld;   // view rotation, camera relative
            vec4 uUnproject;     // xScale, yScale, near, far
            vec4 uResolution;    // width, height, 1/width, 1/height
            vec4 uSunAndCamera;  // xyz world direction towards the sun, w how far to look
            vec4 uCamera;        // xyz camera position, w unused
        } p;

        // The view-space position a depth sample came from. Reverse-Z, so the stored depth is mapped back
        // to the normalised z the closed-form inverse expects; the vertical is negated because the
        // G-buffer was rasterised through the vertex stage's Y flip.
        vec3 ViewPositionAt(vec2 uv, float depth)
        {
            float ndcZ = 1.0 - 2.0 * depth;
            float near = p.uUnproject.z;
            float far = p.uUnproject.w;
            float viewZ = (2.0 * near * far) / ((far + near) - ndcZ * (far - near));
            vec2 ndc = uv * 2.0 - 1.0;
            return vec3(ndc.x * viewZ / p.uUnproject.x, -ndc.y * viewZ / p.uUnproject.y, viewZ);
        }

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            ivec2 size = ivec2(p.uResolution.xy);
            if (px.x >= size.x || px.y >= size.y)
            {
                return;
            }

            vec2 uv = (vec2(px) + 0.5) * p.uResolution.zw;
            float depth = texture(uDepth, uv).r;

            // Sky. Nothing there to be shadowed, and the shading pass never asks.
            if (depth <= 0.0)
            {
                imageStore(uShadow, px, vec4(1.0));
                return;
            }

            vec3 normal = normalize((texture(uNormal, uv).xyz * 2.0) - 1.0);
            vec3 sunDirection = p.uSunAndCamera.xyz;

            // Facing away from the sun. The surface shadows itself, and no ray is needed to know it.
            if (dot(normal, sunDirection) <= 0.0)
            {
                imageStore(uShadow, px, vec4(0.0));
                return;
            }

            vec3 viewPos = ViewPositionAt(uv, depth);
            vec3 worldPos = (p.uViewToWorld * vec4(viewPos, 1.0)).xyz + p.uCamera.xyz;

            // Started off the surface along its own normal, so a face does not shadow itself through
            // rounding at the origin. Scaled with distance because that rounding grows with the magnitude
            // of the position it is happening to.
            float offset = 0.02 + (0.0005 * viewPos.z);

            rayQueryEXT query;
            rayQueryInitializeEXT(
                query,
                uScene,

                // Nothing here has an any-hit shader and nothing needs the nearest hit: the first thing in
                // the way is enough to know the point is in shadow.
                gl_RayFlagsOpaqueEXT | gl_RayFlagsTerminateOnFirstHitEXT,
                0xFF,
                worldPos + (normal * offset),
                0.01,
                sunDirection,
                p.uSunAndCamera.w);

            while (rayQueryProceedEXT(query))
            {
            }

            bool hit = rayQueryGetIntersectionTypeEXT(query, true) != gl_RayQueryCommittedIntersectionNoneEXT;
            imageStore(uShadow, px, vec4(hit ? 0.0 : 1.0));
        }
        """;

    /// <summary>Initialises a new instance of the <see cref="RayTracedShadowPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public RayTracedShadowPass(
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
            "sunshadow.comp",
            Source,
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
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView normalView,
        AccelerationStructureKHR scene,
        ImageView shadowView,
        int width,
        int height,
        Matrix3f viewToWorld,
        Vector3f cameraPosition,
        Vector3f sunDirection,
        DepthUnprojection unprojection)
    {
        Span<float> data = stackalloc float[ParamSize / sizeof(float)];

        data[0] = viewToWorld.M11; data[1] = viewToWorld.M21; data[2] = viewToWorld.M31; data[3] = 0f;
        data[4] = viewToWorld.M12; data[5] = viewToWorld.M22; data[6] = viewToWorld.M32; data[7] = 0f;
        data[8] = viewToWorld.M13; data[9] = viewToWorld.M23; data[10] = viewToWorld.M33; data[11] = 0f;
        data[12] = 0f; data[13] = 0f; data[14] = 0f; data[15] = 1f;

        data[16] = unprojection.XScale;
        data[17] = unprojection.YScale;
        data[18] = unprojection.Near;
        data[19] = unprojection.Far;

        data[20] = width;
        data[21] = height;
        data[22] = 1f / Math.Max(1, width);
        data[23] = 1f / Math.Max(1, height);

        data[24] = sunDirection.X;
        data[25] = sunDirection.Y;
        data[26] = sunDirection.Z;
        data[27] = RayLength;

        data[28] = cameraPosition.X;
        data[29] = cameraPosition.Y;
        data[30] = cameraPosition.Z;
        data[31] = 0f;

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(normalView),
            ComputeResource.Scene(scene),
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
