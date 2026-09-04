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
    // How far a shadow ray looks. Past this the sun is taken as unobstructed, which for a level whose
    // world is a few thousand units across means only the horizon is given the benefit of the doubt.
    private const float RayLength = 4000f;

    // One 4x4 rotation and six 4-vectors.
    private const int ParamSize = 64 + (6 * 16);

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

    private const string Source =
        """
        // 4.60, not 4.50: ray query is defined against the later language, and a 4.50 shader is told the
        // type does not exist rather than that the version is too low.
        #version 460
        #extension GL_EXT_ray_query : require

        // How a hit reaches the geometry it landed on. There is no descriptor for a triangle the ray
        // chose, so the vertices, the indices and the material records are read straight from their
        // device addresses -- which is what buffer_reference is, and what the 64-bit integer type is
        // needed for.
        #extension GL_EXT_buffer_reference2 : require
        #extension GL_EXT_shader_explicit_arithmetic_types_int64 : require

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uDepth;
        layout(set = 0, binding = 1) uniform sampler2D uNormal;
        layout(set = 0, binding = 2) uniform accelerationStructureEXT uScene;
        layout(set = 0, binding = 3, rg16f) uniform writeonly image2D uShadow;  // visibility, occluder distance
        layout(set = 0, binding = 4) uniform sampler2DArray uMasks;             // cut-out shapes, one layer each

        // The pass places its parameters after every resource, so this index moves whenever a binding is
        // added above it. It was 4 while there were four resources and is 5 now; getting it wrong does not
        // fail to compile, it silently overlaps a resource and the pass reads rubbish.
        layout(set = 0, binding = 5) uniform Params
        {
            mat4 uViewToWorld;   // view rotation, camera relative
            vec4 uUnproject;     // xScale, yScale, near, far
            vec4 uResolution;    // width, height, 1/width, 1/height
            vec4 uSunAndCamera;  // xyz world direction towards the sun, w how far to look
            vec4 uCamera;        // xyz camera position, w the sine of the sun's half-angle
            vec4 uJitter;        // frame index, unused
            uvec4 uRecords;      // device address of the material records, low word then high
        } p;

        // The reference's sun is a disc a little over half a degree wide, not a point, and that is the
        // whole of why a shadow softens with distance from what casts it: near the contact almost the
        // whole disc is blocked or almost none of it is, and far away the two answers overlap over
        // several pixels. Sampling the disc is therefore not a quality setting -- a point sun has no
        // penumbra at any sample count.
        //
        // From the reference's UniformSampleConeRobust. The series expansion is not an optimisation:
        // 1 - sqrt(1 - x) loses almost all its significant digits for x this small, and the sun's
        // sin-squared half-angle is about 2e-5.
        vec3 SampleSunCone(vec2 rand, float sinThetaMax2)
        {
            float phi = 2.0 * 3.14159265359 * rand.x;
            float oneMinusCosThetaMax = sinThetaMax2 < 0.01
                ? sinThetaMax2 * (0.5 + (0.125 * sinThetaMax2))
                : 1.0 - sqrt(1.0 - sinThetaMax2);

            float cosTheta = 1.0 - (oneMinusCosThetaMax * rand.y);
            float sinTheta = sqrt(max(1.0 - (cosTheta * cosTheta), 0.0));

            return vec3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
        }

        // The reference's GetTangentBasis: an orthonormal frame around a direction with no branches and
        // no degenerate case, which matters because the sun can point straight down any axis.
        vec3 TangentToWorld(vec3 v, vec3 axis)
        {
            float sign = axis.z >= 0.0 ? 1.0 : -1.0;
            float a = -1.0 / (sign + axis.z);
            float b = axis.x * axis.y * a;

            vec3 tangentX = vec3(1.0 + (sign * a * axis.x * axis.x), sign * b, -sign * axis.x);
            vec3 tangentY = vec3(b, sign + (a * axis.y * axis.y), -axis.y);

            return (v.x * tangentX) + (v.y * tangentY) + (v.z * axis);
        }

        // Decorrelated per pixel and per frame. One ray a frame is far too few to resolve a penumbra on
        // its own -- what resolves it is the temporal pass averaging a different ray every frame, and
        // that only works if consecutive frames disagree. The reference reaches for its path-tracing
        // sequence here; this is the same idea and is the noise our temporal pass was built to eat.
        float InterleavedGradientNoise(vec2 pos, float frame)
        {
            pos += frame * (vec2(47.0, 17.0) * 0.695);
            return fract(52.9829189 * fract(dot(pos, vec2(0.06711056, 0.00583715))));
        }

        // Everything the ray needs about a triangle it has not chosen yet, read by address. Declared as
        // plain word and float arrays rather than as the structures they describe: the alignment a
        // reference block demands is then four bytes, which every one of these buffers already satisfies,
        // and no layout rule has to agree with a C# struct.
        layout(buffer_reference, buffer_reference_align = 4, std430) readonly buffer Words { uint v[]; };
        layout(buffer_reference, buffer_reference_align = 4, std430) readonly buffer Floats { float v[]; };

        // Vertex3D is 48 bytes, and its diffuse UV is the seventh and eighth floats.
        const uint kVertexFloats = 12u;
        const uint kUvFloat = 6u;

        // Eight words: the vertex address, the index address, the first index, and the mask layer.
        const uint kRecordWords = 8u;

        // Matches VulkanOpacityMasks.Resolution.
        const float kMaskResolution = 256.0;

        // One 16-bit index, out of a buffer read as 32-bit words so that no 16-bit storage feature is
        // needed. The buffer's own base is word aligned; only the element is not.
        uint IndexAt(Words indices, uint at)
        {
            uint word = indices.v[at >> 1u];
            return ((at & 1u) == 0u) ? (word & 0xffffu) : (word >> 16u);
        }

        // Whether the triangle the ray is standing on is actually there at the point it met it.
        //
        // This is the whole reason cut-out surfaces can be traced at all. Traversal only asks about
        // geometry the structure was built as non-opaque, so a solid triangle never reaches here and pays
        // nothing; a wire fence's quad does, and the answer is whether the texel the ray passed through
        // was one of the wires or one of the holes. The reference does the same thing through its
        // any-hit shader with the material's real texture. Ours reads a mask reduced from that texture at
        // load, which is a resolution the shadow cannot see the difference from and a great deal less
        // machinery -- see VulkanOpacityMasks.
        bool CandidateIsSolid(rayQueryEXT query)
        {
            // A structure with no records is one built before this existed, or one whose records ran out.
            // Taking the hit is the conservative answer: the surface occludes as it did before.
            if ((p.uRecords.x | p.uRecords.y) == 0u)
            {
                return true;
            }

            // The instance carries where its structure's records start; the geometry index says which of
            // them this run is. A mesh is not a material, so both halves are needed.
            uint record = uint(rayQueryGetIntersectionInstanceCustomIndexEXT(query, false))
                + uint(rayQueryGetIntersectionGeometryIndexEXT(query, false));

            Words records = Words(packUint2x32(uvec2(p.uRecords.x, p.uRecords.y)));
            uint at = record * kRecordWords;

            int layer = int(records.v[at + 5u]);
            if (layer < 0)
            {
                return true;
            }

            Floats vertices = Floats(packUint2x32(uvec2(records.v[at + 0u], records.v[at + 1u])));
            Words indices = Words(packUint2x32(uvec2(records.v[at + 2u], records.v[at + 3u])));

            // The primitive index counts from the geometry's own first index, which is what the build was
            // given as its primitive offset -- so the first index is added back here.
            uint first = records.v[at + 4u]
                + (uint(rayQueryGetIntersectionPrimitiveIndexEXT(query, false)) * 3u);

            uint i0 = IndexAt(indices, first + 0u);
            uint i1 = IndexAt(indices, first + 1u);
            uint i2 = IndexAt(indices, first + 2u);

            vec2 uv0 = vec2(vertices.v[(i0 * kVertexFloats) + kUvFloat], vertices.v[(i0 * kVertexFloats) + kUvFloat + 1u]);
            vec2 uv1 = vec2(vertices.v[(i1 * kVertexFloats) + kUvFloat], vertices.v[(i1 * kVertexFloats) + kUvFloat + 1u]);
            vec2 uv2 = vec2(vertices.v[(i2 * kVertexFloats) + kUvFloat], vertices.v[(i2 * kVertexFloats) + kUvFloat + 1u]);

            vec2 bary = rayQueryGetIntersectionBarycentricsEXT(query, false);
            vec2 uv = (uv0 * (1.0 - bary.x - bary.y)) + (uv1 * bary.x) + (uv2 * bary.y);

            // Wrapped rather than clamped, because these textures tile -- a fence's UV runs well past one
            // along its length. A cut-out texture sampled with clamping in the raster path would disagree
            // at its edge, which no surface in this level does.
            ivec2 texel = ivec2(fract(uv) * kMaskResolution);
            return texelFetch(uMasks, ivec3(texel, layer), 0).r > 0.5;
        }

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
                imageStore(uShadow, px, vec4(1.0, 0.0, 0.0, 0.0));
                return;
            }

            vec3 normal = normalize((texture(uNormal, uv).xyz * 2.0) - 1.0);
            vec3 sunDirection = p.uSunAndCamera.xyz;

            // Facing away from the sun. The surface shadows itself, and no ray is needed to know it.
            if (dot(normal, sunDirection) <= 0.0)
            {
                imageStore(uShadow, px, vec4(0.0, 0.0, 0.0, 0.0));
                return;
            }

            vec3 viewPos = ViewPositionAt(uv, depth);
            vec3 worldPos = (p.uViewToWorld * vec4(viewPos, 1.0)).xyz + p.uCamera.xyz;

            // Started off the surface along its own normal, so a face does not shadow itself through
            // rounding at the origin. Scaled with distance because that rounding grows with the magnitude
            // of the position it is happening to.
            float offset = 0.02 + (0.0005 * viewPos.z);

            // One ray, aimed somewhere on the sun's disc rather than at its centre. The reference traces
            // one sample per pixel here too, which is why the jitter has to move every frame -- a single
            // ray cannot resolve a penumbra on its own, and what resolves it is the temporal pass
            // averaging a different ray every frame.
            //
            // KNOWN DEVIATION: the reference ALSO runs a spatial denoiser over this mask before its
            // temporal pass sees it -- r.Shadow.Denoiser is 2, and a directional light whose source angle
            // is above zero asks for one -- and we do not. Ours leaves all of the averaging to the
            // temporal pass. Measured as acceptable on this scene rather than assumed: still-camera
            // stability IMPROVES against the hard-edged trace this replaced, 0.83% to 0.49%
            // consecutive-frame RMSE, and the penumbra resolves smooth rather than speckled. It is a
            // deviation all the same, and a scene with more shadow detail may want the denoiser ported.
            vec2 rand = vec2(
                InterleavedGradientNoise(vec2(px), p.uJitter.x),
                InterleavedGradientNoise(vec2(px) + 97.0, p.uJitter.x));

            float sinThetaMax = p.uCamera.w;
            vec3 traceDirection = sinThetaMax > 0.0
                ? normalize(TangentToWorld(SampleSunCone(rand, sinThetaMax * sinThetaMax), sunDirection))
                : sunDirection;

            rayQueryEXT query;
            rayQueryInitializeEXT(
                query,
                uScene,

                // Nothing needs the nearest hit -- the first thing in the way is enough to know the point
                // is in shadow -- but the ray is NOT forced opaque. Forcing it would take every cut-out
                // triangle at face value, which is the fence casting the shadow of a board; without it,
                // traversal asks about those triangles and the loop below answers from the alpha. Solid
                // geometry is still committed by traversal itself and never reaches the loop.
                gl_RayFlagsTerminateOnFirstHitEXT,
                0xFF,
                worldPos + (normal * offset),
                0.01,
                traceDirection,
                p.uSunAndCamera.w);

            while (rayQueryProceedEXT(query))
            {
                if (rayQueryGetIntersectionTypeEXT(query, false) == gl_RayQueryCandidateIntersectionTriangleEXT
                    && CandidateIsSolid(query))
                {
                    rayQueryConfirmIntersectionEXT(query);
                }
            }

            bool hit = rayQueryGetIntersectionTypeEXT(query, true) != gl_RayQueryCommittedIntersectionNoneEXT;

            // How far the ray got before something stopped it. This is what sets the width of the
            // penumbra: the sun is a disc, so a caster twice as far from the surface casts an edge twice
            // as soft, and the filter downstream cannot know how far to spread without it.
            float hitDistance = hit ? rayQueryGetIntersectionTEXT(query, true) : 0.0;
            imageStore(uShadow, px, vec4(hit ? 0.0 : 1.0, hitDistance, 0.0, 0.0));
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

        // The sine of the sun's half-angle, which is the form the cone sampler wants. Zero collapses the
        // disc to a point and the trace back to the hard-edged one this replaced.
        data[31] = MathF.Sin(0.5f * float.DegreesToRadians(Math.Max(sunAngleDegrees, 0f)));

        data[32] = frameIndex;
        data[33] = 0f;
        data[34] = 0f;
        data[35] = 0f;

        // The record buffer's address, as the two words the shader reassembles it from. Written as raw
        // bits through a float span because that is what the parameter block is: the block is declared
        // uvec4 on the other side, so the bits are what matter and not the type they travelled in.
        data[36] = BitConverter.UInt32BitsToSingle((uint)recordAddress);
        data[37] = BitConverter.UInt32BitsToSingle((uint)(recordAddress >> 32));
        data[38] = 0f;
        data[39] = 0f;

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
