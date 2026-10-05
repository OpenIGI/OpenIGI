// The lamp visibility trace: how much of a pixel's lamp light actually reaches it.
//
// A many-lights sampling stage. A level's lamps number in the
// hundreds and a pixel can sit within reach of dozens, so shadowing every one is out of the question.
// Instead each pixel draws a few lamps in proportion to how much each is worth there -- a streaming
// weighted reservoir -- and traces one ray at each. Drawn that way, the plain average of those rays'
// visibilities is an unbiased estimate of the RATIO the pixel needs: its shadowed lamp light over its
// unshadowed lamp light. The shading computes the unshadowed light analytically, at full resolution and
// for every lamp, and multiplies by the denoised ratio -- the ratio estimator of Heitz et al., "Combining
// Analytic Direct Illumination and Stochastic Shadows" (2018).
//
// What that buys over shading with raw rays is everything that makes a shadow look finished. The ratio
// is smooth wherever the lamps agree, so it survives the temporal and spatial denoisers behind this pass
// without losing its edges, and the only noise that reaches the frame is what they could not remove.
// Rays fired inside the shading instead handed the temporal resolve a new random verdict at every pixel
// of every frame, and its neighbourhood clamp fought them rather than averaging them: shadows that
// crawled.
//
// Two forms. The default traces each ray against the world's acceleration structure, and is compiled for
// SPIR-V alone. The software form (OPENIGI_LAMP_FIELD) is for an adapter with no ray tracing -- WebGPU,
// and so every browser -- and asks the same question without hardware: a short march through the
// depth buffer first, for the occluders the camera can see in detail, then the level's global distance
// field from where that stopped, for the walls it cannot. Everything around the ray -- the reservoirs,
// the points on the source, the penumbra, both denoisers -- is shared. Without it such an adapter had no
// lamp shadow at all: every lamp lit through every wall in reach, and a night ran several times brighter
// than on a renderer that traces.

#ifndef OPENIGI_LAMP_FIELD
#define OPENIGI_LAMP_FIELD 0
#endif

#include "../Common/ViewDepth.slang"
#if !OPENIGI_LAMP_FIELD
#include "../Common/HitRecords.slang"
#include "../Common/CutoutHit.slang"
#endif

#ifndef OPENIGI_MAX_LAMPS
#error "OPENIGI_MAX_LAMPS must be defined by the shader build."
#endif

#if OPENIGI_LAMP_FIELD
// Declared as the depth image it is: WGSL will not read one bound as a float texture. See DepthFilter.
#include "../Common/DepthFilter.slang"
[[vk::binding(0, 0)]] DepthTexture2D uDepth;
#define LAMP_DEPTH_AT(uv) DepthBilinear(uDepth, uDepthSampler, (uv))
#else
[[vk::binding(0, 0)]] Texture2D<float4> uDepth;
#define LAMP_DEPTH_AT(uv) uDepth.SampleLevel(uDepthSampler, (uv), 0.0).r
#endif
[[vk::binding(1, 0)]] SamplerState uDepthSampler;

[[vk::binding(2, 0)]] Texture2D<float4> uNormal;
[[vk::binding(3, 0)]] SamplerState uNormalSampler;

#if OPENIGI_LAMP_FIELD
// The level's distance field clipmaps, every level stacked along z, as the gather reads them.
[[vk::binding(4, 0)]] Texture3D<float4> uClipmaps;
[[vk::binding(6, 0)]] SamplerState uClipmapsSampler;
#else
[[vk::binding(4, 0)]] RaytracingAccelerationStructure uScene;
#endif

[[vk::binding(5, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uVisibility;

// The sun's traced visibility this frame, at the same resolution, for the skip below: how much of the sun
// a pixel already has decides whether the lamps' share of its light is enough to show a shadow in.
// Where the sun was not traced p.sunLight.w is nought and this is never read.
[[vk::binding(10, 0)]] Texture2D<float4> uSunVisibility;

#if !OPENIGI_LAMP_FIELD
// The world's cut-out masks, for the rays' alpha test (CutoutHit.slang).
[[vk::binding(6, 0)]] Texture2DArray<float4> uMasks;
[[vk::binding(7, 0)]] SamplerState uMasksSampler;
#endif

// The share of the sunlight on a surface that lamp light must reach before its shadow is traced there.
static const float kInvisibleShareOfSun = 0.01;

struct Params
{
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height -- of the target, at the frame's resolution (see
    // LampShadowParameters.ResolutionDivisor).
    float4 resolution;

    // xyz the camera's world position, w the frame index.
    float4 cameraFrame;

    // xyz the world direction towards the sun, w its luminance as the lighting pass applies it; nought
    // where the sun was not traced this frame, which turns the relative skip off.
    float4 sunLight;

    // The lamps, laid out exactly as the shading pass lays them out, so the two cannot disagree
    // about which lamp is which.

    // count, whether they shadow, frame index, _.
    float4 lampControl;

    // largest source a ray samples, the hit records' address as a bit pattern (low word, high word), and
    // the summed lamp light below which a shadow here could not be seen at this exposure.
    float4 lampSource;

    // xyz view position, w reach.
    float4 lampPositionRadius[OPENIGI_MAX_LAMPS];

    // rgb colour, a strength.
    float4 lampColourIntensity[OPENIGI_MAX_LAMPS];

    // xyz view-space facing, w source size.
    float4 lampDirection[OPENIGI_MAX_LAMPS];

    // xyz the emitting rectangle's long axis at half its length (view space), w its half width; zero for
    // a fixture lit as a point.
    float4 lampRect[OPENIGI_MAX_LAMPS];
};

[[vk::binding(8, 0)]] ConstantBuffer<Params> p;

#if OPENIGI_LAMP_FIELD
// Where the distance field stands, in a block of its own so the traced form's layout is untouched.
struct FieldParams
{
    // Each level's low corner relative to the CAMERA, and its voxel size in w.
    float4 origin[4];

    // Each level's encoded distance range.
    float4 range;

    // level count, resolution per level, whether the march reports its own verdicts (the lampmarch
    // debug view), _.
    int4 levels;

    // The nearest lamps' shadow maps: each map's camera-relative world to clip, and which map each lamp
    // reads (-1 for none), four lamps to an int4.
    float4x4 mapMatrix[240];

    // Each map's own projection depth terms, two maps to a float4: ndc depth = a + b / view depth. Not read
    // out of the matrices above, whose third row is the projection's mixed with the light's rotation -- the
    // two terms only for a map looking straight along the vertical, and nonsense for one looking sideways.
    float4 mapDepth[120];
    int4 mapSlot[40];
};

[[vk::binding(9, 0)]] ConstantBuffer<FieldParams> f;
[[vk::binding(11, 0)]] DepthTexture2DArray uLampMaps;
[[vk::binding(12, 0)]] SamplerComparisonState uLampMapSampler;

// Every lamp's coarse cube of maps (the surface cache's), for the lamps the fine maps above do not reach:
// six records a lamp in the frame's light order, each four rows: the face's camera-relative world-to-clip
// first, second and fourth rows, then its two depth terms, one when drawn, and its layer of the array.
// Four rather than five so all 160 lamps' fit the 64 KB a constant buffer may hold.
[[vk::binding(13, 0)]] DepthTexture2DArray uCoarseMaps;

struct CoarseParams
{
    float4 record[OPENIGI_MAX_LAMPS * 6 * 4];
};

[[vk::binding(14, 0)]] ConstantBuffer<CoarseParams> c;

// How much of a lamp reaches a camera-relative point by its coarse cube, or -1 where it has none. A tent
// over sixteen texels and a normal offset of a texel and a half: the maps are sixty-four texels a face, and
// a surface the lamp sees edge-on crosses several of their depths within one.
float CoarseLampVisibility(int lamp, float3 cameraRelative, float3 normal)
{
    if (lamp < 0 || lamp >= OPENIGI_MAX_LAMPS)
    {
        return -1.0;
    }

    int first = lamp * 6;
    if (c.record[(first * 4) + 3].z <= 0.0)
    {
        return -1.0;
    }

    uint width;
    uint height;
    uint layers;
    uCoarseMaps.GetDimensions(width, height, layers);

    float pointZ = 0.25;
    for (int face = 0; face < 6; face++)
    {
        float4 forward = c.record[((first + face) * 4) + 2];
        pointZ = max(pointZ, dot(forward.xyz, cameraRelative) + forward.w);
    }

    float texelAtPoint = pointZ * 2.0 / float(max(width, 1u));
    float3 at = cameraRelative + (normal * (0.05 + (1.5 * texelAtPoint)));

    int slot = first;
    float deepest = -1e30;
    for (int face = 0; face < 6; face++)
    {
        float4 forward = c.record[((first + face) * 4) + 2];
        float depth = dot(forward.xyz, at) + forward.w;
        if (depth > deepest)
        {
            deepest = depth;
            slot = first + face;
        }
    }

    float4 terms = c.record[(slot * 4) + 3];
    if (terms.z <= 0.0)
    {
        return -1.0;
    }

    float4 row0 = c.record[slot * 4];
    float4 row1 = c.record[(slot * 4) + 1];
    float viewZ = deepest;
    float nearPlane = terms.y / (-1.0 - terms.x);
    if (viewZ <= nearPlane)
    {
        return 1.0;
    }

    float2 ndc = float2(dot(row0.xyz, at) + row0.w, dot(row1.xyz, at) + row1.w) / viewZ;
    float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));
    int2 size = int2(int(width), int(height));
    float texelMetres = viewZ * (2.0 / max(length(row0.xyz), 1e-3)) / float(max(width, 1u));
    float bias = 0.05 + (0.005 * viewZ) + (2.0 * texelMetres);
    int layer = int(terms.w + 0.5);

    float2 texelAt = (uv * float2(size)) - 0.5;
    int2 corner = int2(floor(texelAt)) - 1;
    float2 blend = texelAt - floor(texelAt);
    float lit = 0.0;
    float weightSum = 0.0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            int2 texel = clamp(corner + int2(x, y), int2(0, 0), size - 1);
            float stored = uCoarseMaps.Load(int4(texel, layer, 0));
            float storedZ = terms.y / min(((stored * 2.0) - 1.0) - terms.x, -1e-6);
            float2 offset = abs(float2(float(x) - 1.0, float(y) - 1.0) - blend);
            float2 tent = saturate(1.0 - (offset / 2.0));
            float weight = tent.x * tent.y;
            lit += storedZ >= viewZ - bias ? weight : 0.0;
            weightSum += weight;
        }
    }

    return lit / max(weightSum, 1e-4);
}

// How much of a lamp reaches a camera-relative point by the lamp's own shadow map, filtered over a texel
// either way; or -1 where the lamp has no map or the point is outside it, for the field to answer instead.
// The bias is taken in metres along the lamp's view -- a few centimetres and a hundredth of the distance --
// and only then turned into the map's depth, which a perspective map spends unevenly.
// A map's projection depth terms: ndc depth = x + y / view depth.
float2 LampMapDepthTerms(int slot)
{
    float4 pair = f.mapDepth[slot >> 1];
    return (slot & 1) != 0 ? pair.zw : pair.xy;
}

// A map texel's stored depth as a distance along the lamp's view, through the map's own projection.
float LampMapViewDepth(float stored, float2 terms)
{
    float ndcZ = (stored * 2.0) - 1.0;
    return terms.y / min(ndcZ - terms.x, -1e-6);
}

// A view depth as the map stores it, through the map's own projection.
float LampMapDeviceDepth(float viewZ, float2 terms)
{
    return ((terms.x + (terms.y / max(viewZ, 1e-4))) * 0.5) + 0.5;
}

// The steepest the receiver's plane is followed away from the receiver: depth change per metre across.
static const float kLampMapMaxSlope = 2.0;

// Where the receiver's plane crosses the line of sight through a map texel, as a view depth, for a texel
// `across` metres to the side of the receiver. The plane is followed only as far as a slope of two in one:
// past that it is nearly edge-on to the lamp, and the crossing runs off to anywhere. A recessed wall under a
// slab is edge-on to the lamp above it; followed all the way, half the filter's taps put the wall in front
// of the slab that shadows it, read lit, and the wall glowed where the traced shadow had it wholly dark.
// Held to a slope instead, a grazing receiver keeps its own depth, as a shadow map's slope-scaled bias is
// capped for the same reason.
float LampMapPlaneDepth(float2 uv, float scale, float3 normalView, float planeOffset, float viewZ, float across)
{
    float2 ndcAt = float2((uv.x * 2.0) - 1.0, 1.0 - (uv.y * 2.0));
    float3 ray = float3(ndcAt / scale, 1.0);
    float facing = dot(normalView, ray);
    float z = abs(facing) > 1e-4 ? planeOffset / facing : viewZ;
    float reach = kLampMapMaxSlope * across;
    return clamp(z, viewZ - reach, viewZ + reach);
}

float LampMapVisibility(int lamp, float3 cameraRelative, float3 receiverNormal, float sourceWidth)
{
    int entry = f.mapSlot[lamp >> 2][lamp & 3];
    if (entry < 0)
    {
        return -1.0;
    }

    int slot = entry & 0xFF;
    float4x4 m = f.mapMatrix[slot];
    float4 clip = mul(m, float4(cameraRelative, 1.0));
    float3 ndc = float3(0.0, 0.0, 0.0);

    if ((entry & 0x100) != 0)
    {
        // A lamp that faces nowhere holds a cube of six ninety-degree maps round it; the point is in exactly
        // one of their views, the one along whichever axis it lies furthest out on.
        bool found = false;
        for (int face = 0; face < 6 && !found; face++)
        {
            float4x4 faceMatrix = f.mapMatrix[slot + face];
            float4 faceClip = mul(faceMatrix, float4(cameraRelative, 1.0));
            if (faceClip.w > 1e-4)
            {
                float3 faceNdc = faceClip.xyz / faceClip.w;
                if (all(abs(faceNdc.xy) <= float2(1.0, 1.0)))
                {
                    found = true;
                    slot += face;
                    m = faceMatrix;
                    clip = faceClip;
                    ndc = faceNdc;
                }
            }
        }

        if (!found)
        {
            return -1.0;
        }

        // Inside the near plane is inside the lamp's own housing, which is not drawn: nothing there to cast.
        if (ndc.z < -1.0)
        {
            return 1.0;
        }
    }
    else
    {
        // Behind the glass: a ceiling round a batten, the wall a lamp hangs on. The fitting's own aim gives
        // such a point none of its light, so nothing here can be taken away; and the field, asked instead,
        // finds the housing in the way at its resolution and blacked out every ceiling under its lights.
        // The map's near plane, out of its own projection: the camera stands behind the glass and nothing
        // nearer than just past it is drawn.
        float2 nearTerms = LampMapDepthTerms(slot);
        float mapNear = nearTerms.y / (-1.0 - nearTerms.x);
        if (clip.w <= mapNear)
        {
            return 1.0;
        }

        ndc = clip.xyz / clip.w;

        // Past the edge of the map but still in front of the glass: the band between seventy-five degrees
        // off the lamp's axis and its own plane, which a ceiling batten's light only grazes -- the top of a
        // wall beside it. Nothing stands between a fitting and the light that skims off its own face except
        // the fitting, and the field, asked instead, found exactly that housing at its quarter-metre
        // resolution and drew a black band along every wall under the ceiling. Only that close, though:
        // further off, past the edge of a lamp's view is as likely the far side of a wall from it -- a lamp
        // on the outside of a building, facing away -- and answering lit there let its light through the
        // wall in a broad band beside the part its map rightly held dark. Those go to the field instead.
        if (any(abs(ndc.xy) > float2(0.98, 0.98)))
        {
            float lateralScale = max(length(float3(m[0][0], m[0][1], m[0][2])), 1e-3);
            float3 fromLamp = float3(clip.xy / lateralScale, clip.w - mapNear);
            return dot(fromLamp, fromLamp) < (1.5 * 1.5) ? 1.0 : -1.0;
        }
    }

    float viewZ = clip.w;
    float scale = max(length(float3(m[0][0], m[0][1], m[0][2])), 1e-3);
    float2 terms = LampMapDepthTerms(slot);
    float nearPlane = terms.y / (-1.0 - terms.x);
    float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));

    uint width;
    uint height;
    uint layers;
    uLampMaps.GetDimensions(width, height, layers);
    float texel = 1.0 / float(max(width, 1u));
    int2 size = int2(int(width), int(height));

    // The receiver's own plane, in the map's view: its point, and its normal rotated into the light's
    // frame (the matrix's rows are the light's axes scaled by the projection; its w row is the forward
    // axis unscaled). Every comparison below is made against where THIS plane crosses the tap's own line
    // of sight -- a receiver-plane depth bias. Compared against the centre's depth instead, a filter tap a
    // dozen texels out on a wall the lamp sees edge-on stood metres further away than the centre and the
    // wall shadowed itself: the dark patch on the wall under every floodlight, where the traced shadow had
    // it lit. A constant bias on top then only has to cover the map's own depth quantisation.
    float3 pointView = float3(ndc.x * viewZ / scale, ndc.y * viewZ / scale, viewZ);
    float3 normalView = float3(
        dot(float3(m[0][0], m[0][1], m[0][2]), receiverNormal) / scale,
        dot(float3(m[1][0], m[1][1], m[1][2]), receiverNormal) / scale,
        dot(float3(m[3][0], m[3][1], m[3][2]), receiverNormal));
    float planeOffset = dot(normalView, pointView);
    float texelMetres = viewZ * (2.0 / scale) / float(max(width, 1u));

    float constantBias = 0.015 + (0.003 * viewZ) + (1.5 * texelMetres);

    // The fitting is not a point: a batten is a metre and a half of glass, and the traced shadow aims its
    // rays across all of it, so an edge a long way from its occluder goes soft. The filter is sized as an
    // area light's penumbra would be (percentage-closer soft shadows): the occluders near the receiver's
    // line to the lamp are found and their mean distance taken, then the shadow is filtered over the
    // light's width times how far the receiver lies past them, as similar triangles give it.
    //
    // In a perspective map that width shrinks with the receiver's own distance: the line from the receiver
    // to the edge of a source L wide crosses depth z at 0.5 * L * (zR - z) / (z * zR) map units off the
    // receiver's own texel (L in map units at a metre's distance). Left without the receiver's distance, the
    // filter six metres from a floodlight spread six times too wide, reached past the edge of the slab over a
    // recessed wall into the open ground beyond it, and lit a wall the traced shadow had wholly dark.
    float frustumAtUnit = 2.0 / scale;
    float lightUv = sourceWidth / frustumAtUnit;

    // Searched wide enough to find what stands anywhere in the nearer three quarters of the way to the lamp.
    float searchFrom = max(viewZ * 0.25, nearPlane + 0.01);
    float searchUv = clamp(
        lightUv * 0.5 * max(viewZ - searchFrom, 0.0) / (searchFrom * viewZ), 2.0 * texel, 32.0 * texel);

    float blockerSum = 0.0;
    float blockers = 0.0;
    for (int by = -2; by <= 2; by++)
    {
        for (int bx = -2; bx <= 2; bx++)
        {
            float2 at = uv + (float2(float(bx), float(by)) * (searchUv * 0.5));
            int2 texelAt = clamp(int2(at * float2(size)), int2(0, 0), size - 1);
            float stored = uLampMaps.Load(int4(texelAt, slot, 0));
            float receiverZ = LampMapPlaneDepth(
                at, scale, normalView, planeOffset, viewZ, length(at - uv) * frustumAtUnit * viewZ);
            float limit = LampMapDeviceDepth(max(receiverZ - constantBias, nearPlane + 0.005), terms);
            if (stored < limit)
            {
                blockerSum += LampMapViewDepth(stored, terms);
                blockers += 1.0;
            }
        }
    }

    // Nothing in the way anywhere near: lit, with no filtering to do.
    if (blockers <= 0.0)
    {
        return 1.0;
    }

    float blockerZ = max(blockerSum / blockers, 0.05);
    float penumbraUv = lightUv * max(viewZ - blockerZ, 0.0) / (blockerZ * viewZ);
    float radius = clamp(penumbraUv * 0.5, 1.0 * texel, 24.0 * texel);

    // Twelve points over a disc of that radius, turned per pixel so the pattern does not print.
    float angle = frac(52.9829189 * frac(dot(uv * float2(size), float2(0.06711056, 0.00583715)))) * 6.2831853;
    float s = sin(angle);
    float c = cos(angle);
    const float2 kDisc[12] =
    {
        float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
        float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
        float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
    };

    float sum = 0.0;
    for (int i = 0; i < 12; i++)
    {
        float2 offset = float2((kDisc[i].x * c) - (kDisc[i].y * s), (kDisc[i].x * s) + (kDisc[i].y * c)) * radius;
        float2 at = uv + offset;
        float receiverZ = LampMapPlaneDepth(
            at, scale, normalView, planeOffset, viewZ, length(offset) * frustumAtUnit * viewZ);
        float compare = LampMapDeviceDepth(max(receiverZ - constantBias, nearPlane + 0.005), terms);
        sum += uLampMaps.SampleCmpLevelZero(uLampMapSampler, float3(at, float(slot)), compare);
    }

    return sum / 12.0;
}

#define uClipmapOrigin f.origin
#define uClipmapRange f.range
#define uShadowLevels f.levels.x
#define uShadowResolution f.levels.y
#include "../Common/DistanceField.slang"

// One level's reading at a point the level holds.
float LampLevelDistance(int level, float3 voxel)
{
    float slice = (float(level) * float(uShadowResolution)) + voxel.z;
    float3 uvw = float3(
        voxel.x / float(uShadowResolution),
        voxel.y / float(uShadowResolution),
        slice / float(uShadowResolution * uShadowLevels));
    float raw = uClipmaps.SampleLevel(uClipmapsSampler, uvw, 0.0).r;
    return ((raw * 2.0) - 1.0) * uClipmapRange[level];
}

// How many voxels from a level's edge its answer starts handing over to the next level out.
static const float kLampLevelBlend = 8.0;

// The field at a point, as DistanceAt reads it, but handed smoothly from each level to the next across the
// outer voxels of the finer one. DistanceAt answers from the finest level holding the point and nothing
// else, and the levels follow the camera: a wall a few metres off went from a quarter-metre voxel to a
// half-metre one the moment a step of the player's put it past the fine level's edge, and its lamp
// shadow -- the thin ones, the near-wall ones -- jumped with it. That was lamp light that changed with
// where the player stood and shimmered while they walked, and held still when they stopped. Each level is
// snapped to the world, so a point's reading in either level is fixed; only the share between them moves,
// and it moves gradually.
float LampDistanceAt(float3 world, out bool known, out float resolved)
{
    known = false;
    resolved = uClipmapOrigin[0].w;

    for (int level = 0; level < uShadowLevels; level++)
    {
        float4 corner = uClipmapOrigin[level];
        float3 voxel = (world - corner.xyz) / corner.w;
        float top = float(uShadowResolution) - 0.5;
        if (any(voxel < float3(0.5, 0.5, 0.5)) || any(voxel > float3(top, top, top)))
        {
            continue;
        }

        known = true;
        float here = LampLevelDistance(level, voxel);
        resolved = corner.w;

        float3 fromEdge = min(voxel - 0.5, top - voxel);
        float edge = min(fromEdge.x, min(fromEdge.y, fromEdge.z));
        float handOver = saturate(1.0 - (edge / kLampLevelBlend));
        if (handOver <= 0.0 || level + 1 >= uShadowLevels)
        {
            return here;
        }

        float4 outer = uClipmapOrigin[level + 1];
        float3 outerVoxel = (world - outer.xyz) / outer.w;
        if (any(outerVoxel < float3(0.5, 0.5, 0.5)) || any(outerVoxel > float3(top, top, top)))
        {
            return here;
        }

        resolved = lerp(corner.w, outer.w, handOver);
        return lerp(here, LampLevelDistance(level + 1, outerVoxel), handOver);
    }

    return 1e6;
}

// The depth buffer is trusted for the first metre only: beyond that a step lands on a pixel whose surface
// may be anything, and the field takes over. Sixteen steps keep each one a few centimetres long.
static const float kScreenReach = 1.0;
static const int kScreenSteps = 16;

float2 ProjectToUv(float3 viewPosition)
{
    float2 ndc = float2(
        (viewPosition.x * p.unproject.x) / viewPosition.z,
        (-viewPosition.y * p.unproject.y) / viewPosition.z);
    return (ndc * 0.5) + 0.5;
}

// Whether anything stands between a point and a point on a lamp, and how far along if so. Positions are
// relative to the camera, as the field's corners are.
//
// receiverLift is how far the ray's start stands off the receiving surface and receiverRise how fast the
// ray climbs away from it (the cosine to its normal); lampDistance is the whole length to the point on the
// lamp and lampRise how squarely the lamp's face looks back down the ray -- negative for a ray reaching it
// from behind the glass, nought for a lamp with no face.
// Together they say how far each sample is from the receiver's own plane and from the plane the lamp is
// mounted on -- see the field's hit test for why that matters.
bool SoftwareBlocked(
    float3 from,
    float3 direction,
    float segment,
    float receiverLift,
    float receiverRise,
    float lampDistance,
    float lampRise,
    out float hitT,
    inout float nearMiss)
{
    hitT = 0.0;

    // The depth buffer, stepped evenly out to kScreenReach. A step that lands behind a surface by less
    // than that surface's thickness has passed through it.
    float screenEnd = min(segment, kScreenReach);
    float screenT = 0.0;
    for (int i = 1; i <= kScreenSteps; i++)
    {
        float t = screenEnd * (float(i) / float(kScreenSteps));
        float3 view = mul(float4(from + (direction * t), 0.0), p.viewToWorld).xyz;
        if (view.z <= p.unproject.z)
        {
            break;
        }

        float2 uv = ProjectToUv(view);
        if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)))
        {
            break;
        }

        float depth = LAMP_DEPTH_AT(uv);
        if (depth > 0.0)
        {
            float sceneZ = ViewDepthAt(depth, p.unproject);
            float behind = view.z - sceneZ;
            // Behind by more than two centimetres, so the receiver's own surface is not mistaken for an
            // occluder, and by less than a thickness that grows with distance as depth precision falls:
            // further behind than that the step has passed BEHIND the surface rather than through it.
            if (behind > 0.02 && behind < max(0.05, 0.01 * sceneZ))
            {
                hitT = t;
                return true;
            }
        }

        screenT = t;
    }

    // Then the field, resumed where the screen left off, with half a voxel of bias at each end: the field
    // blurs a surface by about that much, so a ray starting on one, or ending on the fitting, would find it.
    if (uShadowLevels <= 0)
    {
        return false;
    }

    float finest = uClipmapOrigin[0].w;
    float travelled = max(screenT, 0.5 * finest);
    float startT = travelled;
    float end = segment - (0.5 * finest);

    // A receiver the field has swallowed -- a door leaf in its frame, a fitting against its ceiling, both
    // thinner than a voxel -- starts the ray INSIDE the field's solid, and the first sample would call that
    // a hit: the office door read as permanently in shadow. So the ray walks out of whatever solid it starts
    // in before it may be stopped by anything, as far as a voxel and a half of the level it is in.
    bool leaving = true;
    float leaveBy = 0.0;

    // What the field reads at the start, where the nearest surface is the receiver's own: the field's
    // error there -- it can read a point a few centimetres under a ceiling as inside it -- is the same
    // error it makes along the receiver's plane further on, so the plane test below is measured from this
    // reading rather than from the true lift.
    bool startKnown;
    float startResolved;
    float startDistance = LampDistanceAt(from + (direction * travelled), startKnown, startResolved);
    float startTravelled = travelled;

    // And at the end, where the nearest surface is the lamp's own fitting and whatever it is mounted on:
    // the same calibration for the lamp's plane, so a fitting is told from a slab hanging in front of it.
    bool endKnown;
    float endResolved;
    float endDistance = LampDistanceAt(from + (direction * end), endKnown, endResolved);
    for (int step = 0; step < 64 && travelled < end; step++)
    {
        bool known;
        float resolved;
        float distance = LampDistanceAt(from + (direction * travelled), known, resolved);
        if (!known)
        {
            return false;
        }

        if (leaving)
        {
            if (leaveBy <= 0.0)
            {
                leaveBy = travelled + (1.5 * resolved);
            }

            if (distance <= 0.0 && travelled < leaveBy)
            {
                travelled += resolved * 0.25;
                continue;
            }

            leaving = false;
        }

        // A hit once the ray comes within a capture radius of a surface: nothing at the start, so a ray
        // leaving a surface does not catch it again, growing to half a voxel over the first two voxels so
        // a slab a short way off is still caught though the march steps over its thin middle.
        //
        // And only a surface nearer the ray than the receiver's own plane and the lamp's mounting plane. A
        // ray from a ceiling to the batten screwed to it runs a few centimetres under the ceiling all the
        // way, and the field -- a quarter of a metre a voxel at its finest -- calls that touching: every
        // ceiling under its own lights came out black, a desk top thinner than a voxel lost its lamp, and
        // the corners blotched. A sample whose nearest surface is no closer than those planes is only
        // seeing them; something really in the way stands closer to the ray than either.
        float hitRadius = (0.5 * resolved) * saturate(travelled / (2.0 * resolved));
        // A quarter of a voxel past the receiver's own reading. The plane is measured from what the field
        // said at the start, which already carries the field's error about it; a wider margin let a slab
        // standing out of the receiver's own wall -- the canopy under a row of wall lamps -- pass as part
        // of the wall, and the whole wall under it lit.
        float margin = 0.25 * resolved;
        float aboveReceiver = startKnown
            ? startDistance + ((travelled - startTravelled) * receiverRise)
            : receiverLift + (travelled * receiverRise);
        float toLamp = lampDistance - travelled;
        float belowLamp = lampRise <= 0.0 ? 1e9
            : endKnown ? endDistance + ((end - travelled) * lampRise)
            : toLamp * lampRise;

        // The last voxel and a half before the lamp is the fitting itself -- its housing, at the field's
        // resolution -- for a ray that reaches it from BEHIND the glass, as one from the ceiling it hangs on
        // does: that is not shadowed by the lamp it is going to. Any ray in front of the glass keeps every
        // hit, however glancing: a wall lamp facing out over a canopy is reached almost edge-on by a ray
        // climbing the wall below, and the canopy in the way is what keeps that wall dark.
        bool atFitting = lampRise <= 0.0 && toLamp < (1.5 * resolved);
        bool touching = distance < hitRadius;
        if (touching
            && (atFitting || distance >= aboveReceiver - margin || distance >= belowLamp - (0.25 * resolved)))
        {
            // Something within the capture radius that a plane test or the fitting let through; counted for
            // the lampmarch debug view, which is how a leak past a thin slab is told from a slab the field
            // never held at all.
            nearMiss = 1.0;
        }

        if (!atFitting
            && touching
            && distance < aboveReceiver - margin
            && distance < belowLamp - (0.25 * resolved))
        {
            hitT = travelled;
            return true;
        }

        travelled += max(distance, resolved * 0.25);
    }

    return false;
}
#endif

// A pixel's own random numbers: a hash of its coordinates, fixed for the pixel, advanced every frame
// along a low-discrepancy sequence (the golden ratio for one number, the R2 constants for two): no
// structure across the screen, and well spread over the frames a pixel accumulates. The interleaved
// gradient noise it replaces is a pattern of diagonal lines; at rest the accumulation hid it, and on the
// move -- history partly rejected -- it came through as diagonal hatching across every lamp-lit floor.
uint3 Pcg3d(uint3 v)
{
    v = (v * 1664525u) + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

float3 PixelRandom(int2 px)
{
    return float3(Pcg3d(uint3(uint2(px), 0x9E3779B9u)) >> 8u) * (1.0 / 16777216.0);
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

    // Nothing is written for a pixel with no lamps: the shading reads a full visibility there and
    // the pass costs it nothing.
    int lampCount = int(p.lampControl.x + 0.5);

    if (lampCount <= 0)
    {
        uVisibility.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    // The centre of the frame pixels this texel stands for: exactly one, at the frame's resolution.
    float2 uv = (float2(px) + 0.5) * p.resolution.zw;
    float depth = LAMP_DEPTH_AT(uv);

    // Reverse-Z clears to zero: no geometry here, so nothing to shadow.
    if (depth <= 0.0)
    {
        uVisibility.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 viewPos = ViewPositionAt(uv, depth, p.unproject);
    float3 normal = normalize((uNormal.SampleLevel(uNormalSampler, uv, 0.0).xyz * 2.0) - 1.0);

    // Absolute world, which is the space the acceleration structure is built in. Everything else
    // here is camera-relative; leaving the ray that way puts it forty-two kilometres from the level,
    // where it misses everything and the pass silently reports no shadows at all.
    float3 worldPos = mul(p.viewToWorld, float4(viewPos, 1.0)).xyz + p.cameraFrame.xyz;

    // Off the surface along its own normal, by at least eight centimetres and more with distance. Some of
    // this game's surfaces carry a second, back-facing skin a few centimetres in front of the one the
    // rasteriser draws -- a door leaf is two single-sided quads -- and a ray started inside that gap
    // strikes the skin from behind: the office door read as permanently shadowed at two centimetres.
    float originLift = 0.08 + (0.0005 * viewPos.z);
    float3 origin = worldPos + (normal * originLift);

    // Wrapped so the low-discrepancy steps below keep their precision in a float.
    float frame = float(uint(p.cameraFrame.w) % 65536u);
    float3 pixelRandom = PixelRandom(px);
#if !OPENIGI_LAMP_FIELD
    uint2 records = uint2(asuint(p.lampSource.y), asuint(p.lampSource.z));
#endif

    // ---- Which lamps to trace: two weighted reservoirs over one pass ---------------------------
    //
    // Each lamp is weighed by the light it would bring here unshadowed -- its windowed falloff, its cone,
    // the cosine at the surface and its brightness -- and each reservoir keeps one lamp, chosen with
    // probability proportional to that weight (Chao 1982; the streaming form of Bitterli et al. 2020).
    // A reservoir spends a single uniform on the whole stream: when lamp i arrives it takes it if the
    // uniform falls below w_i / sum, and either way the uniform is stretched back over [0,1) from the
    // part of the interval it fell in, so it is still uniform for the next decision. The two reservoirs'
    // uniforms sit half a period apart, which stratifies the pair: they never both fall on the same
    // narrow lamp by chance.
    const int kSamples = 2;
    int chosen[kSamples];
    float pick[kSamples];
    float u = frac(pixelRandom.x + (frame * 0.6180339887));
    for (int k = 0; k < kSamples; k++)
    {
        chosen[k] = -1;
        pick[k] = frac(u + (float(k) / float(kSamples)));
    }

    float weightSum = 0.0;
    for (int i = 0; i < lampCount; i++)
    {
        float4 positionRadius = p.lampPositionRadius[i];
        float3 offset = positionRadius.xyz - viewPos;
        float distanceSquared = dot(offset, offset);
        float reachSquared = positionRadius.w * positionRadius.w;
        if (distanceSquared >= reachSquared)
        {
            continue;
        }

        float3 toLamp = normalize(mul(p.viewToWorld, float4(offset, 0.0)).xyz);
        float nDotL = dot(normal, toLamp);
        if (nDotL <= 0.0)
        {
            continue;
        }

        // Inverse square, softened at the source and windowed to reach exactly zero at the lamp's reach.
        float falloff = saturate(1.0 - ((distanceSquared / reachSquared) * (distanceSquared / reachSquared)));
        float atten = (falloff * falloff) / (distanceSquared + 1.0);

        float3 facing = p.lampDirection[i].xyz;
        if (dot(facing, facing) > 0.25)
        {
            float3 axis = normalize(mul(p.viewToWorld, float4(facing, 0.0)).xyz);
            atten *= max(dot(axis, -toLamp), 0.0);
        }

        float4 colourIntensity = p.lampColourIntensity[i];
        float brightness = dot(colourIntensity.rgb * colourIntensity.a, float3(0.2126, 0.7152, 0.0722));
        float weight = atten * nDotL * brightness;
        if (weight <= 1e-6)
        {
            continue;
        }

        weightSum += weight;
        float take = weight / max(weightSum, 1e-6);
        for (int k = 0; k < kSamples; k++)
        {
            if (pick[k] < take)
            {
                chosen[k] = i;
                pick[k] = pick[k] / max(take, 1e-6);
            }
            else
            {
                pick[k] = (pick[k] - take) / max(1.0 - take, 1e-6);
            }

            // Rounding can carry the stretched uniform to one; keep it inside the interval.
            pick[k] = min(pick[k], 0.99999994);
        }
    }

    // No lamp reaches this pixel -- or what reaches it could not show a shadow: too little at the exposure the
    // frame is seen at, or too small a share of the sunlight already on it. The weights are the lighting
    // pass's own lamp term, so this is all the light a shadow here could take away; against a sunlit
    // surface a hundredth of its light is below what the eye tells apart. Only sunlight the sun's own
    // trace found reaching the pixel counts, so a room, a shaded wall and every night keep their lamps'
    // shadows exactly as they were.
    float sunlit = 0.0;
    if (p.sunLight.w > 0.0 && weightSum > 0.0)
    {
        float sunVisibility = saturate(uSunVisibility.Load(int3(px, 0)).r);
        sunlit = p.sunLight.w * max(dot(normal, p.sunLight.xyz), 0.0) * sunVisibility;
    }

    if (weightSum <= 0.0 || weightSum < p.lampSource.w || weightSum < (kInvisibleShareOfSun * sunlit))
    {
        // No lamp reaches this pixel, so there is nothing for a shadow to be cast by.
        uVisibility.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    // ---- One ray per reservoir, at the lamp it kept ------------------------------------------
    //
    // Aimed at a point on the source rather than its centre, so the shadow has a penumbra that widens
    // with the gap between occluder and receiver. Each reservoir takes its own point, spread over the
    // source by the R2 sequence so they cover it evenly, and the whole pattern moves with the frame.
    float visibleSum = 0.0;

    // The penumbra each blocked ray implies, summed: how wide the soft edge is where this pixel sits, in
    // metres on the receiver. Sizing the reconstruction from the closest occluder is what turns a couple
    // of binary rays into a smooth soft edge rather than a grain.
    float penumbraSum = 0.0;
    float blocked = 0.0;
    float nearMissSum = 0.0;

    // For the lampmaps debug view: the rays a lamp's map held lit, and those left to the field.
    float mappedLitSum = 0.0;
    float fieldRays = 0.0;

    for (int k = 0; k < kSamples; k++)
    {
        int lamp = chosen[k];
        float4 positionRadius = p.lampPositionRadius[lamp];
        float3 lampCentre = mul(p.viewToWorld, float4(positionRadius.xyz, 1.0)).xyz + p.cameraFrame.xyz;
        float3 toLamp = lampCentre - origin;
        float3 lampDir = normalize(toLamp);

        // Capped, because the size this starts from bounds the whole clustered fitting rather than the
        // glass that emits.
        float source = clamp(p.lampDirection[lamp].w, 0.02, p.lampSource.x);

        // The points are on the fitting's EMITTING FACE, the disc square to the way it faces, and a
        // couple of centimetres in front of its glass. Not on a disc square to the ray: for a wall just
        // under a ceiling batten that disc stands upright, half its points lie above the ceiling, and half
        // the rays struck the ceiling on their way -- a different half every frame, which was the speckle
        // along every wall-ceiling corner. A point lamp has no face, and keeps the disc square to the ray.
        float3 axis = lampDir;
        float3 emitting = float3(0.0, 0.0, 0.0);
        float4 facing = p.lampDirection[lamp];
        if (dot(facing.xyz, facing.xyz) > 0.25)
        {
            axis = normalize(mul(p.viewToWorld, float4(facing.xyz, 0.0)).xyz);
            emitting = axis * 0.02;
        }

        // Each reservoir its own point, the pair spread by the R2 step so they cover the source evenly.
        float2 rand = frac(
            pixelRandom.yz
            + ((frame * float(kSamples) + float(k)) * float2(0.7548776662, 0.5698402910)));

        float3 target;
        float4 rect = p.lampRect[lamp];
        if (dot(emitting, emitting) > 0.0 && rect.w > 0.0 && dot(rect.xyz, rect.xyz) > 0.0)
        {
            // A fitting with a measured face: a point on THAT rectangle, uniform over its area. The glass
            // is what emits,
            // so every ray ends on it -- not on a disc hanging half in the air beside a batten, whose far
            // points a grazing wall reached past the housing on some frames and not others: the black
            // speckle under every ceiling and beside every lit panel.
            float3 halfLength = mul(p.viewToWorld, float4(rect.xyz, 0.0)).xyz;
            float3 across = normalize(cross(axis, halfLength)) * rect.w;
            float2 uv = (rand * 2.0) - 1.0;
            target = lampCentre + emitting + (halfLength * uv.x) + (across * uv.y);
        }
        else
        {
            float3 up = abs(axis.z) < 0.9 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
            float3 across = normalize(cross(up, axis));
            float3 along = cross(axis, across);

            float spread = sqrt(rand.x) * source;
            float around = 6.28318530718 * rand.y;
            target = lampCentre + emitting
                + (across * (spread * cos(around))) + (along * (spread * sin(around)));
        }

        float3 toTarget = target - origin;
        float distance = length(toTarget);

        // Stop short of the fitting: past it is whatever the lamp is mounted on, which would stop every
        // ray in the room, and just short of it is the fitting's own glass.
        // A point on the measured glass needs only to miss the glass itself; the old disc stood in for the
        // whole fitting and had to stop short of all of it.
        bool onRect = dot(emitting, emitting) > 0.0 && rect.w > 0.0 && dot(rect.xyz, rect.xyz) > 0.0;
        float stopShort = onRect ? 0.05 : clamp(source * 0.5, 0.05, 0.2);
        if (distance <= stopShort + 0.02)
        {
            visibleSum += 1.0;
            continue;
        }

        bool hit;
        float t;
        float nearMiss = 0.0;
#if OPENIGI_LAMP_FIELD
        // A lamp with a map of its own is answered by it: the geometry as drawn, thin slabs and all.
        float mapped = LampMapVisibility(
            lamp,
            origin - p.cameraFrame.xyz,
            normal,
            onRect ? (length(rect.xyz) + rect.w) : (2.0 * source));
        // Past the fine maps' budget, the lamp's coarse cube: the field cannot hold a wall at a distance its
        // voxels are a metre or two, and a far lamp lit straight through the building it stood behind.
        if (mapped < 0.0)
        {
            mapped = CoarseLampVisibility(lamp, origin - p.cameraFrame.xyz, normal);
        }

        if (mapped >= 0.0)
        {
            mappedLitSum += mapped;
            visibleSum += mapped;
            if (mapped < 1.0)
            {
                blocked += 1.0 - mapped;
                penumbraSum += (1.0 - mapped) * 0.05;
            }

            continue;
        }

        // Camera-relative, which is the space the field's corners are handed over in.
        fieldRays += 1.0;
        float3 rayDirection = toTarget / distance;
        hit = SoftwareBlocked(
            origin - p.cameraFrame.xyz,
            rayDirection,
            distance - stopShort,
            originLift,
            max(dot(rayDirection, normal), 0.0),
            distance,
            dot(emitting, emitting) > 0.0 ? dot(-rayDirection, axis) : 0.0,
            t,
            nearMiss);
        nearMissSum += nearMiss;
#else
        RayDesc ray;
        ray.Origin = origin;
        ray.TMin = 0.0;
        ray.Direction = toTarget / distance;
        ray.TMax = distance - stopShort;

        // Cut-out surfaces tested against their masks: the solid part of a wall stops the ray and a
        // window cut into it lets it through. Forcing them opaque shut every window; ignoring them let
        // every such wall leak.
        RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;

        // Everything but the first-person rig, for the reason the sun's trace excludes it.
        query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
        ProceedWithCutouts(query, records, uMasks);

        hit = query.CommittedStatus() != COMMITTED_NOTHING;
        t = hit ? query.CommittedRayT() : 0.0;
#endif

        if (!hit)
        {
            visibleSum += 1.0;
        }
        else
        {
            // Similar triangles: a source of width L, an occluder t from the receiver and the source D
            // away casts a penumbra L * t / (D - t) wide on the receiver.
            float sourceWidth = onRect ? (length(rect.xyz) + rect.w) : (2.0 * source);
            penumbraSum += sourceWidth * t / max(distance - t, 0.05);
            blocked += 1.0;
        }
    }

    // The ratio estimate, and in the second channel the penumbra width the blocked rays imply -- never
    // nought, since a positive second channel is also what says there were lamps to estimate over. The
    // denoisers behind this pass smooth the first and size their kernel from the second; the shading
    // multiplies every lamp's unshadowed light by the result.
    float penumbra = blocked > 0.0 ? clamp(penumbraSum / blocked, 0.002, 1.0) : 0.002;
#if OPENIGI_LAMP_FIELD
    if (f.levels.z == 2)
    {
        // The lampmaps debug view: the share of rays a map held lit, and the share left to the field.
        uVisibility.Store(px, float4(mappedLitSum / float(kSamples), 0.002 + (fieldRays / float(kSamples)), 0.0, 0.0));
        return;
    }

    if (f.levels.z != 0)
    {
        // The lampmarch debug view: the share of rays the march stopped, and the share it let through past
        // something it was touching. Both channels are read by the shading as they come out of the filters.
        uVisibility.Store(px, float4(blocked / float(kSamples), max(nearMissSum / float(kSamples), 0.002), 0.0, 0.0));
        return;
    }
#endif
    uVisibility.Store(px, float4(visibleSum / float(kSamples), penumbra, 0.0, 0.0));
}
