using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
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

    private const string Source =
        """
        #version 450

        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uRaw;      // visibility, occluder distance
        layout(set = 0, binding = 1) uniform sampler2D uDepth;    // reverse-Z
        layout(set = 0, binding = 2) uniform sampler2D uHistory;  // .z is frames accumulated so far
        layout(set = 0, binding = 3, r8) uniform writeonly image2D uShadow;

        layout(set = 0, binding = 4) uniform Params
        {
            vec4 uSize;       // width, height, 1/width, 1/height
            vec4 uUnproject;  // xScale, yScale, near, far
            vec4 uSun;        // tangent of the sun's half-angle, one over the accumulation ceiling
        } p;

        // The first eight offsets of the reference's Stackowiak set, in pixels. The order is not
        // arbitrary and must not be re-sorted: the set is built so that any PREFIX of it is itself well
        // distributed, which is what lets the count be lowered without the pattern collapsing to a line.
        const vec2 kSampleSet[8] = vec2[8](
            vec2(-0.5, -0.5), vec2(+0.5, -0.5), vec2(-0.5, +0.5), vec2(+0.5, +0.5),
            vec2(-1.5, +0.5), vec2(-1.5, -0.5), vec2(-0.5, +1.5), vec2(+1.5, -0.5));

        // The longest of those eight, so the set can be mapped onto a disk of the penumbra's radius.
        const float kSampleSetExtent = 1.5811388;

        // Reverse-Z device depth to view-space distance, the same mapping every other pass here uses.
        float ViewDepth(float depth)
        {
            float near = p.uUnproject.z;
            float far = p.uUnproject.w;
            float ndcZ = 1.0 - (2.0 * depth);
            return (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
        }

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            ivec2 size = ivec2(p.uSize.xy);
            if (px.x >= size.x || px.y >= size.y)
            {
                return;
            }

            vec2 centre = texelFetch(uRaw, px, 0).xy;
            float centreDepth = texelFetch(uDepth, px, 0).x;

            // Sky, or a surface facing away from the sun. Neither has a penumbra: the first is shadowed
            // by nothing and the second shadows itself completely, and the trace recorded no occluder
            // distance for either. Filtering them would only drag a neighbour's answer in.
            if (centreDepth <= 0.0 || centre.y <= 0.0)
            {
                imageStore(uShadow, px, vec4(centre.x));
                return;
            }

            // The penumbra this pixel's own occluder throws -- the reference's formula for a directional
            // light exactly: the tangent of the sun's half-angle times how far the ray got before
            // something stopped it.
            float worldRadius = p.uSun.x * centre.y;

            // Projected to pixels through the same projection the depth unprojection inverts, so a
            // surface further from the camera gets a proportionally narrower filter and the softness
            // stays a property of the geometry rather than of where the camera happens to stand.
            float viewZ = max(ViewDepth(centreDepth), 1e-4);
            float pixelRadius = worldRadius * p.uUnproject.x * (0.5 * p.uSize.x) / viewZ;

            // How much of that spread this pixel still needs. The temporal pass behind this one averages
            // a DIFFERENTLY JITTERED ray every frame, and the fraction of those rays an occluder blocks
            // IS the penumbra -- so once its history is deep, the mean it holds already resolves the soft
            // edge at its true width, and spreading the frame spatially on top of that widens a penumbra
            // that was already right. Measured against the reference at the office window: spreading
            // unconditionally, the converged shadow ramps over 4.0 pixels where the reference takes 3.0;
            // not spreading at all, 3.0 against 3.0, quartiles included.
            //
            // It cannot simply be dropped, though. The spread is what makes ONE ray's binary answer
            // usable, which is all the frames after a disocclusion or a camera cut have to live on, and
            // it is worth about five percent of the mask's frame-to-frame stability under a shaken
            // camera. So it holds the full occluder-derived radius while the history is shallow and eases
            // down to a two-pixel cap as the history converges -- the spatial and the temporal halves of
            // one denoiser sharing the work rather than each doing it in full. Two pixels still averages
            // away the residual variance of a sixteen-frame mean of binary samples and is far too narrow
            // to shape the penumbra. The easing is deliberately late: nothing narrows at all until the
            // accumulation is a quarter of the way to its ceiling.
            float accumulated = texelFetch(uHistory, px, 0).z * p.uSun.y;
            float converged = smoothstep(0.25, 0.9, clamp(accumulated, 0.0, 1.0));
            // The converged cap is PROPORTIONAL, not flat. A flat cap sharpens every penumbra to the
            // same width and so throws away the one thing a distance-derived penumbra is for: the
            // reference's shadows are crisper than ours at the median (2 px against 3) and yet carry four
            // times as many edges wider than eight pixels (1.48% against 0.36%) -- contact hardening, hard
            // where the occluder is close and genuinely soft where it is far. Keeping a share of the
            // radius preserves that range while still letting a narrow penumbra collapse to the couple of
            // pixels the temporal mean has already resolved on its own.
            pixelRadius = mix(pixelRadius, max(2.0, 0.35 * pixelRadius), converged);

            // Below half a pixel there is nothing to reconstruct: the shadow really is that sharp here,
            // and spreading it would soften a contact edge that should stay hard.
            if (pixelRadius < 0.5)
            {
                imageStore(uShadow, px, vec4(centre.x));
                return;
            }

            // Capped so a caster far enough away cannot turn this into a screen-wide blur, which costs
            // bandwidth and produces a softness no geometry asked for.
            pixelRadius = min(pixelRadius, 32.0);

            // The reference offsets the whole set by a quadrant per pixel, so four neighbours walk four
            // different patterns and the eight rays between them cover thirty-two distinct positions.
            uint track = uint(px.x & 1) | (uint(px.y & 1) << 1);
            vec2 trackOffset = vec2(
                (track & 1u) != 0u ? 0.5 : -0.5,
                (track & 2u) != 0u ? 0.5 : -0.5);

            float scale = pixelRadius / kSampleSetExtent;
            float visibility = centre.x;
            float weightSum = 1.0;

            // Sampled at fractional positions through the bilinear sampler, the way the reference offsets
            // its kernel in buffer UV. Rounding each tap to a whole texel instead collapses the eight
            // samples onto a handful of them once the radius is small, and the shadow edge comes out
            // MORE stair-stepped than the unfiltered one -- the filter then quantises the very edge it
            // was added to smooth.
            vec2 centreUv = (vec2(px) + 0.5) * p.uSize.zw;

            for (int i = 0; i < 8; i++)
            {
                vec2 tapUv = clamp(
                    centreUv + ((kSampleSet[i] + trackOffset) * scale * p.uSize.zw),
                    0.5 * p.uSize.zw,
                    1.0 - (0.5 * p.uSize.zw));

                float tapVisibility = texture(uRaw, tapUv).x;
                float tapDepth = texture(uDepth, tapUv).x;

                // SUBSTITUTION: the reference weights a sample by unpacking a compressed scene-metadata
                // record -- world position, normal, and a per-signal bilateral distance derived from the
                // bluring radius. That record exists so one kernel can serve four different denoised
                // signals; there is no such record here. What is kept is the part that decides a shadow:
                // refusing to average across a depth discontinuity, because a sample sitting on another
                // surface knows nothing about this pixel's occluder and would bleed one object's shadow
                // onto another. Compared in view space rather than on the stored value, so a single
                // tolerance holds at every distance instead of only near the camera.
                float tapViewZ = ViewDepth(tapDepth);
                float weight = (tapDepth > 0.0 && abs(tapViewZ - viewZ) < (viewZ * 0.02)) ? 1.0 : 0.0;

                visibility += tapVisibility * weight;
                weightSum += weight;
            }

            imageStore(uShadow, px, vec4(visibility / weightSum));
        }
        """;

    /// <summary>Initialises a new instance of the <see cref="ShadowDenoisePass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    public ShadowDenoisePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ShaderCompiler compiler,
        int framesInFlight)
        => _pass = new VulkanComputePass(
            vk, device, allocator, compiler, "shadow.denoise.comp", Source, Bindings, ParamSize, framesInFlight);

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
