// How much light survives between a point at some altitude and space, in some direction.
//
// The first of the atmosphere's three tables, and the only one that needs no table of its own to build.

#include "../Common/SkyAtmosphere.slang"

[[vk::binding(0, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTransmittance;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;

    // unused here.
    float4 sunDir;

    // unused here.
    float4 sunColour;

    // view height km, _, _, _.
    float4 misc;
    // world direction towards the moon and the share of it the air scatters; the moon's illuminance
    // outside the atmosphere, nought by day.
    float4 moonDir;
    float4 moonColour;
};

[[vk::binding(1, 0)]] ConstantBuffer<Params> p;

// No table to read yet, so transmittance towards the sun is left at one and no multiple scattering is
// taken; all this march accumulates is the optical depth along its own ray. The march calls both by
// name, so they stand in for the lookups the other passes include.
float3 SampleTransmittanceLut(float viewHeight, float lightZenithCos)
{
    return float3(1.0, 1.0, 1.0);
}

float3 SampleMultiScattering(float3 worldPos, float lightZenithCos)
{
    return float3(0.0, 0.0, 0.0);
}

#include "../Common/SkyMarch.slang"

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.size.x) || px.y >= int(p.size.y))
    {
        return;
    }

    float2 uv = (float2(px) + 0.5) * p.size.zw;
    float viewHeight;
    float viewZenithCos;
    TransmittanceUvToParams(uv, viewHeight, viewZenithCos);

    float3 origin = float3(0.0, 0.0, viewHeight);
    float3 direction = float3(0.0, sqrt(max(0.0, 1.0 - (viewZenithCos * viewZenithCos))), viewZenithCos);

    // Only the optical depth along the ray matters, so the light, its phase and the ground are idle.
    const float kTransmittanceSamples = 10.0;
    ScatteringResult march = IntegrateScatteredLuminance(
        origin,
        direction,
        float3(0.0, 0.0, 1.0),
        float3(0.0, 0.0, 0.0),
        false,
        kTransmittanceSamples,
        false,
        false,
        false);

    uTransmittance.Store(px, float4(EncodeTransmittance(exp(-march.opticalDepth)), 1.0));
}
