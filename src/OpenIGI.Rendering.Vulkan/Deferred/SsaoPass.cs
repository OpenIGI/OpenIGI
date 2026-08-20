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
/// flight, so the frame that reads it is two frames behind the one that wrote it and its fence has been
/// waited on — no read races a write.
/// </para>
/// <para>
/// Built on the backend's own compute-pass helper rather than routed through the engine's compute seam,
/// because it binds the depth attachment's own view — which is not a texture handle the seam knows.
/// </para>
/// </remarks>
internal sealed class SsaoPass : IDisposable
{
    private const int ParamSize = 48;

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,   // the scene depth
        ComputeBindingKind.StorageImage,   // the occlusion written out
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    private const string Source =
        """
        #version 450

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uDepth;
        layout(set = 0, binding = 1, r8) uniform writeonly image2D uOcclusion;
        layout(set = 0, binding = 2) uniform Params
        {
            vec4 uResolution;   // width, height, 1/width, 1/height
            vec4 uUnproject;    // xScale, yScale, near, far
            vec4 uParams;       // radius (metres), intensity, bias, power
        } p;

        // View position from a reverse-Z depth sample, the same reconstruction the lighting uses so the two
        // agree on where a surface is; the vertical is negated for the G-buffer's Y flip.
        vec3 ViewPosition(vec2 uv)
        {
            float d = texture(uDepth, uv).r;
            float ndcZ = 1.0 - 2.0 * d;
            float near = p.uUnproject.z;
            float far = p.uUnproject.w;
            float viewZ = (2.0 * near * far) / ((far + near) - ndcZ * (far - near));
            vec2 ndc = uv * 2.0 - 1.0;
            return vec3(ndc.x * viewZ / p.uUnproject.x, -ndc.y * viewZ / p.uUnproject.y, viewZ);
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

            vec2 uv = (vec2(px) + 0.5) * p.uResolution.zw;
            float depth = texture(uDepth, uv).r;

            // The sky (reverse-Z far is zero) is not a surface and is never occluded.
            if (depth <= 0.0)
            {
                imageStore(uOcclusion, px, vec4(1.0));
                return;
            }

            vec3 position = ViewPosition(uv);

            // A face normal from the neighbouring depths — a compute shader has no screen-space derivatives.
            // Turned to face the camera: the reconstruction's cross product can come out either way, and a
            // back-facing normal would send the whole hemisphere into the geometry.
            vec3 dx = ViewPosition(uv + vec2(p.uResolution.z, 0.0)) - position;
            vec3 dy = ViewPosition(uv + vec2(0.0, p.uResolution.w)) - position;
            vec3 normal = normalize(cross(dx, dy));
            if (dot(normal, position) > 0.0)
            {
                normal = -normal;
            }

            // A per-pixel rotation so the fixed sample directions do not band across the image.
            float angle = Hash(vec2(px)) * 6.2831853;
            vec3 randomVec = vec3(cos(angle), sin(angle), 0.0);
            vec3 tangent = normalize(randomVec - normal * dot(randomVec, normal));
            vec3 bitangent = cross(normal, tangent);
            mat3 tbn = mat3(tangent, bitangent, normal);

            // The radius is an absolute view-space distance: the hemisphere covers a fixed span of the world,
            // and the perspective divide shrinks it on screen as the surface recedes — the correct
            // screen-space AO behaviour. It must not be scaled by the view depth: in this large-unit world
            // every view depth already runs to hundreds of units, so a depth-scaled radius would fling every
            // sample clear off the screen and none would land on a surface to occlude against.
            float radius = p.uParams.x;
            float bias = p.uParams.z * radius;
            const int kCount = 16;
            float occlusion = 0.0;

            for (int i = 0; i < kCount; i++)
            {
                // A cosine-weighted point in the hemisphere over the surface: a disk in the tangent plane
                // lifted along the normal, so the directions spread laterally enough to find an adjacent
                // wall as well as up into open space.
                float r1 = Hash(vec2(float(i) + 0.13, float(px.x * 3 + px.y)));
                float r2 = Hash(vec2(float(i) * 1.7 + 0.5, float(px.y * 3 + px.x)));
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
    /// <param name="occlusionView">The occlusion image to write.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="unprojectX">The projection's horizontal scale.</param>
    /// <param name="unprojectY">The projection's vertical scale.</param>
    /// <param name="near">The near plane.</param>
    /// <param name="far">The far plane.</param>
    /// <param name="radius">The sample radius in metres.</param>
    /// <param name="intensity">How strongly occlusion darkens.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView occlusionView,
        int width,
        int height,
        float unprojectX,
        float unprojectY,
        float near,
        float far,
        float radius,
        float intensity)
    {
        ReadOnlySpan<float> data =
        [
            width, height, 1f / Math.Max(1, width), 1f / Math.Max(1, height),
            unprojectX, unprojectY, near, far,
            radius, intensity, 0.02f, 1.5f,
        ];

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(occlusionView),
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
