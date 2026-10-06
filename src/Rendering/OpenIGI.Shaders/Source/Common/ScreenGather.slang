// What the two screen-probe gathers share: the resources they read, the parameters they take, and the
// gather itself.
//
// Both place a probe every few pixels and ask what light arrives at it over the hemisphere its surface
// faces. The only difference between them is what happens afterwards -- one writes the estimate straight
// out, the other pools its workgroup's neighbours and eases onto a history -- so everything up to that
// point is written once, here.
//
// The host must define OPENIGI_RAY_QUERY and include this after ComputeBindings.

#ifndef OPENIGI_SCREEN_GATHER_INCLUDED
#define OPENIGI_SCREEN_GATHER_INCLUDED

OPENIGI_COMPUTE_IMAGE(0) [[vk::image_format("rgba16f")]] WTexture2D<float4> uIndirect;

OPENIGI_COMPUTE_BUFFER(0) StructuredBuffer<float4> uPages;
OPENIGI_COMPUTE_BUFFER(3) RWStructuredBuffer<int2> uCells;
OPENIGI_COMPUTE_BUFFER(4) RWStructuredBuffer<int> uCellPages;

// Each at the unit the host already binds it to.
OPENIGI_COMPUTE_TEXTURE(1) Texture2D<float4> uAlbedo;
OPENIGI_COMPUTE_SAMPLER(1) SamplerState uAlbedoSampler;

OPENIGI_COMPUTE_TEXTURE(4) Texture2D<float4> uDepth;
OPENIGI_COMPUTE_SAMPLER(4) SamplerState uDepthSampler;

OPENIGI_COMPUTE_TEXTURE(5) Texture3D<float4> uClipmaps;
OPENIGI_COMPUTE_SAMPLER(5) SamplerState uClipmapsSampler;

OPENIGI_COMPUTE_TEXTURE(6) Texture2D<float4> uFinal;
OPENIGI_COMPUTE_SAMPLER(6) SamplerState uFinalSampler;

// A depth image, declared as one: WGSL refuses one bound as a float texture. See DepthFilter.
OPENIGI_COMPUTE_TEXTURE(7) DepthTexture2D uSceneDepth;
OPENIGI_COMPUTE_SAMPLER(7) SamplerState uSceneDepthSampler;

OPENIGI_COMPUTE_TEXTURE(8) Texture2D<float4> uSceneNormal;
OPENIGI_COMPUTE_SAMPLER(8) SamplerState uSceneNormalSampler;

OPENIGI_COMPUTE_TEXTURE(11) Texture2D<float4> uSceneColour;
OPENIGI_COMPUTE_SAMPLER(11) SamplerState uSceneColourSampler;

#if OPENIGI_RAY_QUERY
OPENIGI_COMPUTE_SCENE RaytracingAccelerationStructure uScene;

// The world's cut-out masks, and in the globals the address of the hit records naming each surface's
// layer: both set by the device itself, for the trace's alpha test (CutoutHit.slang).
OPENIGI_COMPUTE_TEXTURE(3) Texture2DArray<float4> uCutoutMasks;
#endif

static const int kVectorsPerPageUnused = 0;
#ifndef OPENIGI_GATHER_PIXEL
#define OPENIGI_GATHER_PIXEL 0
#endif

// How a screen-traced hit is read out of last frame's picture: 0 where it lands now, 1 through last frame's
// camera with the probe history's occlusion test (the temporal gather), 2 through last frame's camera alone
// (the pixel gather, which keeps no probe history).
#ifndef OPENIGI_SCREEN_HIT_REPROJECT
#define OPENIGI_SCREEN_HIT_REPROJECT 0
#endif

#if OPENIGI_GATHER_PIXEL
// One ray a pixel, for DLSS ray reconstruction to denoise: the whole cosine hemisphere from a single
// uniform pair, which the pixel draws afresh each frame.
static const int kHemisphereResolution = 1;
static const int kTracesPerProbe = 1;
#else
static const int kHemisphereResolution = 4;
static const int kTracesPerProbe = 16;
#endif
static const float kPi = 3.14159265358979;

bool IsSkyDepth(float depth)
{
    return depth <= 0.0;
}

// The sky in a direction: the frame's own harmonics where it has them, and a flat colour where it does
// not -- a level with no sky bake still has to light its outdoors with something.
float3 SkyRadiance(float3 direction)
{
    if (uSkyShValid == 0)
    {
        return uSkyColour;
    }

    float4 n = float4(direction, 1.0);
    float4 q = n.xyzz * n.yzzx;
    float3 linearPart = float3(dot(uSkySh[0], n), dot(uSkySh[1], n), dot(uSkySh[2], n));
    float3 quadraticPart = float3(dot(uSkySh[3], q), dot(uSkySh[4], q), dot(uSkySh[5], q));
    float3 lastBand = uSkySh[6].xyz * ((direction.x * direction.x) - (direction.y * direction.y));
    return max(float3(0.0, 0.0, 0.0), linearPart + quadraticPart + lastBand);
}

// The albedo assumed for the open ground an escaped ray heading down would have met.
static const float kGroundAlbedo = 0.25;

#define kTraceOrigin uTraceOrigin

#include "DepthFilter.slang"
#include "SoftwareTrace.slang"
// The cache's lighting, for the filtered lookup a marched hit reads (SampleCardsWithin).
#define kCardLighting uFinal
#define kCardLightingSampler uFinalSampler
#include "SurfaceCache.slang"
#include "Hemisphere.slang"

#if !OPENIGI_RAY_QUERY
#include "Shelter.slang"

// The terrain's height, a square of ground to a texel: the distance field holds the level's placements
// and not its terrain, so a ray marched towards the ground passes through it.
OPENIGI_COMPUTE_TEXTURE(14) Texture2D<float4> uTerrainHeights;

ShelterMap TerrainMap()
{
    ShelterMap m;
    m.origin = uTerrainMap0.xy;
    m.invCell = uTerrainMap0.z;
    m.present = uTerrainMap0.w;
    m.size = uTerrainMap1.xy;
    m.lowestZ = uTerrainMap1.z;
    m.scale = uTerrainMap1.w;
    m.headroom = 0.0;
    m.reach = 0.0;
    return m;
}

// The terrain's height under an absolute world point, or far below everything off the raster.
float TerrainHeightAt(ShelterMap m, float2 worldXY)
{
    int2 at = ShelterTexel(m, worldXY);
    return ShelterHolds(m, at) ? ShelterSurface(m, uTerrainHeights.Load(int3(at, 0))) : -1.0e30;
}

OPENIGI_COMPUTE_TEXTURE(15) Texture2D<float4> uRoofs;

// Whether a roof stands over an absolute world point, by the level's roof raster.
bool UnderRoof(float3 worldPoint)
{
    ShelterMap m;
    m.origin = uShelterMap0.xy;
    m.invCell = uShelterMap0.z;
    m.present = uShelterMap0.w;
    m.size = uShelterMap1.xy;
    m.lowestZ = uShelterMap1.z;
    m.scale = uShelterMap1.w;
    m.headroom = uShelterMap2.x;
    m.reach = uShelterMap2.y;
    int2 at = ShelterTexel(m, worldPoint.xy);
    if (!ShelterHolds(m, at))
    {
        return false;
    }

    return ShelterCovers(m, ShelterSurface(m, uRoofs.Load(int3(at, 0))), worldPoint.z);
}

// How far along a ray heading down it meets the terrain, within a reach; -1 for not at all. Stepped at
// half a cell and the crossing then halved down four times, which puts the hit within a few centimetres
// of the raster's own surface.
float TerrainHitDistance(float3 absoluteOrigin, float3 direction, float reach)
{
    ShelterMap m = TerrainMap();
    if (m.present < 0.5 || direction.z > -0.01 || m.invCell <= 0.0)
    {
        return -1.0;
    }

    float stepLength = 0.5 / m.invCell;
    float previous = 0.0;
    for (int i = 1; i <= 64; i++)
    {
        float t = min(float(i) * stepLength, reach);
        float3 at = absoluteOrigin + (direction * t);
        if (at.z <= TerrainHeightAt(m, at.xy))
        {
            float low = previous;
            float high = t;
            for (int refine = 0; refine < 4; refine++)
            {
                float middle = (low + high) * 0.5;
                float3 probe = absoluteOrigin + (direction * middle);
                if (probe.z <= TerrainHeightAt(m, probe.xy))
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            return high;
        }

        if (t >= reach)
        {
            break;
        }

        previous = t;
    }

    return -1.0;
}
#endif

// A vector into the view's own space, which is the transpose of the view-to-world rotation the host
// hands over as three columns.
float3 ToView(float3 world)
{
    return float3(dot(uViewToWorld0, world), dot(uViewToWorld1, world), dot(uViewToWorld2, world));
}

float3 ViewPositionAt(float2 uv, float depth)
{
    float ndcZ = 1.0 - (2.0 * depth);
    float near = uUnproject.z;
    float far = uUnproject.w;
    float viewZ = (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
    float2 ndc = (uv * 2.0) - 1.0;
    float ySign = -1.0;
    return float3((ndc.x * viewZ) / uUnproject.x, ySign * (ndc.y * viewZ) / uUnproject.y, viewZ);
}

float2 ProjectToUv(float3 viewPosition)
{
    float ySign = -1.0;
    float2 ndc = float2(
        (viewPosition.x * uUnproject.x) / viewPosition.z,
        ySign * (viewPosition.y * uUnproject.y) / viewPosition.z);
    return (ndc * 0.5) + 0.5;
}

// One ray's light held under uMaxRayIntensity, scaled as a whole so the brightest channel lands on the
// cap and the colour keeps its hue. A single ray that finds a lamp or a sunlit patch would otherwise
// dominate the sixteen it is averaged with and flicker as the jitter moves it on and off.
float3 CapRayIntensity(float3 radiance)
{
    float brightest = max(radiance.r, max(radiance.g, radiance.b));
    return brightest > uMaxRayIntensity ? radiance * (uMaxRayIntensity / brightest) : radiance;
}

// A march through the depth buffer, tried before anything else: where the camera can already see what a
// ray would have found, the lit frame is this frame's real lighting at full resolution and a card is one
// texel per several centimetres.
bool ScreenTraceRadiance(float3 viewOrigin, float3 viewNormal, float3 viewDir, out float3 radiance)
{
    radiance = float3(0.0, 0.0, 0.0);
    if (uScreenTraceSteps <= 0 || viewDir.z == 0.0)
    {
        return false;
    }

    float3 origin = viewOrigin + (viewNormal * 0.02);
    float3 farPoint = origin + (viewDir * uScreenTraceDistance);

    // Clipped to the near plane rather than dropped: a ray that leaves towards the camera still crosses
    // everything between here and there.
    if (farPoint.z < uUnproject.z + 0.01)
    {
        farPoint = origin + (viewDir * ((uUnproject.z + 0.01 - origin.z) / viewDir.z));
    }

    if (farPoint.z <= uUnproject.z)
    {
        return false;
    }

    float2 startUv = ProjectToUv(origin);
    float2 endUv = ProjectToUv(farPoint);
    float2 travel = endUv - startUv;
    float2 texelSize = 1.0 / float2(float(uScreenWidth), float(uScreenHeight));

    // One step per pixel crossed, up to the budget: a ray that barely moves on screen does not need the
    // whole march, and one that crosses the frame cannot have it.
    float pixels = length(travel / texelSize);
    int used = int(clamp(pixels, 1.0, float(uScreenTraceSteps)));
    float stride = 1.0 / float(used);
#if OPENIGI_SCREEN_HIT_REPROJECT == 1
    // Interleaved gradient noise on the start pixel, advanced every frame, to seed the
    // march: a hash of the position alone gave a still camera the same step offsets for ever, and any
    // sub-pixel move scrambled them, flipping thin hits in and out under a hand on the mouse.
    float2 noisePixel = (startUv * float2(float(uScreenWidth), float(uScreenHeight)))
        + (5.588238 * float(uFrameIndex & 63));
    float jitter = frac(52.9829189 * frac(dot(noisePixel, float2(0.06711056, 0.00583715)))) * stride;
#elif OPENIGI_GATHER_PIXEL
    // White noise on the start pixel and the frame, as every other number the pixel gather draws.
    float jitter = WhiteNoise3(
        int2(startUv * float2(float(uScreenWidth), float(uScreenHeight))), uint(uFrame), 0x7Au).x * stride;
#else
    float jitter = frac(sin(dot(startUv, float2(12.9898, 78.233))) * 43758.5453) * stride;
#endif

    float previous = 0.0;
    float hitFraction = -1.0;

    for (int i = 1; i <= 256; i++)
    {
        if (i > used)
        {
            break;
        }

        float t = (float(i) * stride) + jitter;
        if (t > 1.0)
        {
            break;
        }

        float2 uv = startUv + (travel * t);
        if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)))
        {
            break;
        }

        // Interpolated in reciprocal depth, because that is what is linear across the screen.
        float rayDepth = 1.0 / lerp(1.0 / origin.z, 1.0 / farPoint.z, t);
        float sceneDepth =
            ViewPositionAt(uv, DepthBilinear(uSceneDepth, uSceneDepthSampler, uv)).z;
        float difference = rayDepth - sceneDepth;

        if (difference > 0.0 && difference < uScreenThickness + (sceneDepth * 0.02))
        {
            float low = previous;
            float high = t;
            for (int refine = 0; refine < 4; refine++)
            {
                float mid = (low + high) * 0.5;
                float midDepth = 1.0 / lerp(1.0 / origin.z, 1.0 / farPoint.z, mid);
                float2 midUv = startUv + (travel * mid);
                float midScene =
                    ViewPositionAt(midUv, DepthBilinear(uSceneDepth, uSceneDepthSampler, midUv)).z;
                if (midDepth - midScene > 0.0)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            hitFraction = high;
            break;
        }

        previous = t;
    }

    if (hitFraction <= 0.0)
    {
        return false;
    }

    float2 hitUv = startUv + (travel * hitFraction);
    float3 storedNormal = (uSceneNormal.SampleLevel(uSceneNormalSampler, hitUv, 0.0).xyz * 2.0) - 1.0;
    float3 hitNormal = ToView(storedNormal);

    // The surface has to be facing the ray. A back face is one the ray passed through rather than landed
    // on, and its colour belongs to whatever is on the other side.
    if (dot(hitNormal, viewDir) >= 0.0)
    {
        return false;
    }

#if OPENIGI_SCREEN_HIT_REPROJECT == 1
    // The lit target is LAST frame's picture -- this pass runs before this frame is lit -- so the hit is
    // looked up where it stood last frame. Read at this frame's
    // position instead, every camera move sampled some other surface's light: a turn read tens of pixels
    // off, and next to a lamp that is the fixture's own glare landing on a wall, which the probes then
    // carried for their whole history -- the clouds that followed the view and settled after it.
    float3 hitView = ViewPositionAt(hitUv, DepthBilinear(uSceneDepth, uSceneDepthSampler, hitUv));
    float3 hitWorld = uCameraToCache
        + (uViewToWorld0 * hitView.x)
        + (uViewToWorld1 * hitView.y)
        + (uViewToWorld2 * hitView.z);
    // Through the camera of the frame the picture was RENDERED in -- the last frame -- which is not the
    // frame the probe history belongs to whenever the gather skips frames (r_screen_gi_cadence).
    float3 colourView = (uColourWorldToView0 * (hitWorld.x - uColourCameraToCache.x))
        + (uColourWorldToView1 * (hitWorld.y - uColourCameraToCache.y))
        + (uColourWorldToView2 * (hitWorld.z - uColourCameraToCache.z));
    if (uHasHistory == 0 || colourView.z <= uUnproject.z)
    {
        return false;
    }

    float2 colourUv = ProjectToUv(colourView);
    if (any(colourUv < float2(0.0, 0.0)) || any(colourUv > float2(1.0, 1.0)))
    {
        return false;
    }

    // And the probe that covered the hit in the frame the probe history belongs to.
    float3 prevView = (uPrevWorldToView0 * (hitWorld.x - uPrevCameraToCache.x))
        + (uPrevWorldToView1 * (hitWorld.y - uPrevCameraToCache.y))
        + (uPrevWorldToView2 * (hitWorld.z - uPrevCameraToCache.z));
    if (prevView.z <= uUnproject.z)
    {
        return false;
    }

    float2 probeFrameUv = ProjectToUv(prevView);

    // And it has to have been in view there, not behind something, which last frame's depth would
    // show. Without a depth history, last frame's probe at that spot stands in -- a probe that sat on a
    // different plane means something else covered the hit, and its colour is that something's.
    int2 probeGrid = (int2(uScreenWidth, uScreenHeight) + (uProbeStep - 1)) / uProbeStep;
    // A probe covers the tile of pixels it sits in, the pixel coordinate over the step -- not the uv times
    // the grid, which drifts whenever the frame is not a whole number of tiles across.
    int2 prevProbe = clamp(
        int2(probeFrameUv * float2(float(uScreenWidth), float(uScreenHeight))) / uProbeStep,
        int2(0, 0),
        probeGrid - int2(1, 1));
    float4 prevProbePos = uPositionHistory.Load(int3(prevProbe, 0));
    float3 hitWorldNormal = normalize(
        (uViewToWorld0 * hitNormal.x) + (uViewToWorld1 * hitNormal.y) + (uViewToWorld2 * hitNormal.z));
    float probeSpan = (2.0 * prevView.z * float(uProbeStep)) / (uUnproject.y * float(uScreenHeight));
    if (prevProbePos.w <= 0.0
        || abs(dot((prevProbePos.xyz + uPrevCameraToCache) - hitWorld, hitWorldNormal))
            > max(0.1, 2.0 * probeSpan))
    {
        return false;
    }
#elif OPENIGI_SCREEN_HIT_REPROJECT == 2
    // Last frame's picture, read where the hit stood last frame, as above; with no probe history there is
    // no telling whether something covered it then, and the one frame between makes that the rare case.
    float3 hitView = ViewPositionAt(hitUv, DepthBilinear(uSceneDepth, uSceneDepthSampler, hitUv));
    float3 hitWorld = uCameraToCache
        + (uViewToWorld0 * hitView.x)
        + (uViewToWorld1 * hitView.y)
        + (uViewToWorld2 * hitView.z);
    float3 colourView = (uColourWorldToView0 * (hitWorld.x - uColourCameraToCache.x))
        + (uColourWorldToView1 * (hitWorld.y - uColourCameraToCache.y))
        + (uColourWorldToView2 * (hitWorld.z - uColourCameraToCache.z));
    if (colourView.z <= uUnproject.z)
    {
        return false;
    }

    float2 colourUv = ProjectToUv(colourView);
    if (any(colourUv < float2(0.0, 0.0)) || any(colourUv > float2(1.0, 1.0)))
    {
        return false;
    }
#else
    float2 colourUv = hitUv;
#endif

    // And it has to be part of the world: the lit target's alpha is one for a surface and zero for
    // anything drawn over the picture rather than into it.
    float4 hitColour = uSceneColour.SampleLevel(uSceneColourSampler, colourUv, 0.0);
    if (hitColour.a < 0.5)
    {
        return false;
    }

    radiance = CapRayIntensity(hitColour.rgb);

    return true;
}

#if !OPENIGI_RAY_QUERY
// The surface's normal at a marched hit, as the field's gradient gives it; nought where the field is
// unknown or flat. The hit is left where the march put it: slid down the gradient by the distance the
// field gives there, a hit beside a thin wall in the coarse levels was carried through the wall onto
// whatever stood behind it -- a lamp-lit room's floor -- and the yard outside took that room's light.
float3 SurfaceNormalAt(float3 hit)
{
    bool known;
    float raw;
    float resolved;
    DistanceAt(hit, known, raw, resolved);
    if (!known)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float h = 0.5 * resolved;
    bool k;
    float r;
    float v;
    float3 gradient = float3(
        DistanceAt(hit + float3(h, 0.0, 0.0), k, r, v) - DistanceAt(hit - float3(h, 0.0, 0.0), k, r, v),
        DistanceAt(hit + float3(0.0, h, 0.0), k, r, v) - DistanceAt(hit - float3(0.0, h, 0.0), k, r, v),
        DistanceAt(hit + float3(0.0, 0.0, h), k, r, v) - DistanceAt(hit - float3(0.0, 0.0, h), k, r, v));
    float length2 = dot(gradient, gradient);
    return length2 < 1e-10 ? float3(0.0, 0.0, 0.0) : gradient * rsqrt(length2);
}
#endif

// The light arriving over the hemisphere a surface faces: the screen first, then a ray or a march
// against the world, then the sky on a miss.
float3 GatherIndirect(float3 world, float3 normal, float3 viewOrigin, float2 jitter)
{
    if (uShadowLevels <= 0 || uLookupBuckets <= 0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    HemisphereBasis basis = BasisAround(normal);
    float3 viewNormal = ToView(normal);

    float3 irradiance = float3(0.0, 0.0, 0.0);

    // How many rays this probe's estimate stands on.
    //
    // A ray that struck something the cache cannot place still struck something: it did not reach the sky,
    // and the light it would have brought is the light of a nearby surface, not nothing and not the open
    // sky. Counted black it printed dark blotches into room corners and under desks, where such misses
    // cluster; left out, the estimate became the mean of the rays that DID escape, and a probe beside a
    // wall took almost the open sky's value -- no darkening where a wall meets the ground, every shadow
    // filled. So each such ray takes the mean light of the surfaces the probe's other rays found (or, if
    // none, a dim estimate from the sky), and counts.
    float counted = 0.0;
    float3 surfaceSum = float3(0.0, 0.0, 0.0);
    float surfaceCount = 0.0;
    float3 openSum = float3(0.0, 0.0, 0.0);
    float openCount = 0.0;
    float unplaced = 0.0;
    for (int ray = 0; ray < kTracesPerProbe; ray++)
    {
        // A stratified cosine hemisphere: the sixteen rays cover a four-by-four grid of the square the
        // mapping comes from, so no two land in the same place however the jitter falls.
        float2 probeUv =
            (float2(float(ray % kHemisphereResolution), float(ray / kHemisphereResolution)) + jitter)
            / float(kHemisphereResolution);
        float phi = 6.28318530718 * probeUv.x;
        float cosTheta = sqrt(probeUv.y);
        float sinTheta = sqrt(max(0.0, 1.0 - (cosTheta * cosTheta)));
        float3 local = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
        float3 direction = Orient(basis, local);

        float3 radiance = float3(0.0, 0.0, 0.0);
        bool resolved = false;

        // This ray's share of the estimate: one, less whatever of it the trace could not vouch for.
        float weight = 1.0;

        // Which source answered this ray, for the r_gi_source diagnostic: 1 screen, 2 surface cache, 3 sky.
        int source = 0;

        if (uScreenTraceSteps > 0)
        {
            float3 viewDir = ToView(direction);
            float3 screenRadiance;
            if (ScreenTraceRadiance(viewOrigin, viewNormal, viewDir, screenRadiance))
            {
                radiance = screenRadiance;
                resolved = true;
                source = 1;
            }
        }

        if (!resolved)
        {
            float3 hit;
            bool fieldHit = VisibilityAt(world, normal, direction, hit) <= 0.5;

#if !OPENIGI_RAY_QUERY
            // The terrain, which the field does not hold: a ray heading down that meets it before anything
            // the field found reads the cache's light where it landed -- the pool a lamp throws on the
            // ground, the sunlit apron in front of a wall -- where it was answered with an open-ground guess
            // that knew nothing of either.
            float3 terrainFrom = world + (normal * 0.1);
            float terrainT = TerrainHitDistance(terrainFrom + uTraceOrigin, direction, 32.0);
            if (terrainT >= 0.0 && (!fieldHit || length(hit - world) > terrainT))
            {
                float3 terrainLight;
                if (SampleCardsWithin(
                        terrainFrom + (direction * terrainT),
                        float3(0.0, 0.0, 1.0),
                        0.5 * uClipmapOrigin[0].w,
                        terrainLight))
                {
                    radiance = terrainLight * uBounceScale;
                    source = 2;
                    surfaceSum += CapRayIntensity(radiance);
                    surfaceCount += 1.0;
                    fieldHit = false;
                    resolved = true;
                }
                else
                {
                    // No card holds the ground there: it is still the ground, nearer than whatever the
                    // field found beyond it, and the open-ground answer below is the one to give.
                    fieldHit = false;
                }
            }
#endif

            if (resolved)
            {
            }
            else if (fieldHit)
            {
                float2 found;
                float2 litFound;
                float agreement;
                // Whether the hit is most likely this surface's own skin: only for the march, which starts a
                // voxel and a half out and finds the surface it left within the next two. A traced ray
                // knows exactly what it struck, and a near hit there is a real occluder -- the foot of the
                // wall, the underside of the desk -- which is the contact darkening the gather is for.
#if OPENIGI_RAY_QUERY
                float near = 1.0;
#else
                // And by its height off the surface's own plane, not only its distance: a ray leaving a
                // wall at a grazing angle runs along it and the march finds the same wall again metres on,
                // well past two voxels, and reads the wall's own cached light back -- its stains and seams
                // and all. Every surface took a blurred copy of its own texture as its bounce, which was the
                // cloud over every wall in the gather. A ray leaving a flat surface cannot strike that
                // surface; whatever it finds within a voxel of the plane is the plane.
                float finest = uClipmapOrigin[0].w;
                float near = smoothstep(1.5 * finest, 2.0 * finest, length(hit - world))
                    * smoothstep(0.75 * finest, 1.5 * finest, dot(hit - world, normal));

                float3 hitNormal = SurfaceNormalAt(hit);

                // From out of doors to a surface with a roof over it: the ray went through a wall or a roof,
                // which the field at that distance cannot hold -- a slab a hand thick in voxels of half a
                // metre -- and it brought the room's lamps back out onto the wall outside, and the lit roof
                // of the hall below onto its own roof. Counted as a surface found but not placed, the way a
                // hit nothing can read is. An open doorway is the exception, and the rarer one.
                if (!UnderRoof(world + uTraceOrigin) && UnderRoof(hit + uTraceOrigin))
                {
                    near = 0.0;
                    unplaced += 1.0;
                }

                // And read through every card that faced the surface there, blended by how squarely each
                // looked and filtered within it, rather than the one texel of the one card nearest a point
                // the march only placed to a voxel. A traced hit knows its surface exactly and keeps the
                // single lookup.
                if (dot(hitNormal, direction) > 0.0)
                {
                    hitNormal = -hitNormal;
                }

                float3 filtered;
                if (near <= 0.0)
                {
                    weight = 0.0;
                }
                else if (dot(hitNormal, hitNormal) > 0.5 && SampleCardsWithin(hit, hitNormal, 0.5 * finest, filtered))
                {
                    radiance = filtered * uBounceScale;
                    weight = near;
                    source = 2;
                    surfaceSum += CapRayIntensity(radiance) * near;
                    surfaceCount += near;
                }
                else
#endif
                if (LookupCardHit(hit, direction, found, litFound, agreement))
                {
                    radiance = uFinal.SampleLevel(uFinalSampler, litFound, 0.0).rgb * uBounceScale;
                    weight = near;
                    source = 2;
                    surfaceSum += CapRayIntensity(radiance) * near;
                    surfaceCount += near;
                }
                else
                {
                    // Placed at the end, once the probe knows what its surfaces look like.
                    unplaced += near;
                    weight = 0.0;
                }
            }
            else
            {
                // Escaped. Heading up it met the sky. Heading down it met the ground -- which is not in the
                // field (the terrain is not one of its placements) and lies past its reach soon enough
                // anyway -- so it is the ground it reports: open ground lit by the sun and the sky above
                // it, at a dry earth's albedo. Taken as sky, every ray out of a window and down to a sunlit
                // yard came back the colour of the sky overhead, and the walls facing the windows went blue.
                radiance = direction.z >= 0.0
                    ? SkyRadiance(direction)
                    : kGroundAlbedo * (SkyRadiance(float3(0.0, 0.0, 1.0)) + uGroundSun);
                source = 3;
                openSum += CapRayIntensity(radiance);
                openCount += 1.0;
            }
        }

        if (uDebugSource != 0 && source != uDebugSource)
        {
            radiance = float3(0.0, 0.0, 0.0);
        }

        radiance = CapRayIntensity(radiance);
        irradiance += radiance * weight;
        counted += weight;
    }

    // The hits nothing could place: a surface's light, as the probe's own surfaces show it, or -- where
    // every ray that found anything was unplaced -- what open ground under this sky would send back.
    if (unplaced > 0.0 && (uDebugSource == 0 || uDebugSource == 2))
    {
        float3 fill = surfaceCount > 0.0
            ? surfaceSum / surfaceCount
            : (openCount > 0.0 ? (openSum / openCount) : SkyRadiance(float3(0.0, 0.0, 1.0))) * kGroundAlbedo;
        irradiance += fill * unplaced;
        counted += unplaced;
    }

    // At least a quarter of the rays' worth, so a probe that found almost nothing is not one lucky ray
    // scaled up to the whole hemisphere.
    float norm = max(counted, 0.25 * float(kTracesPerProbe));
    return max(irradiance * (kPi / norm), float3(0.0, 0.0, 0.0));
}

#endif
