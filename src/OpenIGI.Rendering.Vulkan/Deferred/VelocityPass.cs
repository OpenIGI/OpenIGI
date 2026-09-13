using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// A compute pass that works out, for every pixel, where the surface under it was on the previous frame.
/// </summary>
/// <remarks>
/// <para>
/// This is the primitive every temporal technique is built on. Accumulating anything across frames —
/// an indirect-lighting estimate gathered from too few rays, a reflection traced at quarter resolution,
/// an anti-aliased edge — means finding last frame's value for the surface now under this pixel, and
/// that surface has moved on screen because the camera moved. Without this, accumulation either smears
/// (reusing whatever was at the same pixel) or is thrown away (reusing nothing).
/// </para>
/// <para>
/// It is derived from depth rather than written by the geometry pass. For a world that does not move,
/// the only thing that changes where a surface lands on screen is the camera, so the previous position
/// follows from this frame's depth and the two frames' cameras — no per-object previous transform, no
/// second position interpolator, nothing pushed per draw. Geometry that moves under its own power needs
/// its own velocity written during the base pass, and that is a later addition; static world geometry is
/// all of this scene.
/// </para>
/// <para>
/// The arithmetic is deliberately kept camera-relative. This world's coordinates run to tens of
/// thousands of units, and subtracting two nearby absolute positions in single precision would throw
/// away most of the significant digits of a difference that is often a fraction of a unit. Working in
/// positions relative to the camera, and applying only the small camera-to-camera delta, keeps the
/// magnitudes near the values that matter.
/// </para>
/// <para>
/// The result is the offset from this pixel to where it was, in texture coordinates, so a consumer reads
/// its history at <c>uv - velocity</c>. It is stored signed and floating-point rather than in the
/// reference's biased fixed-point encoding: the values cluster tightly around zero, which is exactly
/// where a float has precision to spare and a fixed-point encoding has least.
/// </para>
/// </remarks>
internal sealed class VelocityPass : IDisposable
{
    // Two 4x4 matrices and five 4-vectors.
    private const int ParamSize = (2 * 64) + (5 * 16);

    private static readonly ComputeBindingKind[] Bindings =
    [
        ComputeBindingKind.SampledImage,   // the scene depth
        ComputeBindingKind.StorageImage,   // the motion written out
    ];

    private readonly VulkanComputePass _pass;
    private bool _disposed;

    private const string Source =
        """
        #version 450

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uDepth;
        layout(set = 0, binding = 1, rg16f) uniform writeonly image2D uVelocity;
        layout(set = 0, binding = 2) uniform Params
        {
            mat4 uViewToWorld;       // this frame's view rotation, camera-relative
            mat4 uPrevWorldToView;   // the previous frame's inverse view rotation
            vec4 uUnproject;         // xScale, yScale, near, far
            vec4 uResolution;        // width, height, 1/width, 1/height
            vec4 uCameraDelta;       // this frame's camera minus the previous frame's, w unused
            vec4 uPrevProjection;    // xScale, yScale, near, far of the previous unjittered projection
            vec4 uJitter;            // current raster jitter in input pixels, xy; zw unused
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

            vec2 rasterUv = (vec2(px) + 0.5) * p.uResolution.zw;
            float depth = texture(uDepth, rasterUv).r;

            // Reverse-Z clears to zero, so a pixel the geometry never covered reads exactly zero. That is
            // the sky, and the sky is not motionless: it sits at infinity, so it turns with the camera
            // exactly as the ground does and only the camera's TRANSLATION leaves it where it was. It
            // used to be reported as still, and then every pixel the sky had just been revealed at --
            // behind a strut, a wire, a soldier against the horizon -- read its history from the same
            // place rather than from where that sky came from, and kept a copy of whatever thin thing
            // had stood there: a ghost one frame behind, on every pan. Reprojecting the pixel's view
            // direction alone, with no parallax, is the sky's true motion.
            if (depth <= 0.0)
            {
                vec2 currentUv = rasterUv - (p.uJitter.xy * p.uResolution.zw);
                vec2 ndc = currentUv * 2.0 - 1.0;
                vec3 direction = vec3(ndc.x / p.uUnproject.x, -ndc.y / p.uUnproject.y, 1.0);
                vec3 worldDirection = (p.uViewToWorld * vec4(direction, 0.0)).xyz;
                vec3 prevDirection = (p.uPrevWorldToView * vec4(worldDirection, 0.0)).xyz;
                if (prevDirection.z <= 1e-4)
                {
                    imageStore(uVelocity, px, vec4(0.0));
                    return;
                }

                vec2 prevSkyNdc = vec2(
                    prevDirection.x * p.uPrevProjection.x / prevDirection.z,
                    -prevDirection.y * p.uPrevProjection.y / prevDirection.z);
                imageStore(uVelocity, px, vec4(currentUv - (prevSkyNdc * 0.5 + 0.5), 0.0, 0.0));
                return;
            }

            // The first-person rig lives in the foreground depth sliver (reverse-Z 0.9..1, nearer than
            // any world surface can be) and is locked to the camera: it does not move on screen when the
            // camera turns. This pass derives motion from depth reprojection, which knows nothing of that
            // rigging -- it read the rig's near depth as world geometry a hand's length away and handed it
            // an enormous velocity every time the view rotated. The motion blur then smeared that velocity
            // across the whole weapon, and the temporal pass chased the same false motion into a ghosting
            // weapon. Its true screen motion is very nearly zero, which is both what stills the blur and
            // what lets the temporal pass hold the weapon steady.
            if (depth >= 0.9)
            {
                imageStore(uVelocity, px, vec4(0.0));
                return;
            }

            // The depth belongs to the ray displaced by this frame's projection jitter. Reconstruct that
            // surface through its UNJITTERED screen position, just as the reference's no-AA clip transform
            // does. Using rasterUv here happens to cancel for a held camera and for much of a rotation,
            // but it reconstructs the wrong world point under translation; the parallax error then grows
            // with walking speed and makes every history lookup land between the right samples.
            vec2 currentUv = rasterUv - (p.uJitter.xy * p.uResolution.zw);
            vec3 viewPos = ViewPositionAt(currentUv, depth);

            // Camera-relative throughout: the world position is never formed, only the offset from this
            // frame's camera, and then the offset from the previous one.
            vec3 relativeToCamera = (p.uViewToWorld * vec4(viewPos, 0.0)).xyz;
            vec3 relativeToPrevCamera = relativeToCamera + p.uCameraDelta.xyz;
            vec3 prevView = (p.uPrevWorldToView * vec4(relativeToPrevCamera, 0.0)).xyz;

            // Behind the previous camera, so it had no screen position at all — nothing to reproject onto.
            if (prevView.z <= p.uPrevProjection.z)
            {
                imageStore(uVelocity, px, vec4(0.0));
                return;
            }

            // The projection applied by hand rather than as a matrix, from the same four numbers the
            // reconstruction above used, so the two are exact inverses of one another.
            vec2 prevNdc = vec2(
                prevView.x * p.uPrevProjection.x / prevView.z,
                -prevView.y * p.uPrevProjection.y / prevView.z);
            vec2 prevUv = prevNdc * 0.5 + 0.5;

            // Motion is always unjittered-current minus unjittered-previous. Consumers that read a raw
            // frame texture add that frame's jitter themselves; the super-sampled history is already in
            // unjittered output space and uses this vector directly.
            imageStore(uVelocity, px, vec4(currentUv - prevUv, 0.0, 0.0));
        }
        """;

    /// <summary>Initialises a new instance of the <see cref="VelocityPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public VelocityPass(
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
            "velocity.comp",
            Source,
            Bindings,
            ParamSize,
            framesInFlight);
    }

    /// <summary>Records the velocity dispatch for one frame.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="depthView">The scene depth view to read.</param>
    /// <param name="velocityView">The velocity image to write.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="previousViewToWorld">The previous frame's view-to-world rotation.</param>
    /// <param name="cameraDelta">This frame's camera position minus the previous frame's.</param>
    /// <param name="unprojection">This frame's projection scales and planes.</param>
    /// <param name="previousUnprojection">The previous frame's, unjittered.</param>
    /// <param name="jitterX">This frame's horizontal projection jitter, in input pixels.</param>
    /// <param name="jitterY">This frame's vertical projection jitter, in input pixels.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ImageView depthView,
        ImageView velocityView,
        int width,
        int height,
        Matrix3f viewToWorld,
        Matrix3f previousViewToWorld,
        Vector3f cameraDelta,
        DepthUnprojection unprojection,
        DepthUnprojection previousUnprojection,
        float jitterX,
        float jitterY)
    {
        // A rotation's inverse is its transpose, which is what turns a world-relative offset back into the
        // previous frame's view space without forming an inverse.
        Matrix3f previousWorldToView = previousViewToWorld.Transposed();

        Span<float> data = stackalloc float[ParamSize / sizeof(float)];
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

        data[48] = jitterX;
        data[49] = jitterY;
        data[50] = 0f;
        data[51] = 0f;

        ReadOnlySpan<ComputeResource> resources =
        [
            new ComputeResource(depthView),
            new ComputeResource(velocityView),
        ];

        _pass.Record(
            cmd,
            frame,
            resources,
            data,
            (uint)((width + 7) / 8),
            (uint)((height + 7) / 8));
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
