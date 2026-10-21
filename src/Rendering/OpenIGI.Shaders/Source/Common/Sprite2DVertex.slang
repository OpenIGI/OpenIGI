// The vertex stage every 2D draw shares, whatever fragment stage is paired with it.
//
// It matches Vertex2D -- a pixel position, a texture coordinate, and a modulating colour -- and the
// coordinate system the original's UI is authored in: the origin at the top-left with Y growing
// downwards.
//
// Which way clip space runs is not the same on every target. Vulkan's Y grows downwards, so the
// position maps straight through; WebGPU's grows upwards, and the same mapping would stand the UI on
// its head. The caller says which by the SIGN of the projection height: positive for a target whose
// clip-space Y runs down, negative for one that runs up. A sign rather than another push field
// because the block is shared with the fragment stage's own slice, and widening it would move that.
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
    [[vk::location(0)]] float2 position : TEXCOORD0;
    [[vk::location(1)]] float2 texCoord : TEXCOORD1;
    [[vk::location(2)]] float4 colorBgra : TEXCOORD2;
};

struct Sprite2DVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 texCoord : TEXCOORD0;
    [[vk::location(1)]] float4 color : TEXCOORD1;
};

/// Pixels to clip space, colour unswizzled, texture coordinate passed through.
///
/// A negative projection height means the target's clip-space Y runs upwards, so the mapping is
/// reflected; see the note at the top of this file.
Sprite2DVaryings Sprite2DTransform(Sprite2DVertexInput input, float2 projectionSize)
{
    Sprite2DVaryings output;
    float2 size = float2(projectionSize.x, abs(projectionSize.y));
    float2 ndc = ((input.position / size) * 2.0) - 1.0;
    ndc.y = projectionSize.y < 0.0 ? -ndc.y : ndc.y;
    output.position = float4(ndc, 0.0, 1.0);
    output.texCoord = input.texCoord;
    output.color = input.colorBgra.bgra;
    return output;
}

#endif
