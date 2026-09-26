// How much of a light reaches a point, planet shadow included, for a surface being lit rather than a
// ray being marched.
//
// The table holds no planet shadow -- it is only filled from zenith to horizon, so that filtering across
// a coarse table cannot smear darkness over the horizon -- so the planet blocking the ray is worked out
// here instead.

#ifndef OPENIGI_SKY_SUN_TRANSMITTANCE_INCLUDED
#define OPENIGI_SKY_SUN_TRANSMITTANCE_INCLUDED

#include "SkyAtmosphere.slang"

float3 AtmosphereTransmittance(float3 planetRelativePos, float3 direction)
{
    float2 ground = RayIntersectSphere(planetRelativePos, direction, kBottomRadiusKm);
    if (ground.x > 0.0 || ground.y > 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float height = length(planetRelativePos);
    return SampleTransmittanceLut(height, dot(direction, planetRelativePos / height));
}

#endif
