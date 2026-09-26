// Reading the sky-view table by world direction: the inverse of the mapping the table was built with,
// and the two ways of reading it.
//
// The mapping is shared between the pass that projects the sky onto spherical harmonics and the lighting
// pass that draws it, because a table read with a different mapping than it was written with is a sky
// that is subtly the wrong shape.
//
// SampleSkyView answers what light arrives from a direction, which is what lighting a surface needs.
// SampleSkyBackground answers what the frame should show there, which below the horizon is not the same
// question -- see its own comment.
//
// Each comes in two forms, for a host that binds the table as a texture and a sampler apart and one that
// binds it as a single combined descriptor. Only the fetch differs; the arithmetic below is shared, so
// the two cannot read the table differently.

#ifndef OPENIGI_SKY_VIEW_INCLUDED
#define OPENIGI_SKY_VIEW_INCLUDED

#include "SkyAtmosphere.slang"

// The table's own parameterisation, before the half-texel inset: azimuth across, and a latitude packed
// towards the horizon from both sides, so that the horizon itself is v = 0.5.
float2 SkyViewUv(float3 worldDir, float viewHeight)
{
    float horizonLength = sqrt(
        max(0.0, (viewHeight * viewHeight) - (kBottomRadiusKm * kBottomRadiusKm)));
    float beta = acos(clamp(horizonLength / viewHeight, -1.0, 1.0));
    float zenithHorizonAngle = kPi - beta;

    float viewZenithAngle = acos(clamp(worldDir.z, -1.0, 1.0));

    float v;
    if (viewZenithAngle < zenithHorizonAngle)
    {
        float coord = sqrt(max(0.0, 1.0 - (viewZenithAngle / zenithHorizonAngle)));
        v = (1.0 - coord) * 0.5;
    }
    else
    {
        float coord = sqrt(max(0.0, (viewZenithAngle - zenithHorizonAngle) / max(beta, 1e-6)));
        v = (coord * 0.5) + 0.5;
    }

    float azimuth = atan2(worldDir.y, worldDir.x);
    if (azimuth < 0.0)
    {
        azimuth += 2.0 * kPi;
    }

    return float2(azimuth / (2.0 * kPi), v);
}

// The same half-texel inset the table was written with, so the extremes line up.
float2 SkyViewInset(float2 uv, float2 size)
{
    return (uv * ((size - 1.0) / size)) + (0.5 / size);
}

// The centre of the last row above the horizon, in the pre-inset coordinates: the inset maps v to
// v(N-1)/N + 1/2N, so row k's centre is at v = k/(N-1).
//
// Holding the read there is one clamp doing two jobs. The table's lower half is the planet's own
// surface: those rays stop on the ground a few kilometres out, and the pass that fills the table adds no
// ground radiance, so that half holds nothing but the little which scattered along a short ray. As an
// answer to what light arrives from below, that is honest. Drawn, it is a dark blue band along the
// skyline -- and with the far plane at a hundred kilometres the level's own terrain stops well short of
// the horizon wherever the ground falls away, so that band is exactly what the frame puts between the
// last ridge and the horizon. The clamp keeps the planet's surface out of the frame, and it keeps the
// surface from smearing into the pixels just above the skyline: v = 0.5 is a texel BOUNDARY, with the
// last row of sky above it and the first row of ground below, so bilinear anywhere near the horizon was
// mixing the two.
//
// Below the horizon the frame then shows the horizon's own colour, holding its azimuth, which is the
// answer the original gives as well: its dome carries a skirt of the horizon ring's colours half a
// radius down, "so that turning the camera below the horizon shows sky rather than the void"
// (sub_52CC40).
float2 SkyViewHeldAboveHorizon(float2 uv, float rows)
{
    uv.y = min(uv.y, (floor(rows * 0.5) - 1.0) / max(rows - 1.0, 1.0));
    return uv;
}

float3 SampleSkyViewUv(Texture2D<float4> skyView, SamplerState skyViewSampler, float2 uv)
{
    uint width;
    uint height;
    skyView.GetDimensions(width, height);
    return skyView.SampleLevel(
        skyViewSampler, SkyViewInset(uv, float2(float(width), float(height))), 0.0).rgb;
}

float3 SampleSkyView(
    Texture2D<float4> skyView,
    SamplerState skyViewSampler,
    float3 worldDir,
    float viewHeight)
{
    return SampleSkyViewUv(skyView, skyViewSampler, SkyViewUv(worldDir, viewHeight));
}

// The sky as the FRAME shows it, which below the horizon is not the question lighting asks. See
// SkyViewHeldAboveHorizon.
float3 SampleSkyBackground(
    Texture2D<float4> skyView,
    SamplerState skyViewSampler,
    float3 worldDir,
    float viewHeight)
{
    uint width;
    uint height;
    skyView.GetDimensions(width, height);
    float2 uv = SkyViewHeldAboveHorizon(SkyViewUv(worldDir, viewHeight), float(height));
    return SampleSkyViewUv(skyView, skyViewSampler, uv);
}


#endif
