// The surface cache's bounce probes: one texel in sixteen of the lighting atlas carries one. What a probe
// gathers over its hemisphere, where in each trace cell it samples on a given frame, and how the combine reads
// a texel's probes back -- only those on the texel's own page and plane, each smoothed with its neighbours.
//
// The host must include this after declaring the cache's Globals (the atlas size, the probe grid's width and
// height, the jitter stride, the clipmaps and the lookup), the probe positions as uProbePos, the gathered
// bounce as uIndirectRead and the last iteration's light as uPrevious, each with its sampler; and after
// SoftwareTrace.slang, SurfaceCache.slang and Hemisphere.slang, with kPi, kHemisphereResolution,
// kTracesPerProbe, RadicalInverse, ShBasis and ShIrradianceTransfer defined.

#ifndef OPENIGI_CARD_PROBES_INCLUDED
#define OPENIGI_CARD_PROBES_INCLUDED

// Whether a probe stands near enough to a texel's plane, judged against how far away it is, to speak for
// that texel's surface: one that is mostly off the plane sits on some other surface around a corner.
static const float kProbePlaneTolerance = 0.25776;

float ProbePlaneAccept(float3 texelWorld, float3 texelNormal, float3 probeWorld)
{
    float3 offset = texelWorld - probeWorld;
    float offPlane = abs(dot(offset, texelNormal));
    return (offPlane / (length(offset) + 0.01)) < kProbePlaneTolerance ? 1.0 : 0.0;
}

// The irradiance a probe gathered, read at its own texel's centre.
float3 ProbeIrradiance(int2 probeCoord)
{
    float2 at = (float2(probeCoord * 4) + 0.5) / uAtlasSize.xy;
    return uIndirectRead.SampleLevel(uIndirectReadSampler, at, 0.0).rgb;
}

// Whether a probe grid coordinate holds a real probe on the same page as the texel asking, and if so
// where it stands. Probes of a neighbouring page sit on unrelated geometry however close they pack.
bool ProbeUsable(int2 probeCoord, int2 pageMin, int2 pageMax, out float3 position)
{
    position = float3(0.0, 0.0, 0.0);
    if (any(probeCoord < int2(0, 0)) || probeCoord.x >= uProbeWidth || probeCoord.y >= uProbeHeight)
    {
        return false;
    }

    int2 texel = probeCoord * 4;
    if (any(texel < pageMin) || any(texel >= pageMax))
    {
        return false;
    }

    float4 record = uProbePos[(probeCoord.y * uProbeWidth) + probeCoord.x];
    position = record.xyz;
    return record.w >= 0.5;
}

// The probe's four edge neighbours on the grid.
static const int2 kProbeNeighbour[4] = { int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };

// A probe's irradiance smoothed with whichever of its edge neighbours share the texel's surface: the
// probe itself counts twice, each accepted neighbour once.
float3 FilteredProbe(int2 probeCoord, int2 pageMin, int2 pageMax, float3 texelWorld, float3 texelNormal)
{
    float3 sum = ProbeIrradiance(probeCoord) * 2.0;
    float weight = 2.0;

    for (int k = 0; k < 4; k++)
    {
        int2 neighbour = probeCoord + kProbeNeighbour[k];
        float3 position;
        if (!ProbeUsable(neighbour, pageMin, pageMax, position))
        {
            continue;
        }

        if (ProbePlaneAccept(texelWorld, texelNormal, position) <= 0.0)
        {
            continue;
        }

        sum += ProbeIrradiance(neighbour);
        weight += 1.0;
    }

    return sum / weight;
}

// Where in each trace cell the hemisphere is sampled this frame. Fixed at the centre for the one-shot
// load pass, so it stays deterministic; jittered per probe per frame otherwise, so the accumulation over
// frames integrates far more directions than the sixteen a single frame traces.
//
// A low-discrepancy Hammersley point along the frame index, rotated per probe, stands in for a blue-noise
// jitter, since there is no blue-noise texture here.
float2 ProbeCenter(int2 probeCoord, int frame)
{
    if (frame <= 0)
    {
        return float2(0.5, 0.5);
    }

    uint seed = (uint(probeCoord.x) * 73856093u) ^ (uint(probeCoord.y) * 19349663u);
    float rotation = float(seed & 0xffffu) / 65536.0;

    // The sequence's LENGTH is not the accumulation's depth, and taking the frame index modulo uMaxFrames
    // tied the two together: only uMaxFrames distinct jitters ever existed, so a tile re-traced for the
    // thousandth time was re-tracing one of eight patterns it had already seen. The running average could
    // not converge to the hemisphere's true value -- it settled into a LIMIT CYCLE of that period
    // instead, and since alpha floors at 1/(1+uMaxFrames) each re-trace moved the tile an eighth of the
    // way around the cycle. Under the relight's amortisation, which walks a contiguous slice of tiles per
    // frame, neighbouring tiles sit at neighbouring phases of that cycle, and the whole thing reads on
    // the wall as a slow ripple travelling across every surface -- the heat-haze shimmer, present with
    // the camera standing perfectly still.
    //
    // The same Hammersley-along-the-frame-index the screen gather's own FrameJitter uses, over a sequence
    // long enough that it does not repeat in any session: the golden-ratio sequence in one axis and the
    // radical inverse in the other.
    //
    // Indexed by the tile's own trace count, not the frame: a tile is traced only on the frames of one
    // phase of the amortisation spread, so the frame number it saw always had the same low bits -- and the
    // radical inverse's leading bits ARE those low bits, which pinned every probe's elevation jitter inside
    // one band of the hemisphere for good: a fixed bias per probe that no accumulation could average out.
    int n = (frame / max(uJitterStride, 1)) & 0xffff;
    float2 point = float2(frac(float(n) * 0.61803398875), RadicalInverse(n));

    return frac(point + float2(rotation, frac(rotation * 1.61803399)));
}

// The bounce at one probe: sixteen rays over the hemisphere on a jittered four-by-four grid, uniform in
// solid angle, each one bringing back the cache's final light where it struck a surface. The samples are
// projected onto two bands of spherical harmonics and turned into irradiance by the cosine lobe's
// transfer, so the cosine is applied once, analytically, rather than by the sampling.
float3 Gather(float3 world, float3 normal, float2 center)
{
    if (uShadowLevels <= 0 || uLookupBuckets <= 0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    HemisphereBasis basis = BasisAround(normal);
    float finest = uClipmapOrigin[0].w;

    float4 red = float4(0.0, 0.0, 0.0, 0.0);
    float4 green = float4(0.0, 0.0, 0.0, 0.0);
    float4 blue = float4(0.0, 0.0, 0.0, 0.0);

    for (int cell = 0; cell < kTracesPerProbe; cell++)
    {
        float2 square = (float2(float(cell % kHemisphereResolution), float(cell / kHemisphereResolution))
            + center) / float(kHemisphereResolution);
        float phi = 2.0 * kPi * square.x;
        float cosTheta = square.y;
        float sinTheta = sqrt(max(0.0, 1.0 - (cosTheta * cosTheta)));
        float3 direction = Orient(basis, float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta));

        float3 radiance = float3(0.0, 0.0, 0.0);
        float3 hit;
        if (VisibilityAt(world, normal, direction, hit) <= 0.5)
        {
            float2 found;
            float2 litFound;
            float agreement;
            if (LookupCardHit(hit, direction, found, litFound, agreement))
            {
                // A hit this close is most likely the surface's own skin, whose light is what this probe
                // is computing; fade it out over the first couple of voxels.
                float near = smoothstep(1.5 * finest, 2.0 * finest, length(hit - world));
                radiance = uPrevious.SampleLevel(uPreviousSampler, litFound, 0.0).rgb * near;
            }
        }

        float4 basisHere = ShBasis(direction);
        red += basisHere * radiance.r;
        green += basisHere * radiance.g;
        blue += basisHere * radiance.b;
    }

    // Uniform hemisphere sampling: each sample weighs the inverse of its density, two pi, and the sixteen
    // are averaged.
    float scale = (2.0 * kPi) / float(kTracesPerProbe);
    float4 transfer = ShIrradianceTransfer(normal);
    float3 irradiance = float3(dot(red, transfer), dot(green, transfer), dot(blue, transfer)) * scale;
    return max(irradiance, float3(0.0, 0.0, 0.0));
}

#endif
