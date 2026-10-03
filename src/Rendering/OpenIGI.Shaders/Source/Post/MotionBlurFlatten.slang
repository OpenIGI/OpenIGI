// Flattens the motion to one velocity per pixel, and reduces each tile to its fastest and slowest.
//
// The reduction carries POLAR velocities so that "fastest" is a comparison of one component rather
// than of two, and so the angle survives to the tile unaveraged -- averaging two directions would
// point the tile's blur somewhere neither object is going.

#include "../Common/DepthFilter.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uVelocity;
[[vk::binding(1, 0)]] SamplerState uVelocitySampler;
// A depth image declared as one: WebGPU refuses a depth format on a float slot, and cannot filter it.
[[vk::binding(2, 0)]] DepthTexture2D uDepth;
[[vk::binding(3, 0)]] SamplerState uDepthSampler;

[[vk::binding(4, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uFlat;
[[vk::binding(5, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTile;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;

    // xScale, yScale, near, far.
    float4 unproject;

    // velocityScaleX, velocityScaleY, velocityMaxPixels, unused.
    float4 control;
};

[[vk::binding(6, 0)]] ConstantBuffer<Params> p;

// A velocity as (length, angle), the angle zero for no motion.
float2 ToPolar(float2 v)
{
    float len = length(v);
    return float2(len, len > 0.0 ? atan2(v.y, v.x) : 0.0);
}

float2 FromPolar(float2 polar)
{
    return float2(cos(polar.y), sin(polar.y)) * polar.x;
}

// The group's polar velocities, folded in half each step down to the fastest and the slowest.
groupshared float2 sFastest[256];
groupshared float2 sSlowest[256];

// The view-space distance of a reverse-Z device depth, for a projection with these planes.
float ViewDistance(float deviceDepth, float near, float far)
{
    float ndc = 1.0 - (2.0 * deviceDepth);
    return (2.0 * near * far) / ((far + near) - (ndc * (far - near)));
}

[shader("compute")]
[numthreads(16, 16, 1)]
void computeMain(
    uint3 id : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID)
{
    int2 px = int2(id.xy);
    bool inFrame = px.x < int(p.size.x) && px.y < int(p.size.y);

    // Threads past the frame's edge still take part in the reduction, as standing still, so every
    // barrier below is reached by the whole group.
    float2 velocity = float2(0.0, 0.0);
    float viewDepth = 0.0;
    if (inFrame)
    {
        float2 uv = (float2(px) + 0.5) * p.size.zw;
        velocity = uVelocity.SampleLevel(uVelocitySampler, uv, 0.0).xy * p.control.xy;
        viewDepth = ViewDistance(DepthBilinear(uDepth, uDepthSampler, uv), p.unproject.z, p.unproject.w);
    }

    float2 polar = ToPolar(velocity);
    polar.x = min(polar.x, p.control.z);

    if (inFrame)
    {
        uFlat.Store(px, float4(polar.x, (polar.y * (0.5 / 3.14159265359)) + 0.5, viewDepth, 0.0));
    }

    uint lane = (localId.y * 16u) + localId.x;
    sFastest[lane] = polar;
    sSlowest[lane] = polar;
    GroupMemoryBarrierWithGroupSync();

    for (uint stride = 128u; stride > 0u; stride >>= 1u)
    {
        if (lane < stride)
        {
            float2 fastA = sFastest[lane];
            float2 fastB = sFastest[lane + stride];
            sFastest[lane] = fastA.x > fastB.x ? fastA : fastB;

            float2 slowA = sSlowest[lane];
            float2 slowB = sSlowest[lane + stride];
            sSlowest[lane] = slowA.x > slowB.x ? slowB : slowA;
        }

        GroupMemoryBarrierWithGroupSync();
    }

    if (lane == 0u)
    {
        uTile.Store(int2(groupId.xy), float4(FromPolar(sSlowest[0]), FromPolar(sFastest[0])));
    }
}
