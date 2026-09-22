// The world-space radiance cache: a grid of probes, each holding the light arriving from every direction
// as four spherical-harmonic coefficients per colour, plus how far the rays got.
//
// One slab of Z per frame, so the whole grid is refreshed over several frames rather than all at once,
// and each probe eases onto its new estimate rather than replacing it.
//
// Compiled for SPIR-V alone, both forms. The easing reads each probe volume and writes it back in the
// same dispatch, and WGSL does not allow read_write access to an rgba16float storage texture at all -- a
// browser form has to ping-pong between two grids, which is a change to what the host allocates rather
// than to this shader. The traced form also traces, which WGSL has no form for either.

#include "../Common/ComputeBindings.slang"

OPENIGI_COMPUTE_BUFFER(0) StructuredBuffer<float4> uPages;
OPENIGI_COMPUTE_BUFFER(3) RWStructuredBuffer<int2> uCells;
OPENIGI_COMPUTE_BUFFER(4) RWStructuredBuffer<int> uCellPages;

OPENIGI_COMPUTE_IMAGE(0) [[vk::image_format("rgba16f")]] RWTexture3D<float4> uCacheRed;
OPENIGI_COMPUTE_IMAGE(1) [[vk::image_format("rgba16f")]] RWTexture3D<float4> uCacheGreen;
OPENIGI_COMPUTE_IMAGE(2) [[vk::image_format("rgba16f")]] RWTexture3D<float4> uCacheBlue;
OPENIGI_COMPUTE_IMAGE(3) [[vk::image_format("rgba16f")]] RWTexture3D<float4> uCacheDistance;

// Each at the unit the host already binds it to. A GL default-block sampler had no slot of its own and
// took one from an int of the same name; a module declares the slot outright, so that int is gone and
// with it the only thing in the seam that needed two calls to bind one texture.
OPENIGI_COMPUTE_TEXTURE(1) Texture2D<float4> uAlbedo;
OPENIGI_COMPUTE_SAMPLER(1) SamplerState uAlbedoSampler;

OPENIGI_COMPUTE_TEXTURE(4) Texture2D<float4> uDepth;
OPENIGI_COMPUTE_SAMPLER(4) SamplerState uDepthSampler;

OPENIGI_COMPUTE_TEXTURE(5) Texture3D<float4> uClipmaps;
OPENIGI_COMPUTE_SAMPLER(5) SamplerState uClipmapsSampler;

OPENIGI_COMPUTE_TEXTURE(6) Texture2D<float4> uFinal;
OPENIGI_COMPUTE_SAMPLER(6) SamplerState uFinalSampler;

#if OPENIGI_RAY_QUERY
OPENIGI_COMPUTE_SCENE RaytracingAccelerationStructure uScene;
#endif

OPENIGI_COMPUTE_GLOBALS cbuffer Globals
{
    float4 uClipmapOrigin[4];
    float4 uSkySh[7];

    float3 uAtlasSize;
    float3 uLookupMin;
    float3 uSkyColour;

    // probe (0,0,0), relative to this pass's origin.
    float3 uCacheOrigin;

    // probe counts per axis, as floats.
    float3 uCacheCount;

    // where this pass's origin stands in the world, which the traced form fires from.
    float3 uTraceOrigin;

    float uClipmapRange[4];

    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;

    int uSkyShValid;
    float uBounceScale;

    // metres between neighbouring probes.
    float uCacheSpacing;

    // this frame's Z slab, [begin, end).
    int uSlabBegin;
    int uSlabEnd;

    // how much of the new estimate to take; 1 while seeding.
    float uAlpha;

    int uRayCount;

    // completed sweeps so far, to offset the ray set each sweep.
    int uSweep;

    // calibration on the stored radiance, so the bounce can be dialled.
    float uCacheScale;

    // escape fraction under which a probe's sky reads as a wall leak.
    float uSkyReject;
};

// A cbuffer rather than a named block, so its members are in scope unqualified -- which is what lets the
// shared march and the shared card lookup name them directly, exactly as they did when these were loose
// uniforms. A named block would need a define per parameter, and a define whose right-hand side repeats
// its own name expands into itself.
#define kTraceOrigin uTraceOrigin

#include "../Common/SoftwareTrace.slang"
#include "../Common/SurfaceCache.slang"

static const float kPi = 3.14159265358979;

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
    float3 linearPart = float3(
        dot(uSkySh[0], n), dot(uSkySh[1], n), dot(uSkySh[2], n));
    float3 quadraticPart = float3(
        dot(uSkySh[3], q), dot(uSkySh[4], q), dot(uSkySh[5], q));
    float3 lastBand = uSkySh[6].xyz
        * ((direction.x * direction.x) - (direction.y * direction.y));
    return max(float3(0.0, 0.0, 0.0), linearPart + quadraticPart + lastBand);
}

// A Fibonacci sphere, offset by the sweep so each pass fires a different set and the estimate converges
// rather than repeating the same directions for ever.
float3 SphereDirection(int i, int count, float jitter)
{
    float k = float(i) + jitter;
    float cosTheta = clamp(1.0 - (((2.0 * k) + 1.0) / float(count)), -1.0, 1.0);
    float sinTheta = sqrt(max(0.0, 1.0 - (cosTheta * cosTheta)));
    float phi = 6.28318530718 * k * 0.61803398875;
    return float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
}

[shader("compute")]
[numthreads(4, 4, 4)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int3 count = int3(uCacheCount + 0.5);
    int3 probe = int3(int(id.x), int(id.y), uSlabBegin + int(id.z));

    if (probe.x >= count.x || probe.y >= count.y || probe.z >= uSlabEnd || probe.z >= count.z)
    {
        return;
    }

    if (uShadowLevels <= 0 || uLookupBuckets <= 0)
    {
        return;
    }

    float3 world = uCacheOrigin + (float3(probe) * uCacheSpacing);
    float jitter = frac(float(uSweep) * 0.61803398875);
    float reach = uCacheSpacing * float(max(count.x, max(count.y, count.z)));

    float3 c0 = float3(0.0, 0.0, 0.0);
    float3 c1 = float3(0.0, 0.0, 0.0);
    float3 c2 = float3(0.0, 0.0, 0.0);
    float3 c3 = float3(0.0, 0.0, 0.0);
    float distSum = 0.0;
    float distSqSum = 0.0;

    // The sky's own contribution, kept apart until the end: a probe inside a wall can see sky through a
    // gap the field leaves at a seam, and taking that at face value floods the room.
    float3 s0 = float3(0.0, 0.0, 0.0);
    float3 s1 = float3(0.0, 0.0, 0.0);
    float3 s2 = float3(0.0, 0.0, 0.0);
    float3 s3 = float3(0.0, 0.0, 0.0);
    float escaped = 0.0;

    for (int ray = 0; ray < uRayCount; ray++)
    {
        float3 dir = SphereDirection(ray, uRayCount, jitter);
        float3 radiance;
        float dist;
        float3 hit;
        bool sky = false;

        if (VisibilityAt(world, dir, dir, hit) <= 0.5)
        {
            float2 found;
            float2 litFound;
            float agreement;
            dist = length(hit - world);
            radiance = LookupCard(hit, found, litFound, agreement)
                ? uFinal.SampleLevel(uFinalSampler, litFound, 0.0).rgb * uBounceScale
                : float3(0.0, 0.0, 0.0);
        }
        else
        {
            radiance = SkyRadiance(dir);
            dist = reach;
            sky = true;
            escaped += 1.0;
        }

        radiance = min(radiance, float3(8.0, 8.0, 8.0));

        if (sky)
        {
            s0 += 0.282095 * radiance;
            s1 += (0.488603 * dir.x) * radiance;
            s2 += (0.488603 * dir.y) * radiance;
            s3 += (0.488603 * dir.z) * radiance;
        }
        else
        {
            c0 += 0.282095 * radiance;
            c1 += (0.488603 * dir.x) * radiance;
            c2 += (0.488603 * dir.y) * radiance;
            c3 += (0.488603 * dir.z) * radiance;
        }

        distSum += dist;
        distSqSum += dist * dist;
    }

    // How much of the sky this probe saw to believe. A probe out of doors escapes on most of its rays;
    // one inside a room that escapes on a handful is leaking through a seam, and its sky is dropped
    // rather than lighting the room from nowhere.
    float skyTrust = uSkyReject <= 0.0
        ? 1.0
        : smoothstep(uSkyReject * 0.33, uSkyReject, escaped / float(uRayCount));

    c0 += s0 * skyTrust;
    c1 += s1 * skyTrust;
    c2 += s2 * skyTrust;
    c3 += s3 * skyTrust;

    float scale = ((4.0 * kPi) / float(uRayCount)) * uCacheScale;
    c0 *= scale;
    c1 *= scale;
    c2 *= scale;
    c3 *= scale;

    float invN = 1.0 / float(uRayCount);
    float4 red = float4(c0.r, c1.r, c2.r, c3.r);
    float4 green = float4(c0.g, c1.g, c2.g, c3.g);
    float4 blue = float4(c0.b, c1.b, c2.b, c3.b);

    // The mean distance and its square, which is what a Chebyshev test downstream needs to reject a probe
    // on the wrong side of a wall.
    float4 moments = float4(distSum * invN, distSqSum * invN, 0.0, 0.0);

    if (uAlpha < 1.0)
    {
        red = lerp(uCacheRed[probe], red, uAlpha);
        green = lerp(uCacheGreen[probe], green, uAlpha);
        blue = lerp(uCacheBlue[probe], blue, uAlpha);
        moments = lerp(uCacheDistance[probe], moments, uAlpha);
    }

    uCacheRed[probe] = red;
    uCacheGreen[probe] = green;
    uCacheBlue[probe] = blue;
    uCacheDistance[probe] = moments;
}
