// The air between the camera and a lit surface, in closed form: the atmosphere's own scattering and
// extinction taken over the distance, with the sun's phase along the view ray. Nothing in it reads a binding.

#ifndef OPENIGI_AERIAL_PERSPECTIVE_INCLUDED
#define OPENIGI_AERIAL_PERSPECTIVE_INCLUDED

#include "SkyAtmosphere.slang"

static const float kMetresToKm = 0.001;

// The air between the camera and a surface: what it takes out of the surface's colour, and what it adds
// back from the sun scattering towards the eye along the same line.
float3 AerialPerspective(
    float3 colour,
    float3 worldRay,
    float3 sunDirection,
    float3 sunIlluminance,
    float distanceKm)
{
    const float kMieScatteringOnly = 0.003996;
    const float kMieAbsorptionOnly = 0.000444;
    float3 scattering = kRayleighScattering + float3(kMieScatteringOnly, kMieScatteringOnly, kMieScatteringOnly);
    float3 extinction = kRayleighScattering
        + float3(
            kMieScatteringOnly + kMieAbsorptionOnly,
            kMieScatteringOnly + kMieAbsorptionOnly,
            kMieScatteringOnly + kMieAbsorptionOnly);
    float3 transmittance = exp(-extinction * distanceKm);

    float cosTheta = dot(sunDirection, worldRay);
    float rayleighPhase = RayleighPhase(cosTheta);
    float miePhase = MiePhase(kMiePhaseG, -cosTheta);

    float3 inScattering =
        ((kRayleighScattering * rayleighPhase)
            + float3(kMieScatteringOnly * miePhase, kMieScatteringOnly * miePhase, kMieScatteringOnly * miePhase))
        * sunIlluminance
        * ((1.0 - transmittance) / max(extinction, float3(1e-6, 1e-6, 1e-6)));

    return (colour * transmittance) + inScattering;
}

#endif
