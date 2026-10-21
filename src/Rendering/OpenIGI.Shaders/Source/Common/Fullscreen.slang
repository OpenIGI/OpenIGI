// The full-screen triangle every post pass draws.
//
// One triangle rather than two, generated from the vertex index, so there is no vertex buffer to
// bind and the draw is a three-vertex call. The triangle is twice the size of the screen; the half
// that falls outside is clipped, and the shared diagonal that two quad triangles would have is never
// rasterised twice.
//
// The texture coordinate runs the way a texture's rows do: nought at the TOP of the target. Where
// that lands in clip space depends on which way the target's clip space runs, so the corner goes out
// through the same switch every mesh stage uses. Without it a pass drawn through a target whose Y runs
// upwards samples everything upside down -- and a chain of them agrees with itself perfectly, because
// each one reads what the last wrote the same wrong way up.

#ifndef OPENIGI_FULLSCREEN_INCLUDED
#define OPENIGI_FULLSCREEN_INCLUDED

#include "ClipSpace.slang"

struct FullscreenVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 uv : TEXCOORD0;
};

/// Builds the triangle's corner for one vertex index.
FullscreenVaryings FullscreenTriangle(uint vertexIndex)
{
    FullscreenVaryings output;
    output.uv = float2(float((vertexIndex << 1) & 2), float(vertexIndex & 2));

    // The corner as an OpenGL projection would place it -- Y up, so the top row is at one -- and then
    // turned whichever way the target wants.
    float2 corner = float2((output.uv.x * 2.0) - 1.0, 1.0 - (output.uv.y * 2.0));
    output.position = float4(corner.x, OpenIgiClipY(corner.y), 0.0, 1.0);
    return output;
}

#endif
