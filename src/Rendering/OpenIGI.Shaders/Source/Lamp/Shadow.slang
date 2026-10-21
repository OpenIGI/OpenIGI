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
#include "../Common/LampIrradiance.slang"
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

[[vk::binding(5, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uVisibility;

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

    // count, whether they shadow, frame index, and above a half when the trace feeds ray reconstruction raw.
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

#include "../Common/LampMapVisibility.slang"

#define uClipmapOrigin f.origin
#define uClipmapRange f.range
#define uShadowLevels f.levels.x
#define uShadowResolution f.levels.y
#include "../Common/DistanceField.slang"

#include "../Common/LampScreenTrace.slang"
#endif

// A pixel's own random numbers: a hash of its coordinates, fixed for the pixel, advanced every frame
// along a low-discrepancy sequence (the golden ratio for one number, the R2 constants for two): no
// structure across the screen, and well spread over the frames a pixel accumulates. The interleaved
// gradient noise it replaces is a pattern of diagonal lines; at rest the accumulation hid it, and on the
// move -- history partly rejected -- it came through as diagonal hatching across every lamp-lit floor.
#include "../Common/PixelNoise.slang"

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

    // Wrapped so the low-discrepancy steps below keep their precision in a float. Raw, for ray
    // reconstruction, the pixel's numbers are drawn afresh each frame instead and nothing steps them: it
    // denoises independent samples, and reads a sequence that moves coherently as structure.
    bool rawNoise = p.lampControl.w > 0.5;
    float frame = rawNoise ? 0.0 : float(uint(p.cameraFrame.w) % 65536u);
    float3 pixelRandom = rawNoise ? WhiteNoise3(px, uint(p.cameraFrame.w), 0x1A3Du) : PixelRandom(px);
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
        // The lighting pass's own diffuse term for this lamp, from the same function: a tube integrated along
        // its length, a point softened inside its fitting, the reach window and the cone. Weighed any other way,
        // a lamp the shading lights a surface with can count as reaching it not at all, and its light goes
        // through whatever stands in the way.
        float4 positionRadius = p.lampPositionRadius[i];
        float3 toLightView = positionRadius.xyz - viewPos;
        float distanceSq = dot(toLightView, toLightView);
        float window = LampReachWindow(distanceSq, positionRadius.w);
        if (window <= 0.0)
        {
            continue;
        }

        float3 toCentre = mul(p.viewToWorld, float4(toLightView, 0.0)).xyz;
        float3 centreDir = toCentre * rsqrt(max(distanceSq, 1e-8));
        float4 facing = p.lampDirection[i];
        if (LampFaces(facing))
        {
            window *= LampCone(mul(p.viewToWorld, float4(facing.xyz, 0.0)).xyz, centreDir);
            if (window <= 0.0)
            {
                continue;
            }
        }

        float source = LampSource(p.lampDirection[i]);
        float4 rect = p.lampRect[i];
        float3 halfAxis = mul(p.viewToWorld, float4(rect.xyz, 0.0)).xyz;

        // What the lamp brings here, its highlight counted alongside its diffuse light: twice the point
        // irradiance -- a point lamp's highlight comes from where its diffuse light does -- and for a tube twice
        // a bound on it (LampTubeBound), which is nought exactly where the shading's tube light is. A bound on
        // the highlight rather than its value, since the trace does not know how glossy the surface is, and one
        // that errs towards tracing a lamp the eye could not have told apart.
        float lit;
        if (dot(halfAxis, halfAxis) > 1e-4)
        {
            lit = LampTubeBound(normal, toCentre, halfAxis, clamp(rect.w, 0.02, source));
        }
        else
        {
            lit = LampPointIrradiance(normal, centreDir, distanceSq, source);
        }

        float4 colourIntensity = p.lampColourIntensity[i];
        float brightness = dot(colourIntensity.rgb * colourIntensity.a, float3(0.2126, 0.7152, 0.0722));
        float weight = 2.0 * window * lit * brightness;
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
        // voxels are a metre or two, and a far lamp lit straight through the building it stood behind. Only
        // for a lamp with no map of its own, though: a point outside a mapped lamp's view goes to the field,
        // as the map's own edge rule intends (see kOutsideLampMap).
        if (mapped == kNoLampMap)
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
    // Raw, the shading reads this image itself rather than the filters' output, and its upsample keys each
    // texel on the view depth the filters would otherwise have written beside it.
    uVisibility.Store(px, float4(visibleSum / float(kSamples), penumbra, rawNoise ? viewPos.z : 0.0, 0.0));
}
