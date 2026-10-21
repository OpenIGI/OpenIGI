// A falling raindrop, drawn as the lens it is.
//
// A drop is clear water. Caught by a shutter it is a thin streak, and what the eye sees in that streak is
// not the drop's own colour -- water has none worth speaking of -- but the scene behind it, bent through a
// cylinder of water: the view is flipped across the streak and drawn in from either side, so a drop falling
// past a dark wall shows a sliver of the lighter ground beside it, and one against the sky shows the sky
// rippled. Along its two rims the curved surface concentrates light towards the eye and catches the sky at
// a grazing angle, so the rims read a touch brighter than the middle. And only where a lamp or the sun stands
// behind it does a drop throw a real glint of its own.
//
// So this samples a copy of the lit scene (taken just before the first drop drew) at the refracted position,
// brightens it by the rim's concentration, adds the sky's sheen at the rims and the drop's glint, and lays the
// result over the scene by the streak's coverage. Far drops dissolve into the air as the scene behind them
// does. The alpha written is ADDED to the lit image's own, which is one wherever the frame was shaded: the
// excess is the reactive mask the temporal resolve reads, so a streak never smears into a trail.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/SampledColour.slang"

struct RainPush
{
    float4x4 modelViewProjection;
};

OPENIGI_PUSH_CONSTANT(RainPush, push);

// The set every mesh pipeline shares; the streak sheet is the diffuse. Only its alpha -- the streak's shape --
// is read. The lightmap and detail are declared because the set is shared.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

// The lit scene before the rain, in linear radiance.
[[vk::binding(0, 1)]] Texture2D<float4> uBackdrop;
[[vk::binding(1, 1)]] SamplerState uBackdropSampler;

struct RainParams
{
    // the air's colour (unread: it is not in the lit image's units) and its density per metre, which veils a glint.
    float4 fog;

    // the render's width, height, 1/width, 1/height.
    float4 screen;

    // x how far the lens bends, in streak widths; y how much of the sky the lens gathers; z the sheet's cells
    // across; w how much brighter the rims concentrate the light behind them.
    float4 lens;
};

[[vk::binding(2, 1)]] ConstantBuffer<RainParams> p;

struct RainVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(2)]] float2 diffuseUv : TEXCOORD2;
    [[vk::location(3)]] float2 lightmapUv : TEXCOORD3;
    [[vk::location(4)]] float4 colorBgra : TEXCOORD4;

    // Declared because the vertex format the storm uploads has them; a drop is rebuilt every frame.
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct RainVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 diffuseUv : TEXCOORD0;
    [[vk::location(1)]] float4 color : TEXCOORD1;
    [[vk::location(2)]] float viewDepth : TEXCOORD2;
};

/// The vertex stage: the world pipeline's transform, character for character (see Glass.slang's).
[shader("vertex")]
RainVaryings vertexMain(RainVertexInput input)
{
    RainVaryings output;
    output.diffuseUv = input.diffuseUv;

    // BGRA, as the engine packs it: the drop's glint in rgb, its opacity in a.
    output.color = input.colorBgra.bgra;

    float4 clip = mul(push.modelViewProjection, float4(input.position, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;

    // An OpenGL projection's clip w is the view depth.
    output.viewDepth = clip.w;
    return output;
}

/// Everything a drop's wide lens takes in around a point: eight taps on a ring an eighth of the screen across,
/// raised towards the top of the view, where the sky is.
float3 WideView(float2 uv, float aspect)
{
    const float radius = 0.12;
    const float lift = 0.08;
    float3 sum = float3(0.0, 0.0, 0.0);

    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float angle = (float(i) + 0.5) * 0.785398163;
        float2 offset = float2(cos(angle) / aspect, sin(angle)) * radius;
        float2 at = uv + offset - float2(0.0, lift);
        sum += uBackdrop.SampleLevel(uBackdropSampler, clamp(at, float2(0.0, 0.0), float2(1.0, 1.0)), 0.0).rgb;
    }

    return sum * 0.125;
}

/// The fragment stage: the scene behind, refracted through the streak.
[shader("fragment")]
float4 fragmentMain(RainVaryings input) : SV_Target
{
    float shape = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv).a;
    float coverage = shape * input.color.a;
    if (coverage < 0.002)
    {
        discard;
    }

    // Across the streak, -1 at one edge to 1 at the other: the sheet lays its cells side by side, so the
    // position within the cell is the position across the drop.
    float cells = max(p.lens.z, 1.0);
    float across = (frac(input.diffuseUv.x * cells) * 2.0) - 1.0;

    // Which way "across" runs on screen, and how many pixels the whole streak is wide, from how fast the
    // sheet's u changes per pixel.
    float2 slope = float2(ddx(input.diffuseUv.x), ddy(input.diffuseUv.x));
    float perPixel = max(length(slope), 1e-6);
    float2 acrossScreen = slope / perPixel;
    float widthPixels = (1.0 / cells) / perPixel;

    // A cylinder of water flips the view across its axis: a point on one side of the streak sees what lies
    // beyond the other. At least a pixel either way, so a distant hair-thin drop still shows as a shimmer.
    float bend = -across * max(widthPixels * p.lens.x, 1.0);
    float2 pixel = input.position.xy;
    float2 refractedUv = (pixel + (acrossScreen * bend)) * p.screen.zw;
    float3 behind = uBackdrop.SampleLevel(uBackdropSampler, clamp(refractedUv, float2(0.0, 0.0), float2(1.0, 1.0)), 0.0).rgb;

    // A sphere of water is a very wide lens: what it shows is not only the patch of scene straight behind it
    // but a fish-eye of everything around, most of which, out of doors, is the bright sky above. That is why
    // rain reads as pale streaks against a dark wall or a stand of trees and all but vanishes against the sky
    // itself -- and why a drop is never the flat white of a light, nor invisible. The wide view is taken from
    // the same image, a broad ring of it leaning upwards, so it is in the scene's own light whatever the hour:
    // grey noon, a lamp-lit night. Its share rises towards the rims, where the surface turns to a grazing angle.
    float rim = across * across;
    float3 around = WideView(pixel * p.screen.zw, p.screen.x * p.screen.w);
    float3 gathered = lerp(behind, around, saturate(p.lens.y * (0.7 + (0.6 * rim))));

    // The curved surface also concentrates light towards the eye along its rims; through the middle it is
    // simply clear, a touch darker for what the water absorbs and scatters away.
    float3 water = gathered * (1.0 + (p.lens.w * rim) - (0.06 * (1.0 - rim)));

    // The drop's own glint: a lamp or the sun behind it. Strongest at the drop's heart, where the sheet's
    // shape is fullest.
    water += input.color.rgb * shape;

    // What a drop refracts is the scene as the eye already sees it, haze and all, so a far drop melts into
    // the fog by itself. Only its own glint -- light it throws rather than bends -- is veiled by the air
    // between, as a lamp's would be. Its coverage is left alone: fading that let a drop a few metres off in
    // a thick storm vanish outright.
    float air = exp(-p.fog.w * max(input.viewDepth, 0.0));
    water += input.color.rgb * shape * (air - 1.0);
    // The painted streak is a hair's breadth with soft sides; a little more of it is water than its falloff
    // alone says.
    float alpha = clamp(coverage * 1.4, 0.0, 1.0);

    return float4(water * alpha, alpha);
}
