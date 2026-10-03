// Averages the traced shadow mask over a bounded window of frames, reprojecting the previous mean
// onto this frame's pixels.
//
// A running mean over a bounded window rather than an unbounded one: the cap is what keeps it
// responsive, because a mean with no bound eventually refuses to move at all, and a shadow that does
// change -- a door swinging, the sun tracking -- would take as many frames to catch up as it had
// spent standing still.

// Both are read by texel or by an explicit filter; the depth needs no sampler and the history does.
[[vk::binding(0, 0)]] Texture2D<float4> uDepth;
[[vk::binding(2, 0)]] Texture2D<float4> uPrevHistory;
[[vk::binding(3, 0)]] SamplerState uPrevHistorySampler;

// This frame's reconstructed mask, read and then replaced by the mean.
[[vk::binding(4, 0)]] [[vk::image_format("r8")]] RWTexture2D<float> uShadow;

// The accumulation carried forward: the mean, the view depth it was measured at, and the count.
[[vk::binding(5, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uHistory;

// The frame's motion field, moving objects' own stamped over the camera's: unjittered current UV less previous.
// Read only when control.w says it was made this frame.
[[vk::binding(7, 0)]] Texture2D<float4> uVelocity;

struct Params
{
    // this frame's view rotation, camera-relative.
    float4x4 viewToWorld;

    // the previous frame's inverse view rotation.
    float4x4 prevWorldToView;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;

    // this frame's camera minus the previous frame's, w unused.
    float4 cameraDelta;

    // xScale, yScale, near, far of the previous unjittered projection.
    float4 prevProjection;

    // frames to average over, hasHistory, depth tolerance, whether uVelocity is this frame's.
    float4 control;

    // this frame's jitter in pixels, then the previous frame's.
    float4 jitter;
};

[[vk::binding(6, 0)]] ConstantBuffer<Params> p;

// The view-space position a depth sample came from. Identical to the velocity pass's, deliberately
// so: the two have to agree about where a pixel was or the mask and the colour would reproject to
// different places and the shadow would slide across the surface it belongs to.
float3 ViewPositionAt(float2 uv, float depth)
{
    float ndcZ = 1.0 - (2.0 * depth);
    float near = p.unproject.z;
    float far = p.unproject.w;
    float viewZ = (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
    float2 ndc = (uv * 2.0) - 1.0;
    return float3(ndc.x * viewZ / p.unproject.x, -ndc.y * viewZ / p.unproject.y, viewZ);
}

// Catmull-Rom over the accumulated mask, nine bilinear taps across the 4x4 footprint. The same filter
// the history update uses, and here for the same reason: a single bilinear fetch low-pass-filters what
// it reads, and a running mean re-reads its own history every frame, so the loss compounds. Sixteen
// frames of it measurably softened the office shadow edges -- 0.0253 to 0.0237 of high-frequency energy
// -- which is the accumulation buying stability with sharpness it did not need to spend. The negative
// lobes undo most of it.
float4 SampleHistoryCatmullRom(float2 uv)
{
    float2 size = p.resolution.xy;
    float2 invSize = p.resolution.zw;

    float2 samplePos = uv * size;
    float2 texel1 = floor(samplePos - 0.5) + 0.5;
    float2 f = samplePos - texel1;

    float2 w0 = f * (-0.5 + (f * (1.0 - (0.5 * f))));
    float2 w1 = 1.0 + (f * f * (-2.5 + (1.5 * f)));
    float2 w2 = f * (0.5 + (f * (2.0 - (1.5 * f))));
    float2 w3 = f * f * (-0.5 + (0.5 * f));

    float2 w12 = w1 + w2;
    float2 offset12 = w2 / max(w12, float2(1e-5, 1e-5));

    float2 pos0 = (texel1 - 1.0) * invSize;
    float2 pos3 = (texel1 + 2.0) * invSize;
    float2 pos12 = (texel1 + offset12) * invSize;

    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos0.x, pos0.y), 0.0) * w0.x * w0.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos12.x, pos0.y), 0.0) * w12.x * w0.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos3.x, pos0.y), 0.0) * w3.x * w0.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos0.x, pos12.y), 0.0) * w0.x * w12.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos12.x, pos12.y), 0.0) * w12.x * w12.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos3.x, pos12.y), 0.0) * w3.x * w12.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos0.x, pos3.y), 0.0) * w0.x * w3.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos12.x, pos3.y), 0.0) * w12.x * w3.y;
    result += uPrevHistory.SampleLevel(uPrevHistorySampler, float2(pos3.x, pos3.y), 0.0) * w3.x * w3.y;
    return result;
}

// This frame's traced mask, per thread of the group, for the history clamp below.
groupshared float gCurrent[8][8];

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID, uint3 local : SV_GroupThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.resolution.xy);
    bool inside = px.x < size.x && px.y < size.y;

    // Read before any thread of the group replaces its own texel, and shared so every thread reaches the
    // barrier -- the image's ragged edge included, standing in as lit.
    float current = inside ? uShadow[px] : 1.0;
    gCurrent[local.y][local.x] = current;
    GroupMemoryBarrierWithGroupSync();

    if (!inside)
    {
        return;
    }

    // The range this frame's mask spans around the pixel, from the group's own threads so the answer never
    // depends on which group ran first. A mean held inside it forgets a shadow the moment its caster has
    // gone: the ground a moving car has left is lit all round, and its old shadow no longer fades out over
    // the whole window as a trail behind it. Inside a penumbra the range is wide and the mean is untouched.
    float currentMin = current;
    float currentMax = current;
    for (int ny = -1; ny <= 1; ny++)
    {
        for (int nx = -1; nx <= 1; nx++)
        {
            int2 n = int2(local.xy) + int2(nx, ny);
            if (all(n >= int2(0, 0)) && all(n < int2(8, 8)))
            {
                float v = gCurrent[n.y][n.x];
                currentMin = min(currentMin, v);
                currentMax = max(currentMax, v);
            }
        }
    }

    float depth = uDepth.Load(int3(px, 0)).r;

    // Where this pixel's sample actually is, which is NOT its centre. The frame was rasterised through
    // a projection displaced by the jitter, so the scene point pixel k measured sits at k + 0.5 - jitter
    // in the unjittered screen the projection scales below describe. Ignoring that reprojects every
    // pixel from up to a whole pixel away, and because the jitter follows a sequence rather than a
    // constant the error changes every frame -- which is a shadow that slides a pixel or two back and
    // forth while the camera stands still.
    float2 uv = (float2(px) + 0.5 - p.jitter.xy) * p.resolution.zw;

    // Sky. Nothing casts onto it and nothing is accumulated for it; leaving the mask alone keeps
    // whatever the trace decided, which for the sky is fully lit.
    if (depth <= 0.0)
    {
        uHistory.Store(px, float4(current, 0.0, 0.0, 0.0));
        return;
    }

    float3 viewPos = ViewPositionAt(uv, depth);

    // Camera-relative throughout, so a world far from the origin never loses precision to the
    // subtraction of two large numbers.
    float3 relativeToCamera = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz;
    float3 relativeToPrevCamera = relativeToCamera + p.cameraDelta.xyz;
    float3 prevView = mul(p.prevWorldToView, float4(relativeToPrevCamera, 0.0)).xyz;

    float accumulated = current;
    float count = 1.0;

    bool hasHistory = p.control.y > 0.5;
    if (hasHistory && prevView.z > p.prevProjection.z)
    {
        float2 prevNdc = float2(
            prevView.x * p.prevProjection.x / prevView.z,
            -prevView.y * p.prevProjection.y / prevView.z);

        // Where the camera alone says this point was. A car, a train, a door moved on its own as well: the
        // motion field carries that, and where it disagrees with the camera by more than the jitter could
        // explain, the surface came from where the field says -- not from the road the camera would point at.
        float2 prevUnjittered = (prevNdc * 0.5) + 0.5;
        bool ownMotion = false;
        if (p.control.w > 0.5)
        {
            float2 motion = uVelocity.Load(int3(px, 0)).xy;
            float2 apart = (motion - (uv - prevUnjittered)) * p.resolution.xy;
            if (dot(apart, apart) > 2.25)
            {
                prevUnjittered = uv - motion;
                ownMotion = true;
            }
        }

        // And back the other way for the fetch: the previous frame stored its answer at the pixel whose
        // own displaced sample was this point, so the position to read is the unjittered one carried
        // forward by THAT frame's jitter rather than this one's.
        float2 prevUv = prevUnjittered + (p.jitter.zw * p.resolution.zw);

        if (all(prevUv >= float2(0.0, 0.0)) && all(prevUv <= float2(1.0, 1.0)))
        {
            // The depth and the sample count come from a plain bilinear tap: they are metadata rather
            // than signal, and a filter with negative lobes can overshoot a count past its cap or drag
            // a depth across a silhouette into a value neither surface has.
            float4 history = uPrevHistory.SampleLevel(uPrevHistorySampler, prevUv, 0.0);
            history.x = clamp(SampleHistoryCatmullRom(prevUv).x, currentMin, currentMax);

            // The test that separates "this is the same surface I averaged last frame" from "this is
            // something else that happens to reproject here". Relative rather than absolute: a tolerance
            // in metres that separates two surfaces at arm's length separates nothing at all at three
            // hundred, which is where the accumulation is needed most.
            //
            // Judged as the BEST agreement over the four texels under the bilinear footprint, not the
            // interpolated value: on a floor receding toward the sun -- exactly where a sun shadow lives
            // -- depth changes fast across neighbouring pixels, and the interpolated depth matches
            // nothing whenever the camera is moving and prevUv sits between texels. A still camera lands
            // on texel centres and passed; any motion rejected, and the mask fell back to a single ray's
            // noise on the very surfaces being watched.
            float expected = prevView.z;
            float4 tapDepths = uPrevHistory.GatherGreen(uPrevHistorySampler, prevUv);
            float depthDisagreement = min(
                min(abs(tapDepths.x - expected), abs(tapDepths.y - expected)),
                min(abs(tapDepths.z - expected), abs(tapDepths.w - expected)));
            // A surface that moved on its own is not where the camera expected it, so the depth it is judged
            // against is meaningless there; the clamp below is what keeps its history honest.
            bool sameSurface = ownMotion || depthDisagreement <= (expected * p.control.z);

            if (sameSurface && history.z > 0.0)
            {
                // A running mean over a bounded window.
                count = min(history.z + 1.0, max(p.control.x, 1.0));
                accumulated = lerp(history.x, current, 1.0 / count);
            }
        }
    }

    uShadow[px] = accumulated;
    uHistory.Store(px, float4(accumulated, viewPos.z, count, 0.0));
}
