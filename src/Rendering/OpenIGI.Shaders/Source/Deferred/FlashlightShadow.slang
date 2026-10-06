// The flashlight's shadow: whether the light from the lens reaches each pixel, one ray per pixel.
//
// Not retail -- the original has no flashlight. Two forms. The default traces the ray against the world's
// acceleration structure, cut-out masks and all, so a chain-link fence throws its diamonds across the floor
// and a window lets the beam through; it is compiled for SPIR-V alone. The software form
// (OPENIGI_FLASHLIGHT_MARCH) marches the depth buffer instead, for an adapter that cannot trace -- WebGPU,
// and Vulkan with the RT switch off.
//
// The march is better here than it is for any other light, and that is what makes it the right fallback
// rather than a poor one. The lens hangs a hand's width from the eye, so nearly everything that can shadow
// it is something the camera can see: the shadows it casts are the thin slivers of parallax between what
// the eye sees and what the lens sees, beside the occluder's own silhouette -- exactly what a depth buffer
// knows. A sample on the ray that lands behind a visible surface is, from a point that close to the eye,
// almost certainly behind it for the lens too, so the march can take a surface to be much thicker than a
// lamp's march dares to.
//
// The output is the visibility and, where it was blocked, the penumbra ratio t / (D - t) the occluder
// implies -- the filter behind this pass widens its kernel by it, so a shadow is crisp at its caster and
// softens with distance, as a small light's does. No history: the light moves with the view.

#ifndef OPENIGI_FLASHLIGHT_MARCH
#define OPENIGI_FLASHLIGHT_MARCH 0
#endif

#include "../Common/ViewDepth.slang"
#include "../Common/GBufferEncoding.slang"
#if !OPENIGI_FLASHLIGHT_MARCH
#include "../Common/HitRecords.slang"
#include "../Common/CutoutHit.slang"
#endif

// Both read with a load at a texel: the depth as the depth image it is, which WGSL requires.
[[vk::binding(0, 0)]] DepthTexture2D uDepth;
[[vk::binding(2, 0)]] Texture2D<float4> uNormal;

#if !OPENIGI_FLASHLIGHT_MARCH
[[vk::binding(4, 0)]] RaytracingAccelerationStructure uScene;
#endif

[[vk::binding(5, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uRaw;

#if !OPENIGI_FLASHLIGHT_MARCH
// The world's cut-out masks, for the rays' alpha test (CutoutHit.slang).
[[vk::binding(6, 0)]] Texture2DArray<float4> uMasks;
#endif

struct Params
{
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;

    // xyz the camera's world position, w the frame index.
    float4 cameraFrame;

    // xyz the lens in view space, w its radius.
    float4 sourceLens;

    // xyz the beam's axis in view space, w the cosine of the spill's half-angle.
    float4 axisCull;

    // the range, how far short of the lens a ray stops, the march's step count, and above a half when the
    // trace feeds ray reconstruction raw.
    float4 reach;

    // the hit records' address as a bit pattern (low word, high word), _, _.
    float4 records;
};

[[vk::binding(8, 0)]] ConstantBuffer<Params> p;

// A pixel's own random numbers, fixed for the pixel and moved every frame along the R2 sequence, so the
// points on the lens spread evenly over the frames the temporal resolve blends.
#include "../Common/PixelNoise.slang"

float3 PixelRandom(int2 px, float frame)
{
    float3 base = float3(Pcg3d(uint3(uint2(px), 0x68E31DA4u)) >> 8u) * (1.0 / 16777216.0);
    return frac(base + (frame * float3(0.7548776662, 0.5698402910, 0.6180339887)));
}

#if OPENIGI_FLASHLIGHT_MARCH
float2 ProjectToUv(float3 viewPosition)
{
    float2 ndc = float2(
        (viewPosition.x * p.unproject.x) / viewPosition.z,
        (-viewPosition.y * p.unproject.y) / viewPosition.z);
    return (ndc * 0.5) + 0.5;
}

// Whether anything the camera can see stands between a point and the lens, and how far along if so. View
// space throughout. The steps are spaced by the square of their index -- dense by the receiver, where the
// contact detail is, and sparse towards the lens -- and each one's position within its interval moves with
// the pixel and the frame, so the steps between them are covered over a few frames.
bool ScreenBlocked(float3 from, float3 direction, float segment, float jitter, out float hitT)
{
    hitT = 0.0;

    int steps = int(p.reach.z + 0.5);
    int2 last = int2(p.resolution.xy) - 1;
    for (int i = 0; i < steps; i++)
    {
        float s = (float(i) + jitter) / float(steps);
        float t = segment * s * s;
        float3 view = from + (direction * t);
        if (view.z <= p.unproject.z)
        {
            break;
        }

        float2 uv = ProjectToUv(view);
        if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)))
        {
            break;
        }

        int2 texel = clamp(int2(uv * p.resolution.xy), int2(0, 0), last);
        float depth = uDepth.Load(int3(texel, 0));
        if (depth <= 0.0)
        {
            continue;
        }

        float sceneZ = ViewDepthAt(depth, p.unproject);
        float behind = view.z - sceneZ;

        // In front of the surface by more than its own depth's precision, and behind it by less than the
        // thickness a surface this far away is taken to have.
        if (behind > 0.02 + (0.002 * sceneZ) && behind < max(0.3, 0.06 * sceneZ))
        {
            hitT = t;
            return true;
        }
    }

    return false;
}
#endif

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.resolution.x) || px.y >= int(p.resolution.y))
    {
        return;
    }

    float depth = uDepth.Load(int3(px, 0));

    // Reverse-Z clears to zero: the sky, which nothing lights.
    if (depth <= 0.0)
    {
        uRaw.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float2 uv = (float2(px) + 0.5) * p.resolution.zw;
    float3 viewPos = ViewPositionAt(uv, depth, p.unproject);

    float3 toLens = p.sourceLens.xyz - viewPos;
    float distance = length(toLens);
    float3 toLensDir = toLens / max(distance, 1e-5);

    // Out of the beam, out of reach, or facing away: the shading gives these nothing whatever this says, so
    // they are left lit rather than traced -- and lit is what a filter tap from a neighbour should read.
    if (distance >= p.reach.x || dot(p.axisCull.xyz, -toLensDir) <= p.axisCull.w)
    {
        uRaw.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 normal = DecodeGBufferNormal(uNormal.Load(int3(px, 0)));
    float3 normalView = mul(float4(normal, 0.0), p.viewToWorld).xyz;
    if (dot(normalView, toLensDir) <= 0.0)
    {
        uRaw.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    // Raw, for ray reconstruction: drawn afresh each frame, which is what it denoises.
    float frame = p.cameraFrame.w;
    float3 random = p.reach.w > 0.5 ? WhiteNoise3(px, uint(frame), 0xF1A5u) : PixelRandom(px, frame);

    // A point on the lens, uniform over its disc square to the beam, so the edge of a shadow has the few
    // centimetres of softness a real emitter gives it.
    float3 axis = p.axisCull.xyz;
    float3 helper = abs(axis.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    float3 across = normalize(cross(helper, axis));
    float3 along = cross(axis, across);
    float spread = sqrt(random.x) * p.sourceLens.w;
    float around = 6.28318530718 * random.y;
    float3 lensPoint = p.sourceLens.xyz + (across * (spread * cos(around))) + (along * (spread * sin(around)));

    // Off the surface along its own normal, more with distance, so a ray does not find the surface it leaves.
    float3 origin = viewPos + (normalView * (0.03 + (0.001 * viewPos.z)));
    float3 toTarget = lensPoint - origin;
    float span = length(toTarget);
    float3 direction = toTarget / max(span, 1e-5);

    // Stopped short of the lens: past the clearance is the hand, the gun and the body holding it.
    float segment = span - p.reach.y;
    if (segment <= 0.02)
    {
        uRaw.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    bool hit;
    float t;
#if OPENIGI_FLASHLIGHT_MARCH
    hit = ScreenBlocked(origin, direction, segment, random.z, t);
#else
    // Absolute world, which is the space the acceleration structure is built in.
    RayDesc ray;
    ray.Origin = mul(p.viewToWorld, float4(origin, 1.0)).xyz + p.cameraFrame.xyz;
    ray.TMin = 0.0;
    ray.Direction = normalize(mul(p.viewToWorld, float4(direction, 0.0)).xyz);
    ray.TMax = segment;

    uint2 records = uint2(asuint(p.records.x), asuint(p.records.y));

    // Everything but the first-person rig, for the reason the sun's trace excludes it; cut-outs tested
    // against their masks, so a window lets the beam through and the wall around it does not.
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
    ProceedWithCutouts(query, records, uMasks);

    hit = query.CommittedStatus() != COMMITTED_NOTHING;
    t = hit ? query.CommittedRayT() : 0.0;
#endif

    // Similar triangles: an occluder t from the receiver with the lens D away casts a penumbra that grows as
    // t / (D - t). Never nought where blocked, since a positive ratio is also what says a ray was.
    float ratio = hit ? clamp(t / max(span - t, 0.05), 0.01, 64.0) : 0.0;
    uRaw.Store(px, float4(hit ? 0.0 : 1.0, ratio, 0.0, 0.0));
}
