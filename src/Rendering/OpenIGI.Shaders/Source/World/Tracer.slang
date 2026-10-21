// A round's tracer: the original's thin amber line, or the remaster's glowing streak.
//
// Both are camera-facing strips the CPU builds, u running along the streak (0 at its tail, 1 at its head) and v
// across it (0 at one edge, 1 at the other); the vertex colour carries the ammunition's tracer colour at the
// display encoding and, in its alpha, the brightness at that end.
//
// STYLE 0, the retail line (0x47ABD0 -> 0x4E0E20 -> 0x50D4C0): flat, unlit and UNFOGGED -- 0x50DCC0 switches
// Direct3D's fog off for this primitive, which is the difference between a line you see and one painted the
// colour of the fog over a fogged scene. Source-alpha blended, so a fifth of the scene behind it is replaced by
// amber: that takes the blue out of a pale sky, which is what makes the original's line read.
//
// STYLE 1, the remastered streak: light, not paint. A hot near-white core inside a soft halo of the tracer's
// colour, added to the scene in linear radiance strong enough for the bloom to catch, tapering from the head
// back along the streak.
//
// Both ADD their alpha to the lit image's, which is one wherever the frame was shaded: the excess is the marker
// the temporal resolve (and an NVIDIA reconstruction's reactive mask) reads to keep an effect that lives for a
// tick out of its history. The pipeline's alpha blend is one-and-one; its colour blend is the style's.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/TargetEncoding.slang"

struct TracerPush
{
    float4x4 modelViewProjection;

    // x: the style, 0 the retail line and 1 the remastered streak.
    // y: the remastered streak's radiance at full brightness.
    // z: one over the exposure the resolve will apply -- one on the direct path, which is never tone mapped.
    // w: non-zero where the target holds the scene in linear light (the deferred frame's lit image).
    float4 style;
};

OPENIGI_PUSH_CONSTANT(TracerPush, push);

// The set every mesh pipeline shares. A tracer samples none of it; it is declared because the set is shared.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

struct TracerVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(2)]] float2 diffuseUv : TEXCOORD2;
    [[vk::location(3)]] float2 lightmapUv : TEXCOORD3;
    [[vk::location(4)]] float4 colorBgra : TEXCOORD4;

    // Declared because the vertex format the renderer uploads has them; a tracer is rebuilt every frame.
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct TracerVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 strip : TEXCOORD0;
    [[vk::location(1)]] float4 color : TEXCOORD1;
};

/// The vertex stage: the world pipeline's transform, character for character (see Rain.slang's).
[shader("vertex")]
TracerVaryings vertexMain(TracerVertexInput input)
{
    TracerVaryings output;
    output.strip = input.diffuseUv;

    // BGRA, as the engine packs it.
    output.color = input.colorBgra.bgra;

    float4 clip = mul(push.modelViewProjection, float4(input.position, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

/// The retail line: the colour as authored, its brightness as the coverage.
float4 RetailLine(TracerVaryings input)
{
    float alpha = saturate(input.color.a);
    if (push.style.w != 0.0)
    {
        // Over the linear lit image: decoded, and divided by the exposure the resolve will multiply back.
        return float4(DecodeSrgb(input.color.rgb) * push.style.z, alpha);
    }

    return ToTarget(float4(input.color.rgb, alpha));
}

/// The remastered streak: a hot core in a soft halo, as light.
float4 RemasteredStreak(TracerVaryings input)
{
    // Across the strip, -1 at one edge to 1 at the other.
    float across = (input.strip.y * 2.0) - 1.0;
    float d2 = across * across;

    // A core about half the strip wide, and a faint halo that fills it and dies away at its edges.
    float core = exp(-d2 * 9.0);
    float halo = exp(-d2 * 3.0) * (1.0 - d2);

    // The vertex alpha is the brightness: full at the head, fading to nothing at the tail.
    float energy = saturate(input.color.a);
    energy *= energy;

    float3 hue = DecodeSrgb(input.color.rgb);
    float3 white = float3(1.0, 0.96, 0.86);
    float3 radiance = ((hue * halo * 0.35) + (lerp(hue, white, 0.35) * core)) * energy * push.style.y;

    // The marker: enough for the resolve to let go of the history under the streak, scaled by how much of it
    // is actually there.
    float marker = saturate((halo * 0.4 + core) * energy);

    if (push.style.w != 0.0)
    {
        return float4(radiance * push.style.z, marker);
    }

    // The direct path stores display values: the light, clipped, at the display encoding.
    return ToTarget(float4(EncodeSrgb(saturate(radiance)), marker));
}

/// The fragment stage: the style the draw asks for.
[shader("fragment")]
float4 fragmentMain(TracerVaryings input) : SV_Target
{
    return push.style.x < 0.5 ? RetailLine(input) : RemasteredStreak(input);
}
