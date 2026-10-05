// The screen probe gather, pooled across its workgroup and accumulated over time.
//
// The gather itself is the shared one. What this adds is everything that makes sixteen rays a frame look
// like a converged estimate: a different jitter each frame so successive frames sample different
// directions, a pool of the workgroup's neighbours that agree about the surface, and a history that is
// reprojected, clamped to that pool and eased onto.

#include "../Common/ComputeBindings.slang"

// The probe's own world position, which the next frame reprojects against.
OPENIGI_COMPUTE_IMAGE(1) [[vk::image_format("rgba16f")]] WTexture2D<float4> uPosition;

// Last frame's estimate and its positions, at the units the host binds them to.
OPENIGI_COMPUTE_TEXTURE(9) Texture2D<float4> uHistory;
OPENIGI_COMPUTE_SAMPLER(9) SamplerState uHistorySampler;

OPENIGI_COMPUTE_TEXTURE(10) Texture2D<float4> uPositionHistory;
OPENIGI_COMPUTE_SAMPLER(10) SamplerState uPositionHistorySampler;

// This frame's raw per-probe estimates (rgb) and whether each probe is real (a): what the first pass
// traced and the second pools. See uGatherPass.
OPENIGI_COMPUTE_TEXTURE(12) Texture2D<float4> uRaw;
OPENIGI_COMPUTE_SAMPLER(12) SamplerState uRawSampler;

// The frame's motion field, moving objects' own stamped over the camera's: unjittered current UV less previous.
// Read only when uHasVelocity says it was made this frame.
OPENIGI_COMPUTE_TEXTURE(13) Texture2D<float4> uVelocity;
OPENIGI_COMPUTE_SAMPLER(13) SamplerState uVelocitySampler;

OPENIGI_COMPUTE_GLOBALS cbuffer Globals
{
#if OPENIGI_RAY_QUERY
    uint2 uCutoutRecords;
#endif

    float4 uClipmapOrigin[4];
    float4 uSkySh[7];

    // x scale, y scale, near, far.
    float4 uUnproject;

    // The same for the frame the history was drawn in. A cinematic zoom moves it every frame.
    float4 uPrevUnproject;

    // the view-to-world rotation, as its three columns.
    float3 uViewToWorld0;
    float3 uViewToWorld1;
    float3 uViewToWorld2;

    // the camera's world position, relative to this pass's origin.
    float3 uCameraToCache;

    // where this pass's origin stands in the world, which the traced form fires from.
    float3 uTraceOrigin;

    // last frame's world-to-view rotation, as its three columns, and where its camera stood.
    float3 uPrevWorldToView0;
    float3 uPrevWorldToView1;
    float3 uPrevWorldToView2;
    float3 uPrevCameraToCache;

    // the frame the lit target was rendered in -- always the last one -- which screen hits are read through.
    float3 uColourWorldToView0;
    float3 uColourWorldToView1;
    float3 uColourWorldToView2;
    float3 uColourCameraToCache;

    float3 uAtlasSize;
    float3 uLookupMin;
    float3 uSkyColour;

    // The sun's light on open ground, as irradiance over pi -- what an escaped ray heading down meets.
    float3 uGroundSun;

    float uClipmapRange[4];

    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;

    int uScreenWidth;
    int uScreenHeight;
    int uProbeStep;

    float uMaxRayIntensity;
    int uScreenTraceSteps;
    float uScreenTraceDistance;
    float uScreenThickness;

    int uSkyShValid;
    float uBounceScale;

    // Diagnostic (r_gi_source): keep only one source of the gather's light -- 0 all, 1 screen-traced
    // hits, 2 surface-cache hits, 3 sky; 4 shows the surface cache's own light at the surface instead.
    int uDebugSource;

    int uHasHistory;
    int uFrameIndex;
    int uMaxAccumulated;

    // Whether uVelocity holds this frame's motion field.
    int uHasVelocity;

    // 0 traces each probe and stores its raw estimate; 1 pools every probe's full neighbourhood of raw
    // estimates and folds the result into the history. Split so that a probe on the edge of a workgroup
    // pools all eight neighbours like any other: in one pass the pool could only see its own group, so the
    // probes on every 64-pixel tile border pooled half as much and were clamped twice as hard.
    int uGatherPass;
};

// Screen hits are read from last frame's picture where they stood last frame; see ScreenTraceRadiance.
#define OPENIGI_SCREEN_HIT_REPROJECT 1
#include "../Common/ScreenGather.slang"

// How squarely two probes must face the same way to be pooled. Below this they are two surfaces meeting,
// and averaging them carries a floor's light onto the wall beside it.
static const float kSameFacing = 0.8;

// Van der Corput in base two, which is the second axis of the Hammersley sequence the jitter walks.
float RadicalInverseBits(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

// Where in its stratum each frame's rays fall. Rotated per probe as well as advanced per frame, so that
// neighbouring probes are not sampling the same directions as each other while they converge.
float2 FrameJitter(int2 probeCoord, int frame)
{
    if (frame <= 0)
    {
        return float2(0.5, 0.5);
    }

    uint seed = (uint(probeCoord.x) * 73856093u) ^ (uint(probeCoord.y) * 19349663u);
    float rotation = float(seed & 0xffffu) / 65536.0;
    uint n = uint(frame & 0xffff);
    float2 point = float2(frac(float(n) * 0.61803398875), RadicalInverseBits(n));
    return frac(point + float2(rotation, frac(rotation * 1.61803398875)));
}

// Where a probe stands and which way its surface faces, from this frame's depth and normals.

// Where in its tile a frame places each probe: one offset shared by the whole screen, walking eight positions
// spread over the tile (a stratified pattern, the second axis bit-reversed). A probe fixed at its tile's
// centre sampled the same surface points every frame, so the grid's own pattern -- a staircase of cells
// along every thin edge, a fitting, a sill -- was printed into the light and no amount of accumulation could
// average it out. Moved every frame, the pattern differs frame to frame and the per-pixel history averages
// it away. Every pass that finds a probe from a pixel asks this for the frame the probe was traced in.
int2 ProbeTileOffset(int frame, int step)
{
    uint i = uint(frame) & 7u;
    uint reversed = ((i & 1u) << 2) | (i & 2u) | ((i >> 2) & 1u);
    float2 at = (float2(float(i), float(reversed)) + 0.5) / 8.0;
    return min(int2(floor(at * float(step))), int2(step - 1, step - 1));
}

bool ProbeSurface(int2 probe, out float3 world, out float3 viewPosition, out float3 worldNormal)
{
    world = float3(0.0, 0.0, 0.0);
    viewPosition = float3(0.0, 0.0, 0.0);
    worldNormal = float3(0.0, 0.0, 1.0);

    int2 pixel = min(
        (probe * uProbeStep) + ProbeTileOffset(uFrameIndex, uProbeStep), int2(uScreenWidth, uScreenHeight) - 1);
    float2 uv = (float2(pixel) + 0.5) / float2(float(uScreenWidth), float(uScreenHeight));
    float depth = DepthBilinear(uSceneDepth, uSceneDepthSampler, uv);
    if (IsSkyDepth(depth))
    {
        return false;
    }

    viewPosition = ViewPositionAt(uv, depth);
    world = uCameraToCache
        + (uViewToWorld0 * viewPosition.x)
        + (uViewToWorld1 * viewPosition.y)
        + (uViewToWorld2 * viewPosition.z);
    float3 storedNormal = (uSceneNormal.SampleLevel(uSceneNormalSampler, uv, 0.0).xyz * 2.0) - 1.0;
    worldNormal = normalize(storedNormal);
    return true;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID, uint3 localId : SV_GroupThreadID)
{
    int2 probe = int2(id.xy);
    int2 grid = (int2(uScreenWidth, uScreenHeight) + (uProbeStep - 1)) / uProbeStep;

    if (probe.x >= grid.x || probe.y >= grid.y)
    {
        return;
    }

    float3 world;
    float3 viewPosition;
    float3 worldNormal;
    bool onSurface = ProbeSurface(probe, world, viewPosition, worldNormal);

    if (uGatherPass == 0)
    {
        float3 traced = float3(0.0, 0.0, 0.0);
        if (onSurface)
        {
            traced = GatherIndirect(world, worldNormal, viewPosition, FrameJitter(probe, uFrameIndex));

            // r_gi_source 4: the surface cache's own stored light at this surface -- what a ray landing
            // here would read -- in place of what arrives at it, and with no history, so the cache's own
            // changes over time show exactly as they happen.
            if (uDebugSource == 4)
            {
                float2 found;
                float2 litFound;
                float agreement;
                traced = LookupCardHit(world, -worldNormal, found, litFound, agreement)
                    ? uFinal.SampleLevel(uFinalSampler, litFound, 0.0).rgb
                    : float3(0.0, 0.0, 0.0);
            }
        }

        uIndirect.Store(probe, float4(traced, onSurface ? 1.0 : 0.0));
        uPosition.Store(probe, onSurface ? float4(world - uCameraToCache, 1.0) : float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    if (!onSurface)
    {
        uIndirect.Store(probe, float4(0.0, 0.0, 0.0, 0.0));
        uPosition.Store(probe, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 current = uRaw.Load(int3(probe, 0)).rgb;

    // How far apart two probes may be and still be the same surface, widened with distance because the
    // probes themselves spread out as the ground recedes.
    float sameSurface = max(0.35, 0.06 * length(viewPosition));

    float3 nMin = current;
    float3 nMax = current;
    float3 nSum = current;
    float nCount = 1.0;

    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0)
            {
                continue;
            }

            int2 n = probe + int2(dx, dy);
            if (any(n < int2(0, 0)) || any(n >= grid))
            {
                continue;
            }

            float4 raw = uRaw.Load(int3(n, 0));
            float3 nWorld;
            float3 nView;
            float3 nNormal;
            if (raw.a > 0.5
                && ProbeSurface(n, nWorld, nView, nNormal)
                && distance(nWorld, world) < sameSurface
                && dot(nNormal, worldNormal) > kSameFacing)
            {
                nMin = min(nMin, raw.rgb);
                nMax = max(nMax, raw.rgb);
                nSum += raw.rgb;
                nCount += 1.0;
            }
        }
    }

    float3 est = nSum / nCount;
    float3 accumulated = est;
    float frames = 0.0;

    if (uHasHistory != 0)
    {
        float3 prevView = (uPrevWorldToView0 * (world.x - uPrevCameraToCache.x))
            + (uPrevWorldToView1 * (world.y - uPrevCameraToCache.y))
            + (uPrevWorldToView2 * (world.z - uPrevCameraToCache.z));

        if (prevView.z > 0.0)
        {
            float prevYSign = -1.0;
            float2 prevNdc = float2(
                (prevView.x * uPrevUnproject.x) / prevView.z,
                prevYSign * (prevView.y * uPrevUnproject.y) / prevView.z);
            float2 prevUv = (prevNdc * 0.5) + 0.5;

            // A car or a door moved on its own as well as with the camera. Where the motion field disagrees
            // with the camera by more than the jitter could explain, the surface came from where the field
            // says; the plane test below would only find the road it left, so it is not asked.
            bool ownMotion = false;
            if (uHasVelocity != 0)
            {
                int2 probePixel = min(
                    (probe * uProbeStep) + ProbeTileOffset(uFrameIndex, uProbeStep), int2(uScreenWidth, uScreenHeight) - 1);
                float2 screen = float2(float(uScreenWidth), float(uScreenHeight));
                float2 probeUv = (float2(probePixel) + 0.5) / screen;
                float2 motion = uVelocity.SampleLevel(uVelocitySampler, probeUv, 0.0).xy;
                float2 apart = (motion - (probeUv - prevUv)) * screen;
                if (dot(apart, apart) > 2.25)
                {
                    prevUv = probeUv - motion;
                    ownMotion = true;
                }
            }

            if (all(prevUv >= float2(0.0, 0.0)) && all(prevUv <= float2(1.0, 1.0)))
            {
                float2 gridF = float2(grid);
                // Probe k sits at the centre of pixel k*step + step/2, as the gather places it; inverting
                // that, not uv*grid - 0.5, which is a sixteenth of a probe off at 1080p and more whenever the
                // frame is not a whole number of tiles -- an offset the resampling compounded every frame.
                // The history's probes stood where the frame before placed them.
                float2 prevOffset = float2(ProbeTileOffset(uFrameIndex - 1, uProbeStep));
                float2 tc = ((prevUv * float2(float(uScreenWidth), float(uScreenHeight)))
                    - prevOffset - 0.5) / float(uProbeStep);
                float2 tcBase = floor(tc);
                float2 tcFrac = tc - tcBase;

                // How much world one probe cell covers here, which is what the plane test below is
                // allowed to be wrong by.
                float cellSpan = (2.0 * viewPosition.z * float(uProbeStep))
                    / (uUnproject.y * float(uScreenHeight));

                float3 historySum = float3(0.0, 0.0, 0.0);
                float frameSum = 0.0;
                float weightSum = 0.0;

                for (int t = 0; t < 4; t++)
                {
                    int2 tap = int2(tcBase) + int2(t & 1, t >> 1);
                    if (any(tap < int2(0, 0)) || any(tap >= grid))
                    {
                        continue;
                    }

                    float4 tapPos = uPositionHistory.Load(int3(tap, 0));
                    if (tapPos.w <= 0.0)
                    {
                        continue;
                    }

                    // Judged by distance from this surface's PLANE rather than by depth: a wall seen at a
                    // glancing angle has a large depth gradient and no discontinuity at all.
                    float3 tapWorld = tapPos.xyz + uPrevCameraToCache;
                    float3 offsetFromSurface = tapWorld - world;
                    float planeDistance = abs(dot(offsetFromSurface, worldNormal));
                    if (!ownMotion && planeDistance > max(0.05, 0.5 * cellSpan))
                    {
                        continue;
                    }

                    float w = ((t & 1) == 0 ? 1.0 - tcFrac.x : tcFrac.x)
                        * ((t >> 1) == 0 ? 1.0 - tcFrac.y : tcFrac.y);
                    float4 tapHistory = uHistory.Load(int3(tap, 0));
                    historySum += tapHistory.rgb * w;
                    frameSum += tapHistory.a * w;
                    weightSum += w;
                }

                if (weightSum > 0.01)
                {
                    float3 history = historySum / weightSum;

                    // Clamped into the pool's own range, widened by a quarter of its centre so that a
                    // converged history is not dragged back to the noise of one frame's sixteen rays.
                    float3 windowCentre = 0.5 * (nMin + nMax);
                    float3 windowHalf = (0.5 * (nMax - nMin)) + (0.25 * windowCentre) + 0.001;
                    float3 clampedHistory = clamp(
                        history,
                        windowCentre - (2.0 * windowHalf),
                        windowCentre + (2.0 * windowHalf));

                    frames = min((frameSum / weightSum) + 1.0, float(uMaxAccumulated));
                    float alpha = 1.0 / (1.0 + frames);
                    accumulated = lerp(clampedHistory, est, alpha);
                }
            }
        }
    }

    if (uDebugSource == 4)
    {
        accumulated = current;
        frames = 1.0;
    }

    uIndirect.Store(probe, float4(accumulated, frames));
    uPosition.Store(probe, float4(world - uCameraToCache, 1.0));
}
