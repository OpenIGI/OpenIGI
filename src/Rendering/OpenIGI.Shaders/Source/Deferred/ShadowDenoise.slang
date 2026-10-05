// The traced sun shadow, spread over the penumbra its own occluder distance implies.

#include "../Common/ViewDepth.slang"

// visibility, occluder distance.
[[vk::binding(0, 0)]] Texture2D<float4> uRaw;
[[vk::binding(1, 0)]] SamplerState uRawSampler;

// reverse-Z.
[[vk::binding(2, 0)]] Texture2D<float4> uDepth;
[[vk::binding(3, 0)]] SamplerState uDepthSampler;

// .z is frames accumulated so far.
[[vk::binding(4, 0)]] Texture2D<float4> uHistory;
[[vk::binding(5, 0)]] SamplerState uHistorySampler;

[[vk::binding(6, 0)]] [[vk::image_format("r8")]] WTexture2D<float4> uShadow;

// The G-buffer's world normals, so the spread stays on one surface: see the tap weight below.
[[vk::binding(8, 0)]] Texture2D<float4> uNormal;
[[vk::binding(9, 0)]] SamplerState uNormalSampler;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;

    // xScale, yScale, near, far.
    float4 unproject;

    // tangent of the sun's half-angle, one over the accumulation ceiling.
    float4 sun;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

// The eight taps, in pattern units, used in this order. Four sit on the rim and four at a little under
// half its radius, so the root-mean-square radius is about three quarters of the extent and the filter's
// effective width is roughly the penumbra radius. The order matters as much as the positions: they came
// out of a small annealing search that scored every PREFIX of the list -- the first two, three, four and
// so on -- for how close its mean stays to the centre and how far it is from a line, and the whole set
// for isotropy and even spacing. So a tap count lowered later still samples a balanced, two-dimensional
// patch instead of leaning to one side.
static const float2 kSampleSet[8] = {
    float2(0.940, 0.342), float2(-0.965, -0.262), float2(0.135, -0.429), float2(-0.137, 0.429),
    float2(0.349, -0.937), float2(-0.274, 0.962), float2(-0.407, -0.192), float2(0.435, 0.116),
};
static const float kSampleSetExtent = 1.0;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.size.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float2 centre = uRaw.Load(int3(px, 0)).xy;
    float centreDepth = uDepth.Load(int3(px, 0)).x;
    float3 centreNormal = normalize((uNormal.Load(int3(px, 0)).xyz * 2.0) - 1.0);

    // Sky, or a surface facing away from the sun. Neither has a penumbra: the first is shadowed by
    // nothing and the second shadows itself completely, and the trace recorded no occluder distance for
    // either. Filtering them would only drag a neighbour's answer in.
    if (centreDepth <= 0.0 || centre.y <= 0.0)
    {
        uShadow.Store(px, float4(centre.x, centre.x, centre.x, centre.x));
        return;
    }

    // The penumbra this pixel's own occluder throws, for a directional light: the tangent of the sun's
    // half-angle times how far the ray got before something stopped it.
    float worldRadius = p.sun.x * centre.y;

    // Projected to pixels through the same projection the depth unprojection inverts, so a surface
    // further from the camera gets a proportionally narrower filter and the softness stays a property of
    // the geometry rather than of where the camera happens to stand.
    float viewZ = max(ViewDepthAt(centreDepth, p.unproject), 1e-4);
    float pixelRadius = worldRadius * p.unproject.x * (0.5 * p.size.x) / viewZ;

    // How much of that spread this pixel still needs. The temporal pass behind this one averages a
    // DIFFERENTLY JITTERED ray every frame, and the fraction of those rays an occluder blocks IS the
    // penumbra -- so once its history is deep, the mean it holds already resolves the soft edge at its
    // true width, and spreading the frame spatially on top of that widens a penumbra that was already
    // right. Measured at the office window: spreading unconditionally, the converged shadow ramps over
    // 4.0 pixels against a target of 3.0; not spreading at all, 3.0 against 3.0, quartiles included.
    //
    // It cannot simply be dropped, though. The spread is what makes ONE ray's binary answer usable,
    // which is all the frames after a disocclusion or a camera cut have to live on, and it is worth
    // about five percent of the mask's frame-to-frame stability under a shaken camera. So it holds the
    // full occluder-derived radius while the history is shallow and eases down to a two-pixel cap as the
    // history converges -- the spatial and the temporal halves of one denoiser sharing the work rather
    // than each doing it in full. Two pixels still averages away the residual variance of a sixteen-frame
    // mean of binary samples and is far too narrow to shape the penumbra. The easing is deliberately
    // late: nothing narrows at all until the accumulation is a quarter of the way to its ceiling.
    float accumulated = uHistory.Load(int3(px, 0)).z * p.sun.y;
    float converged = smoothstep(0.25, 0.9, clamp(accumulated, 0.0, 1.0));

    // The converged cap is PROPORTIONAL, not flat. A flat cap sharpens every penumbra to the same width
    // and so throws away the one thing a distance-derived penumbra is for: contact hardening, hard where
    // the occluder is close and genuinely soft where it is far. Keeping a share of the radius preserves
    // that range while still letting a narrow penumbra collapse to the couple of pixels the temporal mean
    // has already resolved on its own.
    pixelRadius = lerp(pixelRadius, max(2.0, 0.35 * pixelRadius), converged);

    // Below half a pixel there is nothing to reconstruct: the shadow really is that sharp here, and
    // spreading it would soften a contact edge that should stay hard.
    if (pixelRadius < 0.5)
    {
        uShadow.Store(px, float4(centre.x, centre.x, centre.x, centre.x));
        return;
    }

    // Capped so a caster far enough away cannot turn this into a screen-wide blur, which costs bandwidth
    // and produces a softness no geometry asked for.
    pixelRadius = min(pixelRadius, 32.0);

    // Each pixel of a 2x2 block shifts the whole pattern about a third of the extent into its own diagonal
    // quadrant, so four neighbours read interleaved positions and between them cover the disk four times
    // as densely as one pattern does.
    float2 trackOffset = (1.0 - (float2(float(px.x & 1), float(px.y & 1)) * 2.0)) * (0.32 * kSampleSetExtent);

    float scale = pixelRadius / kSampleSetExtent;
    float visibility = centre.x;
    float weightSum = 1.0;

    // Sampled at fractional positions through the bilinear sampler. Rounding each tap to a whole texel
    // instead collapses the eight samples onto a handful of them once the radius is small, and the shadow
    // edge comes out MORE stair-stepped than the unfiltered one -- the filter then quantises the very edge
    // it was added to smooth.
    float2 centreUv = (float2(px) + 0.5) * p.size.zw;

    for (int i = 0; i < 8; i++)
    {
        float2 tapUv = clamp(
            centreUv + ((kSampleSet[i] + trackOffset) * scale * p.size.zw),
            0.5 * p.size.zw,
            1.0 - (0.5 * p.size.zw));

        float tapVisibility = uRaw.SampleLevel(uRawSampler, tapUv, 0.0).x;
        float tapDepth = uDepth.SampleLevel(uDepthSampler, tapUv, 0.0).x;

        // Refuses to average across a depth discontinuity, because a sample sitting on another surface
        // knows nothing about this pixel's occluder and would bleed one object's shadow onto another.
        // Compared in view space rather than on the stored value, so a single tolerance holds at every
        // distance instead of only near the camera.
        float tapViewZ = ViewDepthAt(tapDepth, p.unproject);
        float weight = (tapDepth > 0.0 && abs(tapViewZ - viewZ) < (viewZ * 0.02)) ? 1.0 : 0.0;

        // And not across a crease. Where a wall meets the ground the two stand at the same depth, so the
        // depth test let the lit wall and the shadowed ground average into each other and the line where
        // they meet went soft; their normals are what tells them apart.
        float3 tapNormal = normalize((uNormal.SampleLevel(uNormalSampler, tapUv, 0.0).xyz * 2.0) - 1.0);
        float facing = saturate(dot(tapNormal, centreNormal));
        weight *= facing * facing * facing * facing;

        visibility += tapVisibility * weight;
        weightSum += weight;
    }

    float resolved = visibility / weightSum;
    uShadow.Store(px, float4(resolved, resolved, resolved, resolved));
}
