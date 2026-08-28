using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
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

    private const string Source =
        """
        #version 450

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uDepth;
        layout(set = 0, binding = 1, r8) uniform writeonly image2D uOcclusion;
        layout(set = 0, binding = 2) uniform sampler2D uPreviousOcclusion;
        layout(set = 0, binding = 3) uniform sampler2D uPreviousDepth;
        layout(set = 0, binding = 4) uniform Params
        {
            mat4 uViewToWorld;       // this frame's view rotation, camera-relative
            mat4 uPrevWorldToView;   // the previous frame's inverse view rotation
            vec4 uResolution;        // AO width, height, 1/width, 1/height
            vec4 uFullResolution;    // frame width, height, 1/width, 1/height
            vec4 uUnproject;         // xScale, yScale, near, far
            vec4 uParams;            // radius (metres), intensity, bias, power
            vec4 uCameraDelta;       // this camera minus the previous, w: frame index for the noise
            vec4 uTemporal;          // x: whether a history exists, yzw unused
        } p;

        // View position from a reverse-Z depth sample, the same reconstruction the lighting uses so the two
        // agree on where a surface is; the vertical is negated for the G-buffer's Y flip.
        vec3 ViewPositionOf(vec2 uv, float d)
        {
            float ndcZ = 1.0 - 2.0 * d;
            float near = p.uUnproject.z;
            float far = p.uUnproject.w;
            float viewZ = (2.0 * near * far) / ((far + near) - ndcZ * (far - near));
            vec2 ndc = uv * 2.0 - 1.0;
            return vec3(ndc.x * viewZ / p.uUnproject.x, -ndc.y * viewZ / p.uUnproject.y, viewZ);
        }

        vec3 ViewPosition(vec2 uv)
        {
            return ViewPositionOf(uv, texture(uDepth, uv).r);
        }

        // Where a view-space point lands on screen — the inverse of the reconstruction above.
        vec2 ViewToUv(vec3 v)
        {
            return vec2(0.5 + (v.x * p.uUnproject.x) / (2.0 * v.z), 0.5 - (v.y * p.uUnproject.y) / (2.0 * v.z));
        }

        float Hash(vec2 v)
        {
            return fract(sin(dot(v, vec2(12.9898, 78.233))) * 43758.5453);
        }

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            if (px.x >= int(p.uResolution.x) || px.y >= int(p.uResolution.y))
            {
                return;
            }

            // Snapped to a full-resolution texel centre, so the half-resolution gather never reads a
            // depth bilinearly mixed across a silhouette — a mixed depth reconstructs a point hanging in
            // the air between two surfaces, and the occlusion of a point that exists nowhere.
            vec2 uv = (vec2(px) * 2.0 + 0.5) * p.uFullResolution.zw;
            float depth = texture(uDepth, uv).r;

            // The sky (reverse-Z far is zero) is not a surface and is never occluded.
            if (depth <= 0.0)
            {
                imageStore(uOcclusion, px, vec4(1.0));
                return;
            }

            vec3 position = ViewPositionOf(uv, depth);

            // A face normal from the neighbouring depths — a compute shader has no screen-space derivatives.
            // Turned to face the camera: the reconstruction's cross product can come out either way, and a
            // back-facing normal would send the whole hemisphere into the geometry.
            vec3 dx = ViewPosition(uv + vec2(p.uFullResolution.z, 0.0)) - position;
            vec3 dy = ViewPosition(uv + vec2(0.0, p.uFullResolution.w)) - position;
            vec3 normal = normalize(cross(dx, dy));
            if (dot(normal, position) > 0.0)
            {
                normal = -normal;
            }

            // A rotation that changes per pixel AND per frame. Per pixel so the fixed directions do not
            // band across the image; per frame so the residual noise is a different pattern each frame and
            // the temporal blend below can integrate it away instead of preserving it for ever.
            float frameIndex = p.uCameraDelta.w;
            float angle = Hash(vec2(px) + vec2(frameIndex * 17.0, frameIndex * 41.0)) * 6.2831853;
            vec3 randomVec = vec3(cos(angle), sin(angle), 0.0);
            vec3 tangent = normalize(randomVec - normal * dot(randomVec, normal));
            vec3 bitangent = cross(normal, tangent);
            mat3 tbn = mat3(tangent, bitangent, normal);

            // The radius is an absolute view-space distance: the hemisphere covers a fixed span of the world,
            // and the perspective divide shrinks it on screen as the surface recedes — the correct
            // screen-space AO behaviour.
            float radius = p.uParams.x;
            float bias = p.uParams.z * radius;
            const int kCount = 16;
            float occlusion = 0.0;

            for (int i = 0; i < kCount; i++)
            {
                // A cosine-weighted point in the hemisphere over the surface, jittered per frame through
                // the rotation above and through the per-frame offsets folded into the sample hashes.
                float r1 = Hash(vec2(float(i) + 0.13 + frameIndex * 0.618, float(px.x * 3 + px.y)));
                float r2 = Hash(vec2(float(i) * 1.7 + 0.5 + frameIndex * 0.382, float(px.y * 3 + px.x)));
                float theta = 6.2831853 * r1;
                float disk = sqrt(r2);
                vec3 hemi = vec3(disk * cos(theta), disk * sin(theta), sqrt(max(1.0 - r2, 0.0)));
                float scale = mix(0.1, 1.0, float(i) / float(kCount));
                vec3 samplePos = position + (tbn * hemi) * radius * scale;

                vec2 sampleUv = ViewToUv(samplePos);
                if (any(lessThan(sampleUv, vec2(0.0))) || any(greaterThan(sampleUv, vec2(1.0))))
                {
                    continue;
                }

                float sceneZ = ViewPosition(sampleUv).z;

                // Occluded when the actual surface at the sample's screen position sits in front of the
                // sample point — closer to the camera, so a smaller view depth. The range check keeps a far
                // surface seen past an edge from casting occlusion it should not.
                float rangeCheck = smoothstep(0.0, 1.0, radius / max(abs(position.z - sceneZ), 1e-4));
                occlusion += (sceneZ <= samplePos.z - bias ? 1.0 : 0.0) * rangeCheck;
            }

            float ao = 1.0 - (occlusion / float(kCount)) * p.uParams.y;
            ao = pow(clamp(ao, 0.0, 1.0), max(p.uParams.w, 0.01));

            // The temporal blend: find where this surface sat on the previous frame's screen, check the
            // previous depth agrees it was this surface, and ease the fresh estimate onto the history.
            if (p.uTemporal.x > 0.5)
            {
                vec3 relativeToCamera = (p.uViewToWorld * vec4(position, 0.0)).xyz;
                vec3 relativeToPrevCamera = relativeToCamera + p.uCameraDelta.xyz;
                vec3 prevView = (p.uPrevWorldToView * vec4(relativeToPrevCamera, 0.0)).xyz;

                if (prevView.z > p.uUnproject.z)
                {
                    vec2 prevNdc = vec2(
                        prevView.x * p.uUnproject.x / prevView.z,
                        -prevView.y * p.uUnproject.y / prevView.z);
                    vec2 prevUv = prevNdc * 0.5 + 0.5;

                    if (all(greaterThanEqual(prevUv, vec2(0.0))) && all(lessThanEqual(prevUv, vec2(1.0))))
                    {
                        // Validated along the surface normal rather than by a depth ratio: reconstruct
                        // where the previous frame actually saw a surface at that pixel, and measure how
                        // far off this surface's plane it lies. A grazing wall fails a depth-ratio test
                        // the moment the camera moves — the same trap every temporal pass here has hit.
                        float prevDepth = texture(uPreviousDepth, prevUv).r;
                        if (prevDepth > 0.0)
                        {
                            vec3 prevSurface = ViewPositionOf(prevUv, prevDepth);

                            // Both in the previous frame's view space, so the comparison needs no second
                            // rotation; the normal is carried over by the same rotation the point took.
                            vec3 prevNormal = normalize((p.uPrevWorldToView * (p.uViewToWorld * vec4(normal, 0.0))).xyz);
                            float planeDistance = abs(dot(prevSurface - prevView, prevNormal));
                            float pixelSpan = (2.0 * position.z) / (p.uUnproject.y * p.uFullResolution.y);

                            if (planeDistance < max(0.05, 8.0 * pixelSpan))
                            {
                                float history = texture(uPreviousOcclusion, prevUv).r;
                                ao = mix(history, ao, 0.125);
                            }
                        }
                    }
                }
            }

            imageStore(uOcclusion, px, vec4(ao));
        }
        """;

    /// <summary>Initialises a new instance of the <see cref="SsaoPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <exception cref="GraphicsDeviceException">The pass could not be created.</exception>
    public SsaoPass(
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
            "ssao.comp",
            Source,
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
