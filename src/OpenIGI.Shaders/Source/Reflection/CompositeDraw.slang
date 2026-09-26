// A reflection laid over the lit scene by the blend unit, one full-screen triangle.
//
// The same answer Composite gives -- the scene plus what it reflects, or the reflection alone for the
// debug view that isolates it -- reached the way both backends allow. Composite adds in place from a
// compute stage, reading and writing the lit image in one dispatch, which WGSL does not permit for an
// rgba16float image; here the fragment only reads the reflection and the pipeline's blend state does the
// adding (one and one) or the replacing (one and nought). The lit image stays an attachment and never
// becomes storage.
//
// What this cannot do is Composite's clamp at nought. That only matters for a reflection that is signed,
// which the water's is and the glazing's never is: a pane's reflection is a radiance weighted by a
// Fresnel term, and both are positive.

#include "../Common/Fullscreen.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uReflection;

/// The vertex stage: one full-screen triangle generated from the vertex index.
[shader("vertex")]
FullscreenVaryings vertexMain(uint vertexIndex : SV_VertexID)
{
    return FullscreenTriangle(vertexIndex);
}

/// The fragment stage: this pixel's reflection, fetched rather than sampled -- it is a value at a pixel.
[shader("fragment")]
float4 fragmentMain(FullscreenVaryings input) : SV_Target
{
    float3 reflection = uReflection.Load(int3(int2(input.position.xy), 0)).rgb;
    return float4(max(reflection, float3(0.0, 0.0, 0.0)), 0.0);
}
