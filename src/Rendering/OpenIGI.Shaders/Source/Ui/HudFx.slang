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
//   - a hit tears the picture on the side it came from: slices of it jump sideways, its colours split, it blurs and greys
//     -- no paint over the picture;
//   - the weapon wheel, open, takes the world behind it out of time: drained to black and white, pulled in towards the
//     middle in a radial smear, darkened at its edges and grained like film.
//
// Who is watching the player is not this pass's: the display marks each watcher itself (HudDetection).
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

// The picture under the display, as the glass copied it this frame (linear light), and that blurred; when there is no copy
// the layer stands in and damageColour.w says so.
[[vk::binding(5, 0)]] Texture2D<float4> uScene;
[[vk::binding(6, 0)]] Texture2D<float4> uSceneBlur;

struct HudFxParams
{
    // width, height, 1/width, 1/height.
    float4 frame;

    // intensity, curvature, fringe in pixels, glow.
    float4 visor;

    // scanlines per 1080 lines, scanline strength, appear, seconds.
    float4 lines;

    // shake x, shake y in pixels, how far the weapon wheel is open, unused.
    float4 shake;

    // the hit's wash in each corner: top left, top right, bottom left, bottom right.
    float4 damage;

    // the hit's colour, and the picture under the display: nought where it cannot be read, one where it can and the target
    // encodes as it stores, two where it can and the target stores what it is given, so the picture goes out encoded.
    float4 damageColour;

    // unused.
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

// Film grain's noise: an integer hash of the pixel and the tick, free of the bands a sine hash leaves.
float Grain(float2 pixel, float tick)
{
    uint h = (uint(pixel.x) * 1664525u) ^ (uint(pixel.y) * 1013904223u) ^ (uint(tick) * 2654435761u);
    h ^= h >> 16;
    h *= 0x7feb352du;
    h ^= h >> 15;
    h *= 0x846ca68bu;
    h ^= h >> 16;
    return float(h) * (1.0 / 4294967295.0);
}

// Linear light to the sRGB encoding, for a target that stores what it is given.
float3 LinearToSrgb(float3 c)
{
    c = max(c, 0.0);
    return select(c <= 0.0031308, c * 12.92, (1.055 * pow(c, 1.0 / 2.4)) - 0.055);
}

// The picture under the display, split red from blue along a direction.
float3 SceneSplit(float2 uv, float2 split)
{
    float r = uScene.SampleLevel(uHudSampler, uv + split, 0.0).r;
    float g = uScene.SampleLevel(uHudSampler, uv, 0.0).g;
    float b = uScene.SampleLevel(uHudSampler, uv - split, 0.0).b;
    return float3(r, g, b);
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

    float2 q = input.uv;
    float aspect = p.frame.x * p.frame.w;
    float2 fromMiddle = (q - 0.5) * float2(aspect, 1.0);
    float time = p.lines.w;
    float tick = floor(time * 24.0);

    // The hit: how hard it lands here, reaching in from the corners it came from.
    float2 qa = q * float2(aspect, 1.0);
    float2 tr = qa - float2(aspect, 0.0);
    float2 bl = qa - float2(0.0, 1.0);
    float2 br = qa - float2(aspect, 1.0);
    float hit = (p.damage.x * exp(-dot(qa, qa) * 2.6))
        + (p.damage.y * exp(-dot(tr, tr) * 2.6))
        + (p.damage.z * exp(-dot(bl, bl) * 2.6))
        + (p.damage.w * exp(-dot(br, br) * 2.6));
    hit = saturate(hit * 1.6) * intensity;

    // The picture under the display, torn where the hit lands: slices slid sideways, split, blurred, greyed and dimmed,
    // laid back under the display only there. Without the picture, the dimming alone, as black laid over.
    float tear = 0.0;
    float3 torn = float3(0.0, 0.0, 0.0);
    if (hit > 0.0 && p.damageColour.w > 0.5)
    {
        float slice = floor((q.y * 1080.0) / (14.0 + (10.0 * Hash(floor(q.y * 31.0) + tick))));
        float jump = step(0.62, Hash((slice * 3.17) + tick)) * (Hash(slice + (tick * 1.31)) - 0.5) * 0.05 * hit;
        float2 suv = q + float2(jump, 0.0);
        float2 split = normalize(fromMiddle + 1e-4) * float2(1.0 / aspect, 1.0) * (0.012 * hit);
        float3 picture = lerp(SceneSplit(suv, split), uSceneBlur.SampleLevel(uHudSampler, suv, 0.0).rgb, 0.4 * hit);
        float grey = dot(picture, float3(0.2126, 0.7152, 0.0722));
        picture = lerp(picture, float3(grey, grey, grey), 0.5 * hit) * (1.0 - (0.12 * hit));
        torn = picture + (p.damageColour.rgb * (grey * 0.25 * hit));
        if (p.damageColour.w > 1.5)
        {
            torn = LinearToSrgb(torn);
        }

        tear = saturate(hit * 1.5);
    }

    // The weapon wheel: the world behind it drained, smeared in towards the middle, darkened at its edges and grained.
    float wheel = saturate(p.shake.z) * intensity;
    if (wheel > 0.0 && p.damageColour.w > 0.5)
    {
        float2 towards = q - 0.5;
        float3 smear = float3(0.0, 0.0, 0.0);
        const int Taps = 10;
        for (int i = 0; i < Taps; i++)
        {
            float k = 1.0 - ((float(i) / float(Taps - 1)) * 0.07 * wheel);
            smear += uScene.SampleLevel(uHudSampler, 0.5 + (towards * k), 0.0).rgb;
        }

        smear *= 1.0 / float(Taps);
        smear = lerp(smear, uSceneBlur.SampleLevel(uHudSampler, q, 0.0).rgb, 0.35 * wheel);
        float grey = dot(smear, float3(0.2126, 0.7152, 0.0722));
        grey = grey * grey * (3.0 - (2.0 * grey));
        float3 drained = lerp(smear, float3(grey, grey * 0.985, grey * 0.96), 0.92 * wheel);
        float edge = smoothstep(0.25, 0.85, length(towards * float2(aspect, 1.0)));
        drained *= (1.0 - (0.3 * wheel)) * (1.0 - (0.6 * wheel * edge));
        drained += (Grain(q * p.frame.xy, tick) - 0.5) * 0.025 * wheel;
        if (p.damageColour.w > 1.5)
        {
            drained = LinearToSrgb(drained);
        }

        torn = lerp(torn * tear, drained, wheel) / max(max(tear, wheel), 1e-4);
        tear = max(tear, wheel);
    }

    float dim = p.damageColour.w > 0.5 ? 0.0 : hit * 0.15;
    float4 layered = lerp(Layer(input.uv), colour, intensity);
    layered.rgb += torn * (tear * (1.0 - layered.a));
    layered.a += max(tear, dim) * (1.0 - layered.a);
    return layered;
}
