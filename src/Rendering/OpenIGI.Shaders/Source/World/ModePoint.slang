// A place a multiplayer mode plays for, marked in the world: a bomb site, a capture point.
//
// Two shapes share one draw, told apart by the vertex's lightmap u:
//
//   0, the GROUND DISC: a quad laid on the ground at the point, its diffuse uv running -1..1 across it, so the
//      point's radius is 1. On it, procedurally: a bright outer ring at the radius, a ring of dashes inside it turning
//      slowly, tick marks every thirty degrees between, a faint fill that rises toward the edge, and the point's
//      letter in the middle -- strokes and arcs as signed distances, crisp at any distance and on any preset.
//   1, the WALL: a low band of light standing on the ring, u around it and v up it, fading upward, with soft bands
//      drifting up it, that shows the point's extent from across the ground.
//
// The colour is the point's state's (amber at rest, red pulsing where a bomb is planted), in linear light. The disc is
// laid over the scene premultiplied (its fill darkens what is under it a little, so it reads on bright ground); the
// column adds light. Over the deferred frame's linear lit image it is divided by the exposure the resolve will apply,
// as the tracer's is; on the direct path it is stored at the display encoding. Depth tested, never written.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/TargetEncoding.slang"

struct ModePointPush
{
    float4x4 modelViewProjection;

    // rgb: the point's colour, linear; a: its brightness.
    float4 colour;

    // x: the letter, 0 for none, 1 for A, 2 for B, 3 for C.
    // y: seconds, for the dashes' turn and the column's bands.
    // z: the alarm, 0..1: how strongly the point pulses (a bomb planted at it).
    // w: one over the exposure the resolve will apply -- one on the direct path.
    float4 state;

    // x: non-zero where the target holds the scene in linear light (the deferred frame's lit image).
    // yzw: unused.
    float4 target;
};

OPENIGI_PUSH_CONSTANT(ModePointPush, push);

// The set every mesh pipeline shares. A mode point samples none of it; it is declared because the set is shared.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

struct ModePointVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(2)]] float2 diffuseUv : TEXCOORD2;
    [[vk::location(3)]] float2 lightmapUv : TEXCOORD3;
    [[vk::location(4)]] float4 colorBgra : TEXCOORD4;
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct ModePointVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 uv : TEXCOORD0;
    [[vk::location(1)]] float shape : TEXCOORD1;
};

/// The vertex stage: the world pipeline's transform (see Tracer.slang's).
[shader("vertex")]
ModePointVaryings vertexMain(ModePointVertexInput input)
{
    ModePointVaryings output;
    output.uv = input.diffuseUv;
    output.shape = input.lightmapUv.x;

    float4 clip = mul(push.modelViewProjection, float4(input.position, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

static const float Pi = 3.14159265;

// Coverage of a signed distance, a pixel wide at the edge.
float Cover(float distance)
{
    float width = max(fwidth(distance), 1e-5);
    return saturate(0.5 - (distance / width));
}

// Distance to a stroke from a to b.
float Segment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - (ba * h));
}

// Distance to the right half of a circle (a letter's bowl).
float RightArc(float2 p, float2 centre, float radius)
{
    float2 d = p - centre;
    if (d.x < 0.0)
    {
        // Past the arc's ends: the nearer end.
        float2 top = centre + float2(0.0, radius);
        float2 bottom = centre - float2(0.0, radius);
        return min(length(p - top), length(p - bottom));
    }

    return abs(length(d) - radius);
}

// The letter's strokes' distance, in disc units; a large distance for none.
float Letter(float2 p, float letter)
{
    float d = 1e3;
    if (letter > 0.5 && letter < 1.5)
    {
        // A: two legs meeting at the top, and the bar across.
        d = min(d, Segment(p, float2(-0.26, -0.32), float2(0.0, 0.34)));
        d = min(d, Segment(p, float2(0.26, -0.32), float2(0.0, 0.34)));
        d = min(d, Segment(p, float2(-0.15, -0.06), float2(0.15, -0.06)));
    }
    else if (letter > 1.5 && letter < 2.5)
    {
        // B: the upright, and two bowls on it, the lower the fuller.
        d = min(d, Segment(p, float2(-0.21, -0.33), float2(-0.21, 0.33)));
        d = min(d, Segment(p, float2(-0.21, 0.33), float2(0.02, 0.33)));
        d = min(d, RightArc(p, float2(0.02, 0.175), 0.155));
        d = min(d, Segment(p, float2(-0.21, 0.02), float2(0.04, 0.02)));
        d = min(d, RightArc(p, float2(0.04, -0.155), 0.175));
        d = min(d, Segment(p, float2(-0.21, -0.33), float2(0.04, -0.33)));
    }
    else if (letter > 2.5)
    {
        // C: an open ring.
        float2 q = p;
        float angle = atan2(q.y, q.x);
        float ring = abs(length(q) - 0.3);
        d = abs(angle) < 0.75 ? min(length(q - float2(0.3 * cos(0.75), 0.3 * sin(0.75))), length(q - float2(0.3 * cos(0.75), -0.3 * sin(0.75)))) : ring;
    }

    return d - 0.045;
}

// The ground disc's coverage and how much of it is glow (light) rather than fill (shade).
float2 Disc(float2 uv, float time, float alarm, float letter)
{
    float r = length(uv);

    // The outer ring, and a thin one just inside it.
    float outer = Cover(abs(r - 0.965) - 0.03);
    float inner = Cover(abs(r - 0.885) - 0.006) * 0.55;

    // Dashes turning slowly between them: 24 round the ring, a little over half of each one lit.
    float angle = atan2(uv.y, uv.x);
    float turn = time * (0.18 + (alarm * 0.6));
    float dashes = frac(((angle / (2.0 * Pi)) + turn) * 24.0);
    float dash = Cover(abs(r - 0.925) - 0.014) * step(dashes, 0.58) * 0.8;

    // Ticks every thirty degrees, reaching in from the inner ring.
    float tickAngle = frac((angle / (2.0 * Pi)) * 12.0 + 0.5) - 0.5;
    float tick = Cover(abs(tickAngle) * r * (2.0 * Pi / 12.0) - 0.008) * Cover(abs(r - 0.82) - 0.05) * 0.6;

    // The letter, with a soft halo round its strokes.
    float stroke = Letter(uv, letter);
    float glyph = Cover(stroke);
    float halo = exp(-max(stroke, 0.0) * 18.0) * 0.35 * step(0.5, letter);

    // A fill that rises toward the edge, so the ground inside reads as the site's.
    float fill = smoothstep(0.15, 0.96, r) * 0.22 * (1.0 - smoothstep(0.96, 1.0, r));

    // Nothing past the rim. Masked here and not left early above: an edge's width is a screen-space derivative,
    // which WGSL allows only where every pixel of a quad takes the same path, so no pixel may leave before one.
    float inside = step(r, 1.02);
    float glow = saturate(outer + inner + dash + tick + glyph + halo) * inside;
    return float2(saturate(glow + fill) * inside, glow);
}

// The wall's brightness: a band of light standing on the ring, fading upward, with soft bands drifting up it.
float Column(float2 uv, float time, float alarm)
{
    float up = saturate(uv.y);
    float fade = pow(1.0 - up, 2.4);

    // A brighter seam where it meets the ground, so it reads as standing on the ring.
    float foot = exp(-up * 22.0) * 0.6;

    float bands = 0.7 + (0.3 * sin(((up * 6.0) - (time * (0.6 + alarm))) * 2.0 * Pi));
    return (fade * bands * 0.42) + foot;
}

/// The fragment stage.
[shader("fragment")]
float4 fragmentMain(ModePointVaryings input) : SV_Target
{
    float time = push.state.y;
    float alarm = saturate(push.state.z);
    float pulse = 1.0 + (alarm * (0.6 + (0.6 * sin(time * 2.0 * Pi * 1.6))));
    float3 colour = push.colour.rgb * push.colour.a * pulse;

    // The disc is worked out for every pixel, the column's included, and only then chosen: its edges take
    // screen-space derivatives, which must not sit behind a branch on what a vertex carried.
    float2 disc = Disc(input.uv, time, alarm, push.state.x);

    float3 light;
    float cover;
    if (input.shape < 0.5)
    {
        light = colour * (disc.y + ((disc.x - disc.y) * 0.5));
        cover = disc.x * 0.6;
    }
    else
    {
        light = colour * Column(input.uv, time, alarm);
        cover = 0.0;
    }

    if (push.target.x != 0.0)
    {
        // Over the linear lit image, divided by the exposure the resolve will multiply back.
        return float4(light * push.state.w, cover);
    }

    // The direct path stores display values.
    return ToTarget(float4(EncodeSrgb(saturate(light)), cover));
}
