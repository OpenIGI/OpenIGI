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

OPENIGI_COMPUTE_GLOBALS cbuffer Globals
{
    float4 uClipmapOrigin[4];
    float4 uSkySh[7];

    // x scale, y scale, near, far.
    float4 uUnproject;

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

    float3 uAtlasSize;
    float3 uLookupMin;
    float3 uSkyColour;

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

    int uHasHistory;
    int uFrameIndex;
    int uMaxAccumulated;
};

#include "../Common/ScreenGather.slang"

// The workgroup's own probes, so a probe can pool its neighbours without reading them back out of an
// image none of them has finished writing.
groupshared float3 sLight[64];
groupshared float sValid[64];
groupshared float3 sWorld[64];
groupshared float3 sNormal[64];

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

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID, uint3 localId : SV_GroupThreadID)
{
    int2 probe = int2(id.xy);
    int2 grid = (int2(uScreenWidth, uScreenHeight) + (uProbeStep - 1)) / uProbeStep;
    uint lid = (localId.y * 8u) + localId.x;

    // Out-of-grid threads still take part: they have to reach the barrier below, and they publish a zero
    // validity, which is the truth about a probe that is not there.
    bool inGrid = probe.x < grid.x && probe.y < grid.y;

    int2 pixel = min(
        (probe * uProbeStep) + (uProbeStep / 2), int2(uScreenWidth, uScreenHeight) - 1);
    float2 uv = (float2(pixel) + 0.5) / float2(float(uScreenWidth), float(uScreenHeight));

    float depth = inGrid ? uSceneDepth.SampleLevel(uSceneDepthSampler, uv, 0.0).r : 0.0;

    float3 world = float3(0.0, 0.0, 0.0);
    float3 viewPosition = float3(0.0, 0.0, 0.0);
    float3 current = float3(0.0, 0.0, 0.0);
    float3 worldNormal = float3(0.0, 0.0, 1.0);
    float valid = 0.0;

    if (inGrid && !IsSkyDepth(depth))
    {
        viewPosition = ViewPositionAt(uv, depth);
        world = uCameraToCache
            + (uViewToWorld0 * viewPosition.x)
            + (uViewToWorld1 * viewPosition.y)
            + (uViewToWorld2 * viewPosition.z);

        float3 storedNormal = (uSceneNormal.SampleLevel(uSceneNormalSampler, uv, 0.0).xyz * 2.0) - 1.0;
        worldNormal = normalize(storedNormal);
        current = GatherIndirect(world, worldNormal, viewPosition, FrameJitter(probe, uFrameIndex));
        valid = 1.0;
    }

    sLight[lid] = current;
    sValid[lid] = valid;
    sWorld[lid] = world;
    sNormal[lid] = worldNormal;

    GroupMemoryBarrierWithGroupSync();

    if (!inGrid)
    {
        return;
    }

    if (valid < 0.5)
    {
        uIndirect.Store(probe, float4(0.0, 0.0, 0.0, 0.0));
        uPosition.Store(probe, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // How far apart two probes may be and still be the same surface, widened with distance because the
    // probes themselves spread out as the ground recedes.
    float sameSurface = max(0.35, 0.06 * length(viewPosition));

    float3 nMin = current;
    float3 nMax = current;
    float3 nSum = current;
    float nCount = 1.0;
    int2 lxy = int2(localId.xy);

    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0)
            {
                continue;
            }

            int2 n = lxy + int2(dx, dy);
            if (n.x < 0 || n.x >= 8 || n.y < 0 || n.y >= 8)
            {
                continue;
            }

            uint nid = (uint(n.y) * 8u) + uint(n.x);
            if (sValid[nid] > 0.5
                && distance(sWorld[nid], world) < sameSurface
                && dot(sNormal[nid], worldNormal) > kSameFacing)
            {
                nMin = min(nMin, sLight[nid]);
                nMax = max(nMax, sLight[nid]);
                nSum += sLight[nid];
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
                (prevView.x * uUnproject.x) / prevView.z,
                prevYSign * (prevView.y * uUnproject.y) / prevView.z);
            float2 prevUv = (prevNdc * 0.5) + 0.5;

            if (all(prevUv >= float2(0.0, 0.0)) && all(prevUv <= float2(1.0, 1.0)))
            {
                float2 gridF = float2(grid);
                float2 tc = (prevUv * gridF) - 0.5;
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
                    if (planeDistance > max(0.05, 0.5 * cellSpan))
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

    uIndirect.Store(probe, float4(accumulated, frames));
    uPosition.Store(probe, float4(world - uCameraToCache, 1.0));
}
