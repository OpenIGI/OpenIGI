// The atmosphere's second table: the light scattered more than once, by sun elevation and altitude.
//
// Hillaire 2020 (section 5.5) observes that after the first bounce the light in clear air is close to
// isotropic, so the higher orders can be folded into one isotropic source per point: the light a point
// receives from second-order scattering, amplified by a geometric series in the fraction of light the
// medium around it re-scatters. Both quantities are estimated here from a short march along the local
// vertical, up and down, which is cheap and holds up well for a medium layered by altitude.

#include "../Common/SkyAtmosphere.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uTransmittanceLut;
[[vk::binding(1, 0)]] SamplerState uTransmittanceLutSampler;

[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uMultiScatter;

struct Params
{
    float4 size;
    float4 sunDir;
    float4 sunColour;
    float4 misc;
    // world direction towards the moon and the share of it the air scatters; the moon's illuminance
    // outside the atmosphere, nought by day.
    float4 moonDir;
    float4 moonColour;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

float3 SampleTransmittanceLut(float viewHeight, float lightZenithCos)
{
    return DecodeTransmittance(
        uTransmittanceLut.SampleLevel(
            uTransmittanceLutSampler,
            TransmittanceParamsToUv(viewHeight, lightZenithCos),
            0.0).rgb);
}

// The table being built here is the first estimate of it, so there is nothing to read yet.
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
    float sunZenithCos = (uv.x * 2.0) - 1.0;
    float3 sunDirection = float3(0.0, sqrt(saturate(1.0 - (sunZenithCos * sunZenithCos))), sunZenithCos);
    float viewHeight = kBottomRadiusKm + (uv.y * (kTopRadiusKm - kBottomRadiusKm));
    float3 origin = float3(0.0, 0.0, viewHeight);

    // Unit white light, isotropic phase, the ground's bounce included and no multiple scattering yet.
    const float kSamples = 15.0;
    const float3 kUnitLight = float3(1.0, 1.0, 1.0);
    ScatteringResult up = IntegrateScatteredLuminance(
        origin, float3(0.0, 0.0, 1.0), sunDirection, kUnitLight, true, kSamples, false, false, false);
    ScatteringResult down = IntegrateScatteredLuminance(
        origin, float3(0.0, 0.0, -1.0), sunDirection, kUnitLight, true, kSamples, false, false, false);

    // The two directions stand for the sphere around the point, each for half of it.
    float3 secondOrder = (up.luminance + down.luminance) * 0.5;
    float3 transfer = (up.multiScatteringAs1 + down.multiScatteringAs1) * 0.5;

    // The series 1 + f + f^2 + ... cut after five terms, so no medium, however extreme, can run away.
    float3 series = 1.0 + transfer + (transfer * transfer) + (transfer * transfer * transfer)
        + (transfer * transfer * transfer * transfer);
    float3 total = secondOrder * series;

    uMultiScatter.Store(px, float4(total * kMultiScatteringFactor, 1.0));
}
