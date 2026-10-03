// The sky's luminance in every direction from where the camera stands.
//
// Parameterised as a latitude and longitude around the viewer, but with the latitude packed towards the
// horizon, where the sky changes fastest and where a low sun does most of its work. The two halves of
// the vertical axis map above and below the horizon separately, so the horizon itself lands exactly on a
// texel boundary rather than being smeared across one.
//
// Built directly in world space with the vertical as the up axis: this game is played on a patch of
// ground where up is up, so world directions serve and there is no local frame to transform through.

#include "../Common/SkyAtmosphere.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uTransmittanceLut;
[[vk::binding(1, 0)]] SamplerState uTransmittanceLutSampler;

[[vk::binding(2, 0)]] Texture2D<float4> uMultiScatterLut;
[[vk::binding(3, 0)]] SamplerState uMultiScatterLutSampler;

[[vk::binding(4, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uSkyView;

struct Params
{
    float4 size;

    // world direction towards the sun.
    float4 sunDir;

    // illuminance outside the atmosphere.
    float4 sunColour;

    // view height km, sky light (read by the irradiance pass), airglow zenith radiance, cloud cover.
    float4 misc;
    // world direction towards the moon, and in w how much of its light the clear air scatters; the moon's
    // illuminance outside the atmosphere, nought by day.
    float4 moonDir;
    float4 moonColour;
};

[[vk::binding(5, 0)]] ConstantBuffer<Params> p;

#include "../Common/SkyLookup.slang"
#include "../Common/SkyMarch.slang"

// The airglow layer's height above the ground, in kilometres: the green oxygen line that carries most
// of a moonless sky's light is emitted around ninety.
static const float kAirglowHeightKm = 90.0;

// How much of the light on top of an overcast deck comes through it, scattered, to the ground below:
// a quarter, the share of the moon the host lets through as direct light in thin patches of the same
// cloud, so the two agree about how thick it is. A grey overcast sky is not dark next to a clear one --
// by day it is often the brighter of the two -- and a rain deck a tenth as bright as its moon read as
// black, taking every silhouette on the horizon with it.
static const float kDeckTransmission = 0.25;

// How much of a light the top of a cloud deck catches, by the light's height in the sky. Straight in
// proportion to its elevation while it is up, and never less than a glancing tenth -- but the sky is
// handed the sun wherever it is, under the horizon too, so that tenth fades out as the sun sinks through
// twilight rather than holding a night deck lit by a sun that set hours ago.
float DeckLight(float3 colour, float elevation)
{
    float source = dot(colour, float3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0));
    float glancing = 0.1 * smoothstep(-0.15, 0.1, elevation);
    return source * max(elevation, glancing);
}

// The part of a night sky the scattering march does not supply, added on top of what it does: the
// airglow, and a cloud deck when the night is overcast.
//
// Airglow is thin emission high up, so the path a ray takes through the layer, not the air under it,
// decides how bright it looks. Looking straight up, the ray crosses the layer's thickness once. Towards
// the horizon it grazes the shell and crosses several times more: the van Rhijn factor. What the air
// below then lets through is the march's own transmittance, which is what stops the factor from growing
// without bound at the horizon and reddens and dims the glow there as a real one is. Its colour is
// left a cool grey rather than the emission's green. By the time the eye has adapted to a sky this
// dark, the cones no longer resolve that green, and the resolve's night shift takes care of how it
// reads.
//
// An overcast night hides all of that behind a deck lit from above by whatever the moon and the glow put
// on it. Seen from below it is close to uniform: a flat grey-blue, a touch brighter towards the horizon
// where more of the deck is in view. The cover crossfades the clear sky into it rather than adding the
// two, so a rainy night has no moonlit blue gradient left to show through.
float3 NightSkyLuminance(float3 worldDir, bool aboveHorizon, float3 clearLuminance, float3 transmittance)
{
    float airglow = p.misc.z;
    float cover = saturate(p.misc.w);
    if (airglow <= 0.0 && cover <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float cosZenith = max(worldDir.z, 0.0);
    float shellRatio = kBottomRadiusKm / (kBottomRadiusKm + kAirglowHeightKm);
    float sinSquared = 1.0 - (cosZenith * cosZenith);
    float vanRhijn = rsqrt(max(1.0 - (shellRatio * shellRatio * sinSquared), 1e-3));

    const float3 airglowTint = float3(0.72, 0.84, 1.0);
    float3 glow = airglow * airglowTint * vanRhijn * transmittance;

    // What the deck is lit by from above -- the sun and the moon at their elevations, whole, since the
    // cloud is under them -- passed on down through it, and the glow.
    float deckLit =
        ((DeckLight(p.sunColour.rgb, p.sunDir.z) + DeckLight(p.moonColour.rgb, p.moonDir.z)) * kDeckTransmission)
        + (airglow * 1.5);
    float horizonLift = 1.0 + (0.15 * (1.0 - cosZenith));
    float3 overcast = deckLit * horizonLift * float3(0.80, 0.85, 0.95) / kPi;

    // Below the horizon the frame holds the horizon's colour and lighting only reads what the ground
    // sends back, so the ground half keeps a quarter: a dim bounce from the terrain, not a second sky.
    float ground = aboveHorizon ? 1.0 : 0.25;
    float3 clearNight = glow * ground;
    float3 overcastNight = overcast * ground;

    return lerp(clearNight, overcastNight - clearLuminance, cover);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.size.x) || px.y >= int(p.size.y))
    {
        return;
    }

    // Edge to edge: the first and last texels sit on the extremes of the range, which is what the
    // half-texel inset on the read side undoes.
    float2 uv = float2(px) / max(p.size.xy - 1.0, float2(1.0, 1.0));

    float viewHeight = p.misc.x;
    float horizonLength = sqrt(max(0.0, (viewHeight * viewHeight) - (kBottomRadiusKm * kBottomRadiusKm)));
    float beta = acos(clamp(horizonLength / viewHeight, -1.0, 1.0));
    float zenithHorizonAngle = kPi - beta;

    // Each half of the latitude packed quadratically towards the horizon at v = 0.5.
    float viewZenithAngle;
    if (uv.y < 0.5)
    {
        float coord = 1.0 - (2.0 * uv.y);
        viewZenithAngle = zenithHorizonAngle * (1.0 - (coord * coord));
    }
    else
    {
        float coord = (2.0 * uv.y) - 1.0;
        viewZenithAngle = zenithHorizonAngle + (beta * coord * coord);
    }

    float azimuth = 2.0 * kPi * uv.x;
    float sinZenith = sin(viewZenithAngle);
    float3 worldDir = float3(sinZenith * cos(azimuth), sinZenith * sin(azimuth), cos(viewZenithAngle));
    float3 worldPos = float3(0.0, 0.0, viewHeight);

    ScatteringResult sun = IntegrateScatteredLuminance(
        worldPos, worldDir, p.sunDir.xyz, p.sunColour.rgb, false, 32.0, true, true, true);
    float3 luminance = sun.luminance;

    // The moon as a second light through the same air, when there is one to see.
    if (any(p.moonColour.rgb > float3(0.0, 0.0, 0.0)) && p.moonDir.w > 0.0)
    {
        ScatteringResult moon = IntegrateScatteredLuminance(
            worldPos, worldDir, p.moonDir.xyz, p.moonColour.rgb * p.moonDir.w, false, 32.0, true, true, true);
        luminance += moon.luminance;
    }

    luminance += NightSkyLuminance(
        worldDir, viewZenithAngle < zenithHorizonAngle, luminance, sun.transmittance);

    float greyTransmittance = (sun.transmittance.r + sun.transmittance.g + sun.transmittance.b) / 3.0;
    uSkyView.Store(px, float4(luminance, greyTransmittance));
}
