// The remastered heads-up display laid over the picture as if projected on a visor.
//
// The display is drawn into a layer of its own (premultiplied colour and coverage, as every sprite blends into a transparent
// layer -- see Post/Compose.slang) and this lays it over whatever the frame's interface draws into, blended as premultiplied
// "over". On the way it does what a projection on curved glass does to an image:
//
//   - the shake of a hit moves it bodily;
//   - the visor bends it, a little more towards the corners;
//   - the projection splits red and blue from green towards the edges;
//   - scanlines run through it, and a faint brighter band rolls down it;
//   - when it appears it flickers in band by band;
//   - its own glow, the blurred layer's bright parts, is laid back over it, so bright figures bloom and the glass stays clear;
//   - a hit washes the corners it came from;
//   - being noticed tints the edges, creeping in as the threat grows.
//
// At nought intensity every one of those is nothing and the layer is laid over exactly as drawn.

#include "../Common/Fullscreen.slang"
#include "../Common/PushConstants.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uHud;
[[vk::binding(1, 0)]] SamplerState uHudSampler;

// The layer blurred, finest first: its glow.
[[vk::binding(2, 0)]] Texture2D<float4> uGlow0;
[[vk::binding(3, 0)]] Texture2D<float4> uGlow1;
[[vk::binding(4, 0)]] Texture2D<float4> uGlow2;

struct HudFxParams
{
    // width, height, 1/width, 1/height.
    float4 frame;

    // intensity, curvature, fringe in pixels, glow.
    float4 visor;

    // scanlines per 1080 lines, scanline strength, appear, seconds.
    float4 lines;

    // shake x, shake y in pixels, unused, unused.
    float4 shake;

    // the hit's wash in each corner: top left, top right, bottom left, bottom right.
    float4 damage;

    // the wash's colour, unused.
    float4 damageColour;

    // how near the player is to being seen, nought to one, and the tint it brings in from the edges.
    float4 alert;
};

OPENIGI_PUSH_CONSTANT(HudFxParams, p);

typedef FullscreenVaryings Varyings;

[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

float Hash(float n)
{
    return frac(sin(n * 12.9898) * 43758.5453);
}

// The layer at a place, nothing off its edges.
float4 Layer(float2 uv)
{
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    return uHud.SampleLevel(uHudSampler, uv, 0.0);
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float2 uv = input.uv;
    float intensity = saturate(p.visor.x);
    if (intensity <= 0.0)
    {
        return Layer(uv);
    }

    // The hit's shake moves the whole display.
    uv -= p.shake.xy * p.frame.zw * intensity;

    // The visor's bend: a point is taken from further out the further it is from the middle, so the display bows towards the
    // viewer. Kept small, so the glass panels' blurred backdrop, sampled where they were drawn, stays under them.
    float2 centred = (uv * 2.0) - 1.0;
    float bend = dot(centred, centred);
    centred *= 1.0 + (p.visor.y * intensity * bend);
    uv = (centred * 0.5) + 0.5;

    // The projection's fringe: red taken from further out and blue from further in, more towards the edges.
    float2 fringe = centred * (p.visor.z * intensity * bend * 0.5) * p.frame.zw;
    float4 red = Layer(uv + fringe);
    float4 green = Layer(uv);
    float4 blue = Layer(uv - fringe);
    float4 colour = float4(red.r, green.g, blue.b, max(green.a, max(red.a, blue.a)));

    // Its glow: the layer blurred at three widths, bright figures blooming the most.
    float4 glow = (uGlow0.SampleLevel(uHudSampler, uv, 0.0) * 0.5)
        + (uGlow1.SampleLevel(uHudSampler, uv, 0.0) * 0.3)
        + (uGlow2.SampleLevel(uHudSampler, uv, 0.0) * 0.2);
    // Only what is bright blooms -- the figures, the bars, the pictures -- not the glass, whose blurred picture of the frame
    // would otherwise be laid over itself again and wash flat.
    float3 bright = max(glow.rgb - 0.45, 0.0) * (1.0 / 0.55);
    float3 bloom = bright * (p.visor.w * intensity);
    colour.rgb += bloom;
    colour.a = saturate(colour.a + (dot(bloom, float3(0.299, 0.587, 0.114)) * 0.5));

    // Scanlines, and a faint brighter band rolling down.
    float y = uv.y * p.frame.y;
    float line = 0.5 - (0.5 * cos(y * (p.lines.x / 1080.0) * 6.2831853));
    float band = exp(-pow((frac(uv.y - (p.lines.w * 0.08)) - 0.5) * 9.0, 2.0));
    float shade = (1.0 - (p.lines.y * intensity * line)) + (0.06 * intensity * band);
    colour.rgb *= shade;

    // Flickering in: each band of rows comes up at its own moment and stutters until it holds.
    float appear = saturate(p.lines.z);
    if (appear < 1.0)
    {
        float row = floor(y / 6.0);
        float stutter = Hash(row + floor(p.lines.w * 30.0));
        float up = step(Hash(row * 1.37), appear) * lerp(0.35, 1.0, step(0.5 - (appear * 0.5), stutter));
        colour *= up;
    }

    // The hit's wash, from the corners it came from.
    float2 q = input.uv;
    float wash = (p.damage.x * saturate(1.0 - length(q - float2(0.0, 0.0)) * 1.4))
        + (p.damage.y * saturate(1.0 - length(q - float2(1.0, 0.0)) * 1.4))
        + (p.damage.z * saturate(1.0 - length(q - float2(0.0, 1.0)) * 1.4))
        + (p.damage.w * saturate(1.0 - length(q - float2(1.0, 1.0)) * 1.4));
    wash = saturate(wash) * intensity;
    colour.rgb = lerp(colour.rgb, p.damageColour.rgb * wash, wash * 0.6) + (p.damageColour.rgb * wash * 0.4);
    colour.a = saturate(max(colour.a, wash * 0.65));

    // Being noticed: the visor's edges take the alert's tint, creeping in as the threat grows.
    float threat = saturate(p.alert.x) * intensity;
    if (threat > 0.0)
    {
        float2 edge = abs((input.uv * 2.0) - 1.0);
        float rim = smoothstep(1.0 - (0.35 * threat), 1.0, max(edge.x, edge.y * 1.15));
        float tint = rim * threat * 0.5;
        colour.rgb += p.alert.yzw * tint;
        colour.a = saturate(max(colour.a, tint));
    }

    return lerp(Layer(input.uv), colour, intensity);
}
