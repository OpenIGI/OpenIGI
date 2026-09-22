// The screen-space shader every 2D draw funnels through.
//
// It matches Vertex2D -- a pixel position, a texture coordinate, and a modulating colour -- and the
// coordinate system the original's UI is authored in: the origin at the top-left with Y growing
// downwards, which is already how Vulkan's clip space runs, so the position maps to normalised
// device coordinates without a flip.
//
// The colour attribute arrives as the engine's Bgra32 -- blue, green, red, alpha in memory. It is
// fed to the pipeline as a plain R8G8B8A8 attribute and swizzled back to RGBA here, which needs no
// optional vertex format the adapter might not support.

#include "../Common/PushConstants.slang"

struct SpritePush
{
    float2 projectionSize;
};

OPENIGI_PUSH_CONSTANT(SpritePush, push);

// Declared apart rather than as a combined Sampler2D: on the WGSL target a combined sampler takes
// TWO binding slots, which collides with whatever is declared next. Apart, both targets agree.
[[vk::binding(0, 0)]] Texture2D<float4> uTexture;
[[vk::binding(1, 0)]] SamplerState uTextureSampler;

struct VertexInput
{
    [[vk::location(0)]] float2 position;
    [[vk::location(1)]] float2 texCoord;
    [[vk::location(2)]] float4 colorBgra;
};

struct Varyings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 texCoord;
    [[vk::location(1)]] float4 color;
};

/// The vertex stage: pixels to clip space, colour unswizzled, texture coordinate passed through.
[shader("vertex")]
Varyings vertexMain(VertexInput input)
{
    Varyings output;
    float2 ndc = ((input.position / push.projectionSize) * 2.0) - 1.0;
    output.position = float4(ndc, 0.0, 1.0);
    output.texCoord = input.texCoord;
    output.color = input.colorBgra.bgra;
    return output;
}

/// The fragment stage: the texture sample modulated by the vertex colour.
[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    return uTexture.Sample(uTextureSampler, input.texCoord) * input.color;
}
