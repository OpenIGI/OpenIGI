// One screen probe every few pixels, each asking what light arrives over the hemisphere its surface
// faces. The estimate goes straight out; the temporal form pools its neighbours and accumulates instead.
//
// Compiled for SPIR-V alone in its traced form, which WGSL has no ray tracing for; the software form
// compiles for both.

#include "../Common/ComputeBindings.slang"

OPENIGI_COMPUTE_GLOBALS cbuffer Globals
{
#if OPENIGI_RAY_QUERY
    uint2 uCutoutRecords;
#endif

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

    // The terrain's height raster (TerrainHeightRaster), for the march that cannot trace the ground:
    // its low corner's world XY in metres, cells per metre, whether there is one; its size in cells, the
    // height its packing counts from and the metres one step stands for.
    float4 uTerrainMap0;
    float4 uTerrainMap1;

    // The roof raster (ShelterHeightField), for telling a marched ray that went through a wall: its low
    // corner's world XY, cells per metre, whether there is one; its size, the height its packing counts from
    // and the metres a step stands for; the headroom and reach a roof is a roof within.
    float4 uShelterMap0;
    float4 uShelterMap1;
    float4 uShelterMap2;
};

#include "../Common/ScreenGather.slang"

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 probe = int2(id.xy);
    int2 grid = (int2(uScreenWidth, uScreenHeight) + (uProbeStep - 1)) / uProbeStep;
    if (probe.x >= grid.x || probe.y >= grid.y)
    {
        return;
    }

    // The middle of the block this probe stands for, clamped to the frame: a probe reading the block's
    // corner would sit on whatever edge happened to fall there.
    int2 pixel = min(
        (probe * uProbeStep) + (uProbeStep / 2), int2(uScreenWidth, uScreenHeight) - 1);
    float2 uv = (float2(pixel) + 0.5) / float2(float(uScreenWidth), float(uScreenHeight));

    float depth = DepthBilinear(uSceneDepth, uSceneDepthSampler, uv);
    if (IsSkyDepth(depth))
    {
        uIndirect.Store(probe, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 viewPosition = ViewPositionAt(uv, depth);
    float3 world = uCameraToCache
        + (uViewToWorld0 * viewPosition.x)
        + (uViewToWorld1 * viewPosition.y)
        + (uViewToWorld2 * viewPosition.z);

    float3 storedNormal = (uSceneNormal.SampleLevel(uSceneNormalSampler, uv, 0.0).xyz * 2.0) - 1.0;
    float3 worldNormal = normalize(storedNormal);

    // Turned to face the eye, as the live gather's probes are: see ScreenGatherTemporal's ProbeSurface.
    if (dot(worldNormal, world - uCameraToCache) > 0.0)
    {
        worldNormal = -worldNormal;
    }

    uIndirect.Store(
        probe,
        float4(GatherIndirect(world, worldNormal, viewPosition, float2(0.5, 0.5)), 1.0));
}
