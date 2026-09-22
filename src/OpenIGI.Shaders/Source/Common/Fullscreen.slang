// The full-screen triangle every post pass draws.
//
// One triangle rather than two, generated from the vertex index, so there is no vertex buffer to
// bind and the draw is a three-vertex call. The triangle is twice the size of the screen; the half
// that falls outside is clipped, and the shared diagonal that two quad triangles would have is never
// rasterised twice.

#ifndef OPENIGI_FULLSCREEN_INCLUDED
#define OPENIGI_FULLSCREEN_INCLUDED

struct FullscreenVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 uv;
};

/// Builds the triangle's corner for one vertex index.
FullscreenVaryings FullscreenTriangle(uint vertexIndex)
{
    FullscreenVaryings output;
    output.uv = float2(float((vertexIndex << 1) & 2), float(vertexIndex & 2));
    output.position = float4((output.uv * 2.0) - 1.0, 0.0, 1.0);
    return output;
}

#endif
