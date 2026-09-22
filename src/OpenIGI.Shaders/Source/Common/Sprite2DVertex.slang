// The vertex stage every 2D draw shares, whatever fragment stage is paired with it.
//
// It matches Vertex2D -- a pixel position, a texture coordinate, and a modulating colour -- and the
// coordinate system the original's UI is authored in: the origin at the top-left with Y growing
// downwards, which is already how Vulkan's clip space runs, so the position maps to normalised
// device coordinates without a flip.
//
// The colour attribute arrives as the engine's Bgra32 -- blue, green, red, alpha in memory. It is
// fed to the pipeline as a plain R8G8B8A8 attribute and swizzled back to RGBA here, which needs no
// optional vertex format the adapter might not support.
//
// It lives here rather than in one of the modules because a module is compiled whole, entry points
// and all: the sprite and the distance-field text each declare their own vertexMain, and both call
// this so that the transform exists once.

#ifndef OPENIGI_SPRITE2D_VERTEX_INCLUDED
#define OPENIGI_SPRITE2D_VERTEX_INCLUDED

struct Sprite2DVertexInput
{
    [[vk::location(0)]] float2 position;
    [[vk::location(1)]] float2 texCoord;
    [[vk::location(2)]] float4 colorBgra;
};

struct Sprite2DVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 texCoord;
    [[vk::location(1)]] float4 color;
};

/// Pixels to clip space, colour unswizzled, texture coordinate passed through.
Sprite2DVaryings Sprite2DTransform(Sprite2DVertexInput input, float2 projectionSize)
{
    Sprite2DVaryings output;
    float2 ndc = ((input.position / projectionSize) * 2.0) - 1.0;
    output.position = float4(ndc, 0.0, 1.0);
    output.texCoord = input.texCoord;
    output.color = input.colorBgra.bgra;
    return output;
}

#endif
