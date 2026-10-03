// The lamp-shadow accumulation: this frame's traced visibility blended onto what the same surface held
// last frame.
//
// The trace fires about two binary rays per pixel, so a single frame is little more than a coin toss
// wherever a lamp is partly hidden. Averaging over frames is what turns that into a usable ratio, and
// it only works if the history being averaged in belongs to the same point on the same surface. So the
// pixel is followed back through the camera's motion (and its own, when it moved by itself), history
// from a different surface is refused, and what history remains is held inside the range this frame's
// neighbourhood says is plausible -- a variance clip in the manner of Salvi (2016) -- so a shadow that
// moved does not linger as a ghost.
//
// The clip is deliberately soft. A hard clamp against a neighbourhood this noisy would reject good
// history on every frame where the rays happened to agree with each other; instead the further the
// history had to be pulled in, the less it is trusted and the shorter the run of frames it may stand for.

#include "../Common/LampDenoiseCommon.slang"
#include "../Common/DepthFilter.slang"
#include "../Common/Unroll.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uRaw;
[[vk::binding(1, 0)]] SamplerState uRawSampler;
[[vk::binding(2, 0)]] DepthTexture2D uDepth;
[[vk::binding(3, 0)]] SamplerState uDepthSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uHistory;
[[vk::binding(5, 0)]] SamplerState uHistorySampler;
[[vk::binding(6, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uOutput;
[[vk::binding(8, 0)]] Texture2D<float4> uVelocity;
[[vk::binding(9, 0)]] SamplerState uVelocitySampler;

struct Params
{
    // Rotation only: turns a view-space offset into a world-space one around the camera.
    float4x4 viewToWorld;

    // Rotation only: turns a world-space offset around last frame's camera into last frame's view.
    float4x4 prevWorldToView;

    // xScale, yScale, near, far -- this frame and the last.
    float4 unproject;
    float4 prevUnproject;

    // width, height, 1/width, 1/height of the lamp images.
    float4 resolution;

    // xyz this camera position minus last frame's, w above a half when there is a history to read.
    float4 cameraDelta;

    // xy this frame's jitter, zw last frame's, in lamp pixels.
    float4 jitter;

    // x above a half to accumulate at all, y above a half when the motion field is valid.
    float4 control;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

// How many standard deviations either side of the neighbourhood mean the history may stand.
static const float kClipSigma = 1.25;

// The neighbourhood is two binary rays per tap, so a run of identical taps proves less than it seems:
// the rule of three bounds the true rate after n clean trials of two rays at 3 / (2n).
static const float kRuleOfThree = 3.0;
static const float kRaysPerTap = 2.0;

// A pixel whose own motion disagrees with the camera's by more than this many lamp pixels moved by itself.
static const float kOwnMotionPixels = 1.5;

// Below this the history taps' combined weight is treated as no history at all.
static const float kMinHistoryWeight = 1e-3;

// A history texel with less share than this held no lamp, and is not a sample.
static const float kMinHistoryShare = 1e-4;

// The narrowest the clip is ever judged against, so a flat neighbourhood does not make every
// difference look infinitely far outside it.
static const float kMinExtent = 0.01;

// The longest run of frames history may stand for when it had to be pulled in fully, and when it sat
// inside the range untouched.
static const float kDistrustedFrames = 4.0;
static const float kTrustedFrames = 32.0;

// Where this lamp pixel's depth sample sits in the full-resolution depth image: the G-buffer was drawn
// with this frame's jitter, so the sample belonging to the pixel is offset by it.
float2 JitteredDepthUv(int2 px)
{
    return ((float2(px) + 0.5) - p.jitter.xy) * p.resolution.zw;
}

float4 RawAt(int2 px)
{
    int2 last = int2(p.resolution.xy) - 1;
    return uRaw.Load(int3(clamp(px, int2(0, 0), last), 0));
}

float4 HistoryAt(int2 px)
{
    int2 last = int2(p.resolution.xy) - 1;
    return uHistory.Load(int3(clamp(px, int2(0, 0), last), 0));
}

// The motion of the full-resolution texel a coordinate falls in. Read unfiltered: at a moving object's
// outline a blend would be a motion that neither the object nor what lies behind it has.
float2 MotionAt(float2 uv)
{
    uint width;
    uint height;
    uVelocity.GetDimensions(width, height);
    int2 last = int2(int(width) - 1, int(height) - 1);
    int2 texel = clamp(int2(floor(uv * float2(float(width), float(height)))), int2(0, 0), last);
    return uVelocity.Load(int3(texel, 0)).xy;
}

// The range of values this frame's neighbourhood makes plausible for the pixel, as low and high per
// channel (visibility, share). Only the current frame is read, so a stale history cannot vouch for itself.
void NeighbourhoodRange(int2 px, float2 centre, out float2 low, out float2 high)
{
    float2 sum = float2(0.0, 0.0);
    float2 sumSquares = float2(0.0, 0.0);
    float count = 0.0;

    OPENIGI_UNROLL
    for (int dy = -2; dy <= 2; dy++)
    {
        OPENIGI_UNROLL
        for (int dx = -2; dx <= 2; dx++)
        {
            // The corners are the furthest taps and the least likely to be on the same patch of shadow.
            if (abs(dx) == 2 && abs(dy) == 2)
            {
                continue;
            }

            float2 tap = RawAt(px + int2(dx, dy)).xy;
            if (tap.y <= 0.0)
            {
                continue;
            }

            sum += tap;
            sumSquares += tap * tap;
            count += 1.0;
        }
    }

    // The centre always passes its own test, so the count is at least one.
    count = max(count, 1.0);
    float2 mean = sum / count;
    float2 sigma = sqrt(max((sumSquares / count) - (mean * mean), float2(0.0, 0.0)));

    low = mean - (kClipSigma * sigma);
    high = mean + (kClipSigma * sigma);

    // Never a range that rules out what was just traced here.
    low = min(low, centre);
    high = max(high, centre);

    // However quiet the neighbourhood, a handful of binary rays cannot pin visibility tighter than this.
    float floorHalfWidth = kRuleOfThree / (kRaysPerTap * count);
    low.x = min(low.x, mean.x - floorHalfWidth);
    high.x = max(high.x, mean.x + floorHalfWidth);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.resolution.x) || px.y >= int(p.resolution.y))
    {
        return;
    }

    float2 depthUv = JitteredDepthUv(px);
    float depth = DepthBilinear(uDepth, uDepthSampler, depthUv);

    // Sky: nothing to shadow and nothing to carry forward.
    if (depth <= 0.0)
    {
        uOutput.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 viewPos = ViewPositionAt(depthUv, depth, p.unproject);
    float2 current = RawAt(px).xy;

    // No lamp reaches this pixel. It reads as unshadowed, and carries a single frame so the spatial pass
    // passes it straight through.
    if (current.y <= 0.0)
    {
        uOutput.Store(px, float4(1.0, 0.0, 1.0, viewPos.z));
        return;
    }

    float2 low;
    float2 high;
    NeighbourhoodRange(px, current, low, high);
    float2 extent = (high - low) * 0.5;

    float2 result = current;
    float frames = 1.0;

    if (p.cameraDelta.w > 0.5 && p.control.x > 0.5)
    {
        // Where the point stood relative to last frame's camera. Offsets only: the level is far enough
        // from the origin that an absolute world position would lose the precision this needs.
        float3 relative = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz;
        float3 prevView = mul(p.prevWorldToView, float4(relative + p.cameraDelta.xyz, 0.0)).xyz;

        if (prevView.z > p.prevUnproject.z)
        {
            float2 prevNdc = float2(
                (prevView.x * p.prevUnproject.x) / prevView.z,
                -((prevView.y * p.prevUnproject.y) / prevView.z));
            float2 prevUv = (prevNdc * 0.5) + 0.5;

            // If the motion field says this surface went somewhere the camera alone would not have taken
            // it, it is a moving object: follow the field instead, and since the object may have turned
            // or come closer, its old depth is no test of whether it is the same surface.
            bool ownMotion = false;
            if (p.control.y > 0.5)
            {
                float2 motion = MotionAt(depthUv);
                float2 disagreement = (motion - (depthUv - prevUv)) * p.resolution.xy;
                if (length(disagreement) > kOwnMotionPixels)
                {
                    prevUv = depthUv - motion;
                    ownMotion = true;
                }
            }

            // Last frame's texels were laid down at last frame's jitter.
            float2 historyUv = prevUv + (p.jitter.zw * p.resolution.zw);

            if (all(historyUv >= 0.0) && all(historyUv <= 1.0))
            {
                // The four texels a bilinear read would blend, each kept only if it is a real sample on
                // the same surface.
                float2 position = (historyUv * p.resolution.xy) - 0.5;
                float2 floored = floor(position);
                float2 fraction = position - floored;
                int2 origin = int2(floored);

                float tolerance = kLampSameSurfaceFraction * max(prevView.z, 1.0);

                float3 weighted = float3(0.0, 0.0, 0.0);
                float weightSum = 0.0;

                OPENIGI_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    int2 corner = int2(i & 1, i >> 1);
                    float2 bilinear = lerp(1.0 - fraction, fraction, float2(corner));
                    float weight = bilinear.x * bilinear.y;

                    float4 texel = HistoryAt(origin + corner);

                    if (!ownMotion && abs(texel.w - prevView.z) > tolerance)
                    {
                        weight = 0.0;
                    }
                    if (texel.y <= kMinHistoryShare || texel.z < 0.5)
                    {
                        weight = 0.0;
                    }

                    weighted += texel.xyz * weight;
                    weightSum += weight;
                }

                if (weightSum > kMinHistoryWeight)
                {
                    float3 history = weighted / weightSum;
                    float historyVis = history.x;
                    float historyShare = history.y;
                    float historyFrames = history.z;

                    if (historyFrames > 0.0)
                    {
                        // Only visibility is clipped; the penumbra width is a geometric estimate that the
                        // filter reads for its radius, and it settles rather than flickers.
                        float clipped = clamp(historyVis, low.x, high.x);
                        float confidence =
                            saturate(1.0 - (abs(clipped - historyVis) / max(extent.x, kMinExtent)));

                        float2 softHistory = float2(lerp(clipped, historyVis, confidence), historyShare);

                        float cap = lerp(kDistrustedFrames, kTrustedFrames, confidence);
                        frames = min(historyFrames + 1.0, cap);
                        result = lerp(softHistory, current, 1.0 / frames);
                    }
                }
            }
        }
    }

    uOutput.Store(px, float4(saturate(result.x), saturate(result.y), frames, viewPos.z));
}
