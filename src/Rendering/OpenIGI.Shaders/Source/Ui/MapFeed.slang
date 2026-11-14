// The remastered computer's map as a satellite feed on a tube: the overhead picture turned into one colour's display.
//
// The picture is the level drawn straight down, already finished. This pass reads a copy of it and lays the feed over it:
//
//   - its brightness alone is kept, with the fine grain of the ground smoothed out of it and the shapes on it -- roofs,
//     roads, walls -- set off against their surroundings, so what stands on the ground reads before the ground does;
//   - that brightness is drawn in three stops of the title's own colour, held well under the display's own marks and
//     text, which are drawn over it brighter;
//   - the tube: the picture bows as the glass does and is cut off round at its corners; the beam leaves scanlines, thinner
//     where it is bright; the grille leaves fine columns; each bright edge trails a little to the right as phosphor does;
//     what is bright spreads a soft halo; a refresh line runs down it every few seconds with the picture freshly lit
//     behind it; a faint bar of hum rolls up it; it flickers a hair, it is grained, and now and then a line of it slips
//     sideways as a signal does; the glass catches a little light at its top;
//   - as it comes up it tears, breaks into blocks and surges, and a bright refresh band runs down it once, the picture
//     under the band not yet lit;
//   - under a text page it dims and softens; a change of page disturbs it for a moment.
//
// At nought amount it is the picture as it was.

#include "../Common/Fullscreen.slang"
#include "../Common/PushConstants.slang"

// The picture as it was copied this frame (linear light), and that blurred: a little, and a great deal.
[[vk::binding(0, 0)]] Texture2D<float4> uScene;
[[vk::binding(1, 0)]] SamplerState uSceneSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uSceneSoft;
[[vk::binding(3, 0)]] Texture2D<float4> uSceneWide;

struct MapFeedParams
{
    // width, height, 1/width, 1/height.
    float4 frame;

    // amount, boot, sweep, where the sweep is down the screen.
    float4 signal;

    // seconds, curvature, scanlines per 1080 lines, scanline strength.
    float4 tube;

    // how far a text page dims it, the page-change burst, the target (one: it encodes as it stores; two: it stores what it
    // is given, so the picture goes out encoded), grain.
    float4 look;

    // the three stops, linear light.
    float4 shadow;
    float4 body;
    float4 highlight;
};

OPENIGI_PUSH_CONSTANT(MapFeedParams, p);

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

// Grain's noise: an integer hash of the pixel and the tick, free of the bands a sine hash leaves.
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

float3 LinearToSrgb(float3 c)
{
    c = max(c, 0.0);
    return select(c <= 0.0031308, c * 12.92, (1.055 * pow(c, 1.0 / 2.4)) - 0.055);
}

float Luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// The ground's light at a place as the feed shows it: the fine grain smoothed away, the shapes set off against what is
// round them, a dark level lifted and a bright one held.
float Ground(float2 uv, float soften)
{
    float sharp = Luminance(uScene.SampleLevel(uSceneSampler, uv, 0.0).rgb);
    float soft = Luminance(uSceneSoft.SampleLevel(uSceneSampler, uv, 0.0).rgb);
    float wide = Luminance(uSceneWide.SampleLevel(uSceneSampler, uv, 0.0).rgb);
    float light = lerp(lerp(sharp, soft, 0.5), soft, soften);
    light += (soft - wide) * 0.6;
    float gain = clamp(0.16 / (wide + 0.04), 0.8, 2.6);
    return max(light, 0.0) * lerp(1.0, gain, 0.55);
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float2 q = input.uv;
    float amount = saturate(p.signal.x);
    float3 picture = uScene.SampleLevel(uSceneSampler, q, 0.0).rgb;
    bool encode = p.look.z > 1.5;
    if (amount <= 0.0)
    {
        return float4(encode ? LinearToSrgb(picture) : picture, 1.0);
    }

    float time = p.tube.x;
    float tick = floor(time * 24.0);
    float boot = saturate(p.signal.y);
    float burst = saturate(p.look.y);
    float disturbed = saturate(boot + burst);
    float aspect = p.frame.x * p.frame.w;

    // The glass's bow, as the display over it is bent (Ui/HudFx.slang), arriving with the feed.
    float2 centred = (q * 2.0) - 1.0;
    float bend = dot(centred, centred);
    float2 uv = ((centred * (1.0 + (p.tube.y * amount * bend))) * 0.5) + 0.5;

    // The tube's face: cut off round at the corners, with a soft edge.
    float2 face = abs((uv * 2.0) - 1.0);
    float radius = 0.06;
    float2 corner = max(face - (1.0 - radius), 0.0) * float2(aspect, 1.0);
    float outside = (length(corner) - radius) + max(max(face.x, face.y) - 1.0, 0.0);
    float inside = 1.0 - smoothstep(-0.004, 0.004, outside);

    // A signal slipping: now and then a few rows slide sideways for a moment; coming up, slices of it jump.
    float rowBand = floor(uv.y * 54.0);
    float slipWindow = step(0.975, Hash(floor(time * 2.0) + 7.0)) * step(0.85, Hash(rowBand + floor(time * 9.0)));
    float slip = slipWindow * (Hash(rowBand * 1.7 + tick) - 0.5) * 0.01;
    float slice = floor((uv.y * 1080.0) / (10.0 + (22.0 * Hash(floor(uv.y * 23.0) + tick))));
    float jump = step(0.55, Hash((slice * 3.17) + tick)) * (Hash(slice + (tick * 1.31)) - 0.5) * 0.09 * disturbed;
    uv.x += (slip * amount) + jump;

    // The ground, and the same a beam's width to the left: a bright edge trails to its right as phosphor does.
    float dim = saturate(p.look.x);
    float soften = dim * 0.85;
    float light = Ground(uv, soften);
    float trail = Ground(uv - float2(3.5 * p.frame.z, 0.0), soften);
    light = max(light, lerp(light, trail, 0.35));

    // As the eye reads it, with a photograph's gentle contrast.
    float v = pow(saturate(light), 1.0 / 2.2);
    v = saturate(((v - 0.5) * 1.08) + 0.5);
    v = lerp(v, v * v * (3.0 - (2.0 * v)), 0.6);

    // Blocks of the picture gone to noise as it comes up.
    float2 block = floor(uv * float2(48.0, 27.0));
    float broken = step(1.0 - (0.5 * disturbed), Hash(block.x + (block.y * 57.0) + (tick * 3.1)));
    v = lerp(v, Hash(block.x * 1.3 + block.y * 91.0 + tick), broken * 0.8);

    // One colour, three stops, kept under the marks drawn over it.
    float3 colour = v < 0.6
        ? lerp(p.shadow.rgb, p.body.rgb, v * (1.0 / 0.6))
        : lerp(p.body.rgb, p.highlight.rgb, (v - 0.6) * (1.0 / 0.4));
    colour *= 0.72;

    // The halo of a lit screen: what is bright spreads.
    float soft = Luminance(uSceneSoft.SampleLevel(uSceneSampler, uv, 0.0).rgb);
    colour += p.body.rgb * (max(pow(saturate(soft * 1.6), 1.0 / 2.2) - 0.55, 0.0) * 0.5);

    // The beam's scanlines, a whole number of pixels apart so they never beat against the screen's own rows, thinner
    // where the beam is bright; and the grille's fine columns.
    float period = max(3.0, floor(p.frame.y / 360.0));
    float row = frac((uv.y * p.frame.y) / period);
    float beam = 0.5 - (0.5 * cos(row * 6.2831853));
    float lines = lerp(1.0 - (p.tube.w * 2.2), 1.0, pow(beam, lerp(1.6, 0.6, v)));
    float column = frac((q.x * p.frame.x) / period);
    float grille = 1.0 - (0.14 * step(0.66, column));
    colour *= lines * grille;

    // The refresh line, every few seconds: bright where it is, and the picture behind it freshly lit, fading until it
    // comes round again.
    float since = frac((time * 0.22) - uv.y);
    colour *= 0.86 + (0.3 * exp(-since * 5.0));
    colour += p.highlight.rgb * (0.28 * exp(-since * 160.0));

    // A bar of hum rolling up, and the flicker of a tube.
    float hum = exp(-pow((frac(uv.y + (time * 0.045)) - 0.5) * 5.0, 2.0));
    colour *= (1.0 - (0.07 * hum)) * (1.0 + (0.012 * sin(time * 47.0)));

    // The refresh band, once, as the display is first driven: bright where it is, the picture under it not yet lit.
    float sweep = saturate(p.signal.z);
    if (sweep > 0.0)
    {
        float from = uv.y - p.signal.w;
        colour *= 1.0 - (sweep * 0.75 * smoothstep(0.0, 0.03, from));
        colour += p.highlight.rgb * (sweep * 0.9 * exp(-pow(from * 70.0, 2.0)));
    }

    // Grain, the surge of a display coming up, and the face falling away towards its edge.
    float grain = Grain(q * p.frame.xy, tick) - 0.5;
    colour += p.body.rgb * (grain * (p.look.w + (0.5 * disturbed)));
    colour *= 1.0 + (0.55 * boot);
    float edge = length(((uv * 2.0) - 1.0) * float2(0.82, 1.0));
    colour *= 1.0 - (0.55 * smoothstep(0.55, 1.25, edge));

    // The glass over it: a little light caught along its top, and a faint rim just inside the face.
    float gloss = smoothstep(0.55, 0.0, uv.y) * smoothstep(1.3, 0.2, abs((uv.x * 2.0) - 0.7));
    colour += p.highlight.rgb * (0.012 * gloss);
    colour += p.body.rgb * (0.25 * smoothstep(-0.03, 0.0, outside) * inside);

    // Under a text page it steps back.
    colour *= 1.0 - (0.55 * dim);
    colour = max(colour, 0.0) * inside;

    float3 result = lerp(picture, colour, amount);
    return float4(encode ? LinearToSrgb(result) : result, 1.0);
}
