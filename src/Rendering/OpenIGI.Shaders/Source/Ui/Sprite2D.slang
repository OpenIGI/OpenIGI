// The screen-space shader every 2D draw funnels through: a texture sample modulated by the vertex
// colour. The vertex stage is the shared one; see Common/Sprite2DVertex.slang.

#include "../Common/PushConstants.slang"
#include "../Common/SampledColour.slang"
#include "../Common/Sprite2DVertex.slang"
#include "../Common/TargetEncoding.slang"

struct SpritePush
{
    float2 projectionSize;
};

OPENIGI_PUSH_CONSTANT(SpritePush, push);

// Declared apart rather than as a combined Sampler2D: on the WGSL target a combined sampler takes
// TWO binding slots, which collides with whatever is declared next. Apart, both targets agree.
[[vk::binding(0, 0)]] Texture2D<float4> uTexture;
[[vk::binding(1, 0)]] SamplerState uTextureSampler;

/// The vertex stage: pixels to clip space, colour unswizzled, texture coordinate passed through.
[shader("vertex")]
Sprite2DVaryings vertexMain(Sprite2DVertexInput input)
{
    return Sprite2DTransform(input, push.projectionSize);
}

/// The fragment stage: the texture sample modulated by the vertex colour.
/// <remarks>
/// At the display encoding throughout, as the original composited its interface: the vertex colours are
/// authored there, and a texel an sRGB image's sampler decoded is put back first. See SampledColour.slang
/// and TargetEncoding.slang.
/// </remarks>
[shader("fragment")]
float4 fragmentMain(Sprite2DVaryings input) : SV_Target
{
    float4 texel = uTexture.Sample(uTextureSampler, input.texCoord);
    return ToTarget(float4(SampledDisplay(texel.rgb), texel.a) * input.color);
}
