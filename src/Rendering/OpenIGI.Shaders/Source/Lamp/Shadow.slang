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

    // level count, resolution per level, _, _.
    int4 levels;
};

[[vk::binding(9, 0)]] ConstantBuffer<FieldParams> f;

#define uClipmapOrigin f.origin
#define uClipmapRange f.range
#define uShadowLevels f.levels.x
#define uShadowResolution f.levels.y
#include "../Common/DistanceField.slang"

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
bool SoftwareBlocked(float3 from, float3 direction, float segment, out float hitT)
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
    for (int step = 0; step < 64 && travelled < end; step++)
    {
        bool known;
        float raw;
        float resolved;
        float distance = DistanceAt(from + (direction * travelled), known, raw, resolved);
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
        float hitRadius = (0.5 * resolved) * saturate(travelled / (2.0 * resolved));
        if (distance < hitRadius)
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
    float3 origin = worldPos + (normal * (0.08 + (0.0005 * viewPos.z)));

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
#if OPENIGI_LAMP_FIELD
        // Camera-relative, which is the space the field's corners are handed over in.
        hit = SoftwareBlocked(origin - p.cameraFrame.xyz, toTarget / distance, distance - stopShort, t);
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
    uVisibility.Store(px, float4(visibleSum / float(kSamples), penumbra, 0.0, 0.0));
}
