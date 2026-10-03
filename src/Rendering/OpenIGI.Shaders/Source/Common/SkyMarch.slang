// The single-scattering march every atmosphere table is built with.
//
// One ray from a point through the medium, to the top of the air or to the ground, accumulating what the
// sun scatters towards its origin, the optical depth, and the share of light the medium would scatter
// back at unit illuminance (the multiple-scattering estimate's transfer factor). Each segment is
// integrated analytically against its own extinction (Hillaire 2015, 2020), so few samples hold energy.
//
// The host defines SampleTransmittanceLut and SampleMultiScattering before including this: a pass that is
// building one of those tables supplies a stand-in for it.

#ifndef OPENIGI_SKY_MARCH_INCLUDED
#define OPENIGI_SKY_MARCH_INCLUDED

#include "SkyAtmosphere.slang"

struct ScatteringResult
{
    float3 luminance;
    float3 opticalDepth;
    float3 transmittance;
    float3 multiScatteringAs1;
};

// Where inside its segment each sample sits. Fixed, not noise: nothing filters these tables over time.
static const float kSampleSegmentOffset = 0.3;

// The variable sample count: from the fewest on a short ray to the most on one this long or longer.
static const float kMinSampleCount = 4.0;
static const float kMaxSampleCount = 128.0;
static const float kSampleCountDistanceKm = 150.0;

// How far above the surface the planet-shadow test starts, so a point on the ground does not shadow itself.
static const float kShadowRayLiftKm = 0.001;

ScatteringResult IntegrateScatteredLuminance(
    float3 worldPos,
    float3 worldDir,
    float3 sunDir,
    float3 sunIlluminance,
    bool ground,
    float sampleCount,
    bool mieRayPhase,
    bool applyMultiScattering,
    bool variableSampleCount)
{
    ScatteringResult result;
    result.luminance = float3(0.0, 0.0, 0.0);
    result.opticalDepth = float3(0.0, 0.0, 0.0);
    result.transmittance = float3(1.0, 1.0, 1.0);
    result.multiScatteringAs1 = float3(0.0, 0.0, 0.0);

    if (dot(worldPos, worldPos) <= kBottomRadiusKm * kBottomRadiusKm)
    {
        return result;
    }

    float2 topHits = RayIntersectSphere(worldPos, worldDir, kTopRadiusKm);
    if (topHits.x < 0.0 && topHits.y < 0.0)
    {
        return result;
    }

    // The ray ends at the top of the air, unless the ground is in the way first.
    float2 groundHits = RayIntersectSphere(worldPos, worldDir, kBottomRadiusKm);
    float tBottom = 0.0;
    float tMax;
    if (groundHits.x < 0.0 && groundHits.y < 0.0)
    {
        tMax = topHits.y;
    }
    else
    {
        tBottom = max(0.0, min(groundHits.x, groundHits.y));
        tMax = tBottom;
    }

    // Both phases depend only on the ray and the light, so once per ray.
    float cosTheta = dot(sunDir, worldDir);
    float miePhase = MiePhase(kMiePhaseG, -cosTheta);
    float rayleighPhase = RayleighPhase(cosTheta);
    const float uniformPhase = 1.0 / (4.0 * kPi);

    float count = sampleCount;
    float countFloor = sampleCount;
    float tMaxFloor = tMax;
    if (variableSampleCount)
    {
        count = lerp(kMinSampleCount, kMaxSampleCount, saturate(tMax / kSampleCountDistanceKm));
        countFloor = floor(count);
        tMaxFloor = tMax * countFloor / count;
    }

    float3 luminance = float3(0.0, 0.0, 0.0);
    float3 opticalDepth = float3(0.0, 0.0, 0.0);
    float3 throughput = float3(1.0, 1.0, 1.0);
    float3 multiScatteringAs1 = float3(0.0, 0.0, 0.0);

    for (float i = 0.0; i < count; i += 1.0)
    {
        float t;
        float dt;
        if (variableSampleCount)
        {
            // Squared spacing crowds the samples near the viewer, where the medium is densest along the
            // ray; the last segment always closes on the true end of the ray.
            float t0 = i / countFloor;
            float t1 = (i + 1.0) / countFloor;
            t0 = t0 * t0;
            t1 = t1 * t1;
            t0 = tMaxFloor * t0;
            t1 = t1 > 1.0 ? tMax : tMaxFloor * t1;
            dt = t1 - t0;
            t = t0 + (kSampleSegmentOffset * dt);
        }
        else
        {
            dt = tMax / count;
            t = tMax * (i + kSampleSegmentOffset) / count;
        }

        float3 position = worldPos + (t * worldDir);
        float height = length(position);
        float3 up = position / height;

        AtmosphereSample medium = SampleAtmosphere(position);
        float3 segmentDepth = medium.extinction * dt;
        float3 segmentTransmittance = exp(-segmentDepth);
        opticalDepth += segmentDepth;

        float sunZenithCos = dot(sunDir, up);
        float3 sunTransmittance = SampleTransmittanceLut(height, sunZenithCos);

        float3 phaseScattering = mieRayPhase
            ? (medium.mieScattering * miePhase) + (medium.rayleighScattering * rayleighPhase)
            : medium.scattering * uniformPhase;

        float planetHit = NearestHit(
            RayIntersectSphere(position + (kShadowRayLiftKm * up), sunDir, kBottomRadiusKm));
        float planetShadow = planetHit >= 0.0 ? 0.0 : 1.0;

        float3 multiScattered = applyMultiScattering
            ? SampleMultiScattering(position, sunZenithCos)
            : float3(0.0, 0.0, 0.0);

        float3 source = sunIlluminance
            * ((planetShadow * sunTransmittance * phaseScattering) + (multiScattered * medium.scattering));

        multiScatteringAs1 += throughput * medium.scattering * dt;

        // The source held constant over the segment, integrated against the segment's own extinction.
        luminance += throughput * (source - (source * segmentTransmittance)) / max(medium.extinction, 1e-9);
        throughput *= segmentTransmittance;
    }

    // The ground lit by the sun, bounced back up as a Lambertian grey.
    if (ground && tMax == tBottom && tBottom > 0.0)
    {
        float3 groundPos = worldPos + (tBottom * worldDir);
        float groundHeight = length(groundPos);
        float3 groundUp = groundPos / groundHeight;
        float groundSunCos = dot(sunDir, groundUp);
        float3 sunTransmittance = SampleTransmittanceLut(groundHeight, groundSunCos);
        luminance += sunIlluminance * sunTransmittance * throughput
            * saturate(dot(groundUp, sunDir)) * kGroundAlbedo / kPi;
    }

    result.luminance = luminance;
    result.opticalDepth = opticalDepth;
    result.transmittance = throughput;
    result.multiScatteringAs1 = multiScatteringAs1;
    return result;
}

#endif
