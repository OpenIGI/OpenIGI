// The glint of a sniper's scope: the sun caught in the glass of a rifle aimed at the player, drawn as light added over the
// finished picture.
//
// What a real glint looks like, and nothing more: a tiny white-hot point, the wide soft bloom an eye or a lens throws round
// a light far brighter than what is behind it, and a short faint horizontal flare. It does not spin or pulse; it wavers a
// little and irregularly, as a rifle held by hand moves the reflection about, and swells with how sure the sniper is.
//
// Up to seven glints a draw, each in screen pixels; the draw is a full-screen triangle, and a pixel too far from every glint
// adds nothing.

#include "../Common/Fullscreen.slang"
#include "../Common/PushConstants.slang"

struct ScopeGlintParams
{
    // width, height in pixels, seconds, how many glints.
    float4 frame;

    // each: x, y in pixels, radius in pixels, strength nought to one.
    float4 glints[7];
};

OPENIGI_PUSH_CONSTANT(ScopeGlintParams, p);

typedef FullscreenVaryings Varyings;

[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

// Smooth noise in one dimension: the hand's waver, not a beat.
float Noise(float x)
{
    float i = floor(x);
    float f = frac(x);
    float a = frac(sin(i * 12.9898) * 43758.5453);
    float b = frac(sin((i + 1.0) * 12.9898) * 43758.5453);
    return lerp(a, b, f * f * (3.0 - (2.0 * f)));
}

// One glint's light at a point, in units of its radius from its centre.
float3 Glint(float2 d, float strength, float seconds, float seed)
{
    float r2 = dot(d, d);
    float r = sqrt(r2);

    // The waver of a hand-held rifle: two slow noises, never a regular beat.
    float waver = 0.72 + (0.2 * Noise((seconds * 3.1) + (seed * 7.0))) + (0.08 * Noise((seconds * 11.0) + seed));
    float k = strength * waver;

    float point = exp(-r2 * 70.0) * 3.0;
    float bloom = (exp(-r2 * 2.8) * 0.32) + (exp(-r * 1.3) * 0.1);
    float flare = exp(-abs(d.y) * 40.0) * exp(-abs(d.x) * 1.5) * 0.3;

    float3 sun = float3(1.0, 0.95, 0.86);
    return k * ((sun * point) + (sun * bloom) + (float3(0.92, 0.95, 1.0) * flare));
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float2 pixel = input.uv * p.frame.xy;
    int count = min(int(p.frame.w), 7);
    float3 light = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < count; i++)
    {
        float4 g = p.glints[i];
        if (g.z <= 0.0 || g.w <= 0.0)
        {
            continue;
        }

        float2 d = (pixel - g.xy) / g.z;

        // Past the stripe's reach and off its line, nothing.
        if (abs(d.x) > 6.0 || (abs(d.y) > 4.0))
        {
            continue;
        }

        light += Glint(d, g.w, p.frame.z, float(i) * 1.618);
    }

    // Added: light over the picture, never darkening it.
    return float4(light, 0.0);
}
