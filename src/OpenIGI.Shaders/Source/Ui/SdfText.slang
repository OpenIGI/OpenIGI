// The distance-field text shader: crisp glyph edges at any size, with an optional outline and glow.
//
// The port of the OpenGL backend's SdfTextShaders, which went with that backend. Without it the
// heading font -- a signed distance field -- was being drawn as ordinary coverage, which is exactly
// a soft, fat glyph: the field's gradient was on screen where the edge should have been.
//
// The edge is placed at half, and the anti-aliasing width is derived from how many screen pixels one
// distance unit spans, so the same atlas is sharp at 14 pixels and at a full-screen title.

#include "../Common/PushConstants.slang"
#include "../Common/Sprite2DVertex.slang"

// One block for both stages. The vertex stage reads projectionSize at offset 0; the fragment stage
// reads from offset 16 onwards, which is where the first float4 lands of its own accord because a
// four-component vector aligns to sixteen bytes. The two stages therefore touch disjoint halves of
// the same block, exactly as the hand-written GLSL did with layout(offset = 16).
struct SdfTextPush
{
    float2 projectionSize;

    // x: distance-field spread in atlas texels; y: dissolve; z: its seed.
    float4 range;

    // rgb, with the outline half-width in a.
    float4 outline;

    // rgb, with the glow intensity in a.
    float4 glow;

    // x: distance below the edge the glow fades over.
    float4 glowWidth;
};

OPENIGI_PUSH_CONSTANT(SdfTextPush, push);

[[vk::binding(0, 0)]] Texture2D<float4> uTexture;
[[vk::binding(1, 0)]] SamplerState uTextureSampler;

/// The vertex stage: the shared 2D transform.
[shader("vertex")]
Sprite2DVaryings vertexMain(Sprite2DVertexInput input)
{
    return Sprite2DTransform(input, push.projectionSize);
}

float Hash(float2 v)
{
    return frac(sin(dot(v, float2(127.1, 311.7))) * 43758.5453);
}

/// How many screen pixels one distance-field unit spans at this fragment.
float ScreenPixelRange(float2 texCoord)
{
    uint width;
    uint height;
    uTexture.GetDimensions(width, height);

    float2 unitRange = float2(push.range.x, push.range.x) / float2(width, height);
    float2 screenTexSize = float2(1.0, 1.0) / fwidth(texCoord);
    return max(0.5 * dot(unitRange, screenTexSize), 1.0);
}

/// The fragment stage: the glyph's fill, its outline and its glow, composited in that order.
[shader("fragment")]
float4 fragmentMain(Sprite2DVaryings input) : SV_Target
{
    float d = uTexture.Sample(uTextureSampler, input.texCoord).a;
    float range = ScreenPixelRange(input.texCoord);

    // The dissolve: a grain of noise across the glyph decides the order its texels go in, the
    // strokes thin as it advances, and the texels about to go glow like an ember's edge.
    float dissolve = clamp(push.range.y, 0.0, 1.0);
    float grain = Hash(floor(input.texCoord * 1024.0) + float2(push.range.z * 17.0, push.range.z * 5.0));
    if (dissolve > 0.0 && grain < dissolve)
    {
        discard;
    }

    float ember = dissolve > 0.0 ? 1.0 - smoothstep(dissolve, dissolve + 0.16, grain) : 0.0;
    float fill = clamp(((d - 0.5 - (dissolve * 0.22)) * range) + 0.5, 0.0, 1.0);

    float4 result = float4(0.0, 0.0, 0.0, 0.0);

    // Glow first, so it sits behind the outline and fill.
    if (push.glow.a > 0.0 && push.glowWidth.x > 0.0)
    {
        float g = smoothstep(0.5 - push.glowWidth.x, 0.5, d) * push.glow.a;
        result = float4(push.glow.rgb, g);
    }

    // Outline: everything inside the edge grown outwards by the half-width.
    if (push.outline.a > 0.0)
    {
        float outer = clamp(((d - (0.5 - push.outline.a)) * range) + 0.5, 0.0, 1.0);
        result = lerp(result, float4(push.outline.rgb, 1.0), outer);
    }

    // Fill on top, tinted by the vertex colour, and lit at the burning edge.
    float3 fillColour = lerp(input.color.rgb, float3(1.0, 0.86, 0.62), ember * 0.9);
    result = lerp(result, float4(fillColour, 1.0), fill);
    result.a *= input.color.a;

    return result;
}
