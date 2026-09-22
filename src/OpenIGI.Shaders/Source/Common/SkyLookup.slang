// Reading the two precomputed atmosphere tables.
//
// A pass that has them bound includes this; a pass that is BUILDING one of them defines the same two
// names as its own functions instead, which is how the march can be shared between the pass that has no
// tables yet and the passes that do.
//
// The host must declare before including: Texture2D<float4> uTransmittanceLut and
// Texture2D<float4> uMultiScatterLut, each with its sampler.

#ifndef OPENIGI_SKY_LOOKUP_INCLUDED
#define OPENIGI_SKY_LOOKUP_INCLUDED

#include "SkyAtmosphere.slang"

float3 SampleTransmittanceLut(float viewHeight, float lightZenithCos)
{
    return DecodeTransmittance(
        uTransmittanceLut.SampleLevel(
            uTransmittanceLutSampler,
            TransmittanceParamsToUv(viewHeight, lightZenithCos),
            0.0).rgb);
}

float3 SampleMultiScattering(float3 worldPos, float lightZenithCos)
{
    float2 uv = clamp(
        float2(
            (lightZenithCos * 0.5) + 0.5,
            (length(worldPos) - kBottomRadiusKm) / (kTopRadiusKm - kBottomRadiusKm)),
        0.0,
        1.0);
    return uMultiScatterLut.SampleLevel(uMultiScatterLutSampler, uv, 0.0).rgb;
}

#endif
