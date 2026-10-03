// One traced ray per pixel at the sun, sampled over its disc so the shadow has a penumbra.
//
// Compiled for SPIR-V alone: WGSL has no ray tracing, and the renderer already falls back to the
// rasterised cascade on an adapter without the extension.

#include "../Common/HitRecords.slang"
#include "../Common/ViewDepth.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uDepth;
[[vk::binding(1, 0)]] SamplerState uDepthSampler;

// the world normal, to offset off the surface.
[[vk::binding(2, 0)]] Texture2D<float4> uNormal;
[[vk::binding(3, 0)]] SamplerState uNormalSampler;

[[vk::binding(4, 0)]] RaytracingAccelerationStructure uScene;

// visibility, occluder distance.
[[vk::binding(5, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uShadow;

// cut-out shapes, one layer each.
[[vk::binding(6, 0)]] Texture2DArray<float4> uMasks;
[[vk::binding(7, 0)]] SamplerState uMasksSampler;

struct Params
{
    // view rotation, camera relative.
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;

    // xyz world direction towards the sun, w how far to look.
    float4 sunAndCamera;

    // xyz camera position, w the sine of the sun's half-angle.
    float4 camera;

    // x the frame index (the dither's phase), yz this frame's sub-pixel jitter in pixels.
    float4 jitter;

    // device address of the material records, low word then high.
    uint4 records;
};

[[vk::binding(8, 0)]] ConstantBuffer<Params> p;

// Every cut-out mask is stored at this size, so a UV maps to a texel without the array's own extent
// having to be known here.
static const float kMaskResolution = 256.0;

// A direction inside the sun's cone, uniformly over the solid angle it subtends. The small-angle form
// is taken below a hundredth because 1 - sqrt(1 - x) loses most of its significant figures there, and
// the sun's cone is exactly that small.
float3 SampleSunCone(float2 rand, float sinThetaMax2)
{
    float phi = 2.0 * 3.14159265359 * rand.x;
    float oneMinusCosThetaMax = sinThetaMax2 < 0.01
        ? sinThetaMax2 * (0.5 + (0.125 * sinThetaMax2))
        : 1.0 - sqrt(1.0 - sinThetaMax2);
    float cosTheta = 1.0 - (oneMinusCosThetaMax * rand.y);
    float sinTheta = sqrt(max(1.0 - (cosTheta * cosTheta), 0.0));
    return float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
}

// Duff's branchless orthonormal basis about an axis, which needs no normalisation and has no
// singularity at either pole.
float3 TangentToWorld(float3 v, float3 axis)
{
    float sign = axis.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (sign + axis.z);
    float b = axis.x * axis.y * a;
    float3 tangentX = float3(1.0 + (sign * a * axis.x * axis.x), sign * b, -sign * axis.x);
    float3 tangentY = float3(b, sign + (a * axis.y * axis.y), -axis.y);
    return (v.x * tangentX) + (v.y * tangentY) + (v.z * axis);
}

#include "../Common/FrameNoise.slang"

// One dimension of the cone sample, moving from frame to frame.
float InterleavedGradientNoise(float2 pos, float frame)
{
    return FrameNoise(pos, frame);
}

// Whether a candidate triangle actually stops light, which for a cut-out surface is a question about
// its texture rather than about its geometry. A wire fence, a canopy or a window is one quad whose
// shape lives in an alpha mask, and confirming every such hit puts a solid board where the fence is.
bool CandidateIsSolid<let F : int>(inout RayQuery<F> query)
{
    // No records at all: a structure built before this existed, or one whose records ran out. Treating
    // the hit as solid is the safe answer -- a shadow that is too complete rather than absent.
    if ((p.records.x | p.records.y) == 0u)
    {
        return true;
    }

    uint record = uint(query.CandidateInstanceID()) + uint(query.CandidateGeometryIndex());
    ConstBufferPointer<uint> records = HitWordsAt(p.records.x, p.records.y);
    uint at = record * kHitRecordWords;

    int layer = int(records[at + kHitMaskLayerWord]);
    if (layer < 0)
    {
        // No mask: the material is opaque, and every triangle of it stops light.
        return true;
    }

    ConstBufferPointer<float> vertices = HitFloatsAt(
        records[at + kHitVertexAddressWord], records[at + kHitVertexAddressWord + 1u]);
    ConstBufferPointer<uint> indices = HitWordsAt(
        records[at + kHitIndexAddressWord], records[at + kHitIndexAddressWord + 1u]);

    uint first = records[at + kHitFirstIndexWord] + (uint(query.CandidatePrimitiveIndex()) * 3u);

    uint i0 = HitIndexAt(indices, first + 0u);
    uint i1 = HitIndexAt(indices, first + 1u);
    uint i2 = HitIndexAt(indices, first + 2u);

    float2 uv0 = float2(
        vertices[(i0 * kHitVertexFloats) + kHitUvFloat],
        vertices[(i0 * kHitVertexFloats) + kHitUvFloat + 1u]);
    float2 uv1 = float2(
        vertices[(i1 * kHitVertexFloats) + kHitUvFloat],
        vertices[(i1 * kHitVertexFloats) + kHitUvFloat + 1u]);
    float2 uv2 = float2(
        vertices[(i2 * kHitVertexFloats) + kHitUvFloat],
        vertices[(i2 * kHitVertexFloats) + kHitUvFloat + 1u]);

    float2 bary = query.CandidateTriangleBarycentrics();
    float2 uv = (uv0 * (1.0 - bary.x - bary.y)) + (uv1 * bary.x) + (uv2 * bary.y);

    int2 texel = int2(frac(uv) * kMaskResolution);
    return uMasks.Load(int4(texel, layer, 0)).r > 0.5;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.resolution.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float2 uv = (float2(px) + 0.5) * p.resolution.zw;
    float depth = uDepth.SampleLevel(uDepthSampler, uv, 0.0).r;

    // Reverse-Z clears to zero: the sky, which nothing shadows.
    if (depth <= 0.0)
    {
        uShadow.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 normal = normalize((uNormal.SampleLevel(uNormalSampler, uv, 0.0).xyz * 2.0) - 1.0);
    float3 sunDirection = p.sunAndCamera.xyz;

    // Facing away from the sun: the surface shadows itself completely and no ray need be fired.
    if (dot(normal, sunDirection) <= 0.0)
    {
        uShadow.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // Where this pixel's depth was actually measured, which is NOT its centre: the frame was rasterised
    // through a projection displaced by the jitter, so the point it holds sits at k + 0.5 - jitter in the
    // unjittered screen the unprojection describes. Rebuilt at the centre, a far hillside seen edge-on put
    // the ray's start a metre or more off its own surface -- under it, on some phases of the jitter
    // sequence, where every ray struck the hill and it went into shadow for that frame.
    float2 sampleUv = (float2(px) + 0.5 - p.jitter.yz) * p.resolution.zw;
    float3 viewPos = ViewPositionAt(sampleUv, depth, p.unproject);

    // Absolute world, which is the space the acceleration structure is built in.
    float3 worldPos = mul(p.viewToWorld, float4(viewPos, 1.0)).xyz + p.camera.xyz;

    // Off the surface along its own normal, and scaled with distance because the rounding this guards
    // against grows with the magnitude of the position it happens to.
    float offset = 0.02 + (0.0005 * viewPos.z);

    float2 rand = float2(
        InterleavedGradientNoise(float2(px), p.jitter.x),
        InterleavedGradientNoise(float2(px) + 97.0, p.jitter.x));

    float sinThetaMax = p.camera.w;
    float3 traceDirection = sinThetaMax > 0.0
        ? normalize(TangentToWorld(SampleSunCone(rand, sinThetaMax * sinThetaMax), sunDirection))
        : sunDirection;

    RayDesc ray;
    ray.Origin = worldPos + (normal * offset);
    ray.TMin = 0.01;
    ray.Direction = traceDirection;
    ray.TMax = p.sunAndCamera.w;

    // Everything but the first-person rig: the player's third-person body carries the same arms and the
    // same weapon, and shadowing both puts a second pair of hands on the ground.
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);

    while (query.Proceed())
    {
        if (query.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE && CandidateIsSolid(query))
        {
            query.CommitNonOpaqueTriangleHit();
        }
    }

    bool hit = query.CommittedStatus() != COMMITTED_NOTHING;
    float hitDistance = hit ? query.CommittedRayT() : 0.0;

    // The occluder distance rides alongside the verdict: the reconstruction behind this derives the
    // penumbra's width from how far the ray got before something stopped it.
    uShadow.Store(px, float4(hit ? 0.0 : 1.0, hitDistance, 0.0, 0.0));
}
