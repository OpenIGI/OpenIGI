#ifndef OPENIGI_SKY_ATMOSPHERE_INCLUDED
#define OPENIGI_SKY_ATMOSPHERE_INCLUDED

// The clear air above the level: what it is made of at each altitude, how it scatters, and how the
// transmittance table is addressed.
//
// The medium is the standard Earth atmosphere of the precomputed-scattering literature (Bruneton and
// Neyret 2008; Hillaire 2020): an exponential Rayleigh layer, a thinner exponential aerosol (Mie) layer
// and an ozone band that only absorbs. Everything is in kilometres, around a planet centred on the origin.

static const float kPi = 3.1415926535897932;

// Ground and the top of the air, as distances from the planet's centre.
static const float kBottomRadiusKm = 6360.0;
static const float kTopRadiusKm = 6420.0;

// Rayleigh scattering at sea level per kilometre; molecules absorb nothing worth counting.
static const float3 kRayleighScattering = float3(0.005802, 0.013558, 0.033100);
static const float kRayleighScaleHeightKm = 8.0;

// Aerosols scatter grey and absorb a tenth of what they take out.
static const float kMieScattering = 0.003996;
static const float kMieExtinction = 0.004440;
static const float kMieScaleHeightKm = 1.2;

// Ozone absorbs only, in a band peaking at 25 km.
static const float3 kOzoneAbsorption = float3(0.000650, 0.001881, 0.000085);

// Forward-peaked aerosol phase.
static const float kMiePhaseG = 0.8;

// Scale on the multiple-scattering table; one keeps it physical.
static const float kMultiScatteringFactor = 1.0;

// The ground as the multiple-scattering estimate sees it: a neutral mid grey.
static const float kGroundAlbedo = 0.4;

struct AtmosphereSample
{
    float3 scattering;
    float3 extinction;
    float3 mieScattering;
    float3 rayleighScattering;
};

// The medium at a planet-centred position.
AtmosphereSample SampleAtmosphere(float3 position)
{
    float altitude = max(0.0, length(position) - kBottomRadiusKm);

    float rayleighDensity = exp(-altitude / kRayleighScaleHeightKm);
    float mieDensity = exp(-altitude / kMieScaleHeightKm);

    // A tent: rising from 10 km, at its peak at 25 km, gone by 40 km.
    float ozoneDensity = altitude < 25.0
        ? saturate((altitude / 15.0) - (2.0 / 3.0))
        : saturate((-altitude / 15.0) + (8.0 / 3.0));

    AtmosphereSample s;
    s.rayleighScattering = kRayleighScattering * rayleighDensity;
    float mieScattering = kMieScattering * mieDensity;
    float mieExtinction = kMieExtinction * mieDensity;
    s.mieScattering = float3(mieScattering, mieScattering, mieScattering);
    s.scattering = s.mieScattering + s.rayleighScattering;
    s.extinction = float3(mieExtinction, mieExtinction, mieExtinction)
        + s.rayleighScattering
        + (kOzoneAbsorption * ozoneDensity);
    return s;
}

// Henyey-Greenstein written with a plus sign in the denominator: the lobe peaks where cosTheta = -1, so
// a caller wanting the forward peak along the light passes minus the cosine between view and light.
float MiePhase(float g, float cosTheta)
{
    float denominator = 1.0 + (g * g) + (2.0 * g * cosTheta);
    return (1.0 - (g * g)) / (4.0 * kPi * denominator * sqrt(max(denominator, 1e-8)));
}

float RayleighPhase(float cosTheta)
{
    return (3.0 / (16.0 * kPi)) * (1.0 + (cosTheta * cosTheta));
}

// Both roots of a ray against a sphere at the origin, nearer first, or (-1, -1) for a miss. The
// direction need not be unit length.
float2 RayIntersectSphere(float3 origin, float3 direction, float radius)
{
    float a = dot(direction, direction);
    float b = 2.0 * dot(origin, direction);
    float c = dot(origin, origin) - (radius * radius);
    float discriminant = (b * b) - (4.0 * a * c);
    if (discriminant < 0.0)
    {
        return float2(-1.0, -1.0);
    }

    float root = sqrt(discriminant);
    return float2((-b - root) / (2.0 * a), (-b + root) / (2.0 * a));
}

// The nearest hit at or ahead of the origin, or -1 when the sphere lies wholly behind it (or is missed).
float NearestHit(float2 roots)
{
    if (roots.x < 0.0 && roots.y < 0.0)
    {
        return -1.0;
    }

    if (roots.x < 0.0)
    {
        return max(0.0, roots.y);
    }

    if (roots.y < 0.0)
    {
        return max(0.0, roots.x);
    }

    return max(0.0, min(roots.x, roots.y));
}

// The transmittance table's addressing (Bruneton 2008/2017): v walks the distance to the horizon, u the
// distance to the top of the air between its shortest and longest for that height. Raw uvs both ways.
void TransmittanceUvToParams(float2 uv, out float viewHeight, out float viewZenithCos)
{
    float horizon = sqrt((kTopRadiusKm * kTopRadiusKm) - (kBottomRadiusKm * kBottomRadiusKm));
    float rho = horizon * uv.y;
    viewHeight = sqrt((rho * rho) + (kBottomRadiusKm * kBottomRadiusKm));

    float dMin = kTopRadiusKm - viewHeight;
    float dMax = rho + horizon;
    float d = dMin + (uv.x * (dMax - dMin));
    viewZenithCos = d == 0.0
        ? 1.0
        : ((horizon * horizon) - (rho * rho) - (d * d)) / (2.0 * viewHeight * d);
    viewZenithCos = clamp(viewZenithCos, -1.0, 1.0);
}

float2 TransmittanceParamsToUv(float viewHeight, float viewZenithCos)
{
    float horizon = sqrt((kTopRadiusKm * kTopRadiusKm) - (kBottomRadiusKm * kBottomRadiusKm));
    float rho = sqrt(max(0.0, (viewHeight * viewHeight) - (kBottomRadiusKm * kBottomRadiusKm)));

    float discriminant = (viewHeight * viewHeight * ((viewZenithCos * viewZenithCos) - 1.0))
        + (kTopRadiusKm * kTopRadiusKm);
    float d = max(0.0, (-viewHeight * viewZenithCos) + sqrt(max(0.0, discriminant)));

    float dMin = kTopRadiusKm - viewHeight;
    float dMax = rho + horizon;
    return float2((d - dMin) / (dMax - dMin), rho / horizon);
}

// The table holds the square root, which spends a half-float's precision where transmittance is small.
float3 EncodeTransmittance(float3 t)
{
    return sqrt(t);
}

float3 DecodeTransmittance(float3 t)
{
    return t * t;
}

#endif
