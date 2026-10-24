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
//   - being noticed swells a soft glow in from the side the watcher is, a crescent along the screen's oval holding a faint
//     drifting honeycomb; once they have the player, arcs ripple inward from it.
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

    // shake x, shake y in pixels, the strongest watcher's bearing in radians clockwise from ahead, whether there is one.
    float4 shake;

    // the hit's wash in each corner: top left, top right, bottom left, bottom right.
    float4 damage;

    // the hit's colour, and the picture under the display: nought where it cannot be read, one where it can and the target
    // encodes as it stores, two where it can and the target stores what it is given, so the picture goes out encoded.
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

// How near a point is to the edge of its cell of a hexagonal grid, nought on the edge: the shield's honeycomb.
float HexEdge(float2 v)
{
    const float2 s = float2(1.0, 1.7320508);
    float2 a = fmod(abs(v), s) - (s * 0.5);
    float2 b = fmod(abs(v - (s * 0.5)), s) - (s * 0.5);
    float2 g = abs(dot(a, a) < dot(b, b) ? a : b);
    return 0.5 - max(dot(g, normalize(s)), g.x);
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

    // Whatever lights the visor, added: the glow of being noticed.
    float3 light = float3(0.0, 0.0, 0.0);

    // Being noticed: a glow swells in from the side the watcher is -- no frame, no edge: a soft crescent following the
    // screen's oval, filled with a faint honeycomb that drifts, and once they have the player, arcs rippling inward.
    float threat = saturate(p.alert.x) * intensity;
    if (threat > 0.0)
    {
        // How far out on the screen's oval: nought in the middle, one at the middle of each edge.
        float oval = length((q - 0.5) * 2.0);

        // Towards the watcher, clockwise from straight up as the bearing is; someone behind swells from the bottom. With no
        // one in particular, a faint even ring.
        float facing = atan2(fromMiddle.x, -fromMiddle.y);
        float toward = pow(saturate(0.5 + (0.5 * cos(facing - p.shake.z))), 6.0);
        float side = lerp(0.3, toward, saturate(p.shake.w));

        float swell = smoothstep(0.62 + (0.25 * (1.0 - threat)), 1.25, oval) * side;
        float cells = 1.0 - smoothstep(0.0, 0.06, HexEdge((qa * 22.0) + float2(0.0, time * 0.35)));
        float shimmer = 0.75 + (0.25 * sin((time * 4.0) + (oval * 18.0)));

        // Once they have the player, arcs ripple in from the edge on their side.
        float sure = saturate((threat - 0.7) / 0.25);
        float phase = frac(time * 0.8);
        float ripple = exp(-pow((oval - (1.3 - (phase * 0.55))) / 0.012, 2.0)) * (1.0 - phase) * sure * side;

        light += p.alert.yzw * (((swell * (0.35 + (0.45 * cells)) * shimmer) + (ripple * 0.9)) * threat);
    }

    colour.rgb += light;

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

    float dim = p.damageColour.w > 0.5 ? 0.0 : hit * 0.15;
    float4 layered = lerp(Layer(input.uv), colour, intensity);
    layered.rgb += torn * (tear * (1.0 - layered.a));
    layered.a += max(tear, dim) * (1.0 - layered.a);
    return layered;
}
