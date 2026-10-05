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

// The scene's depth and normals, so a rough surface's reflection is spread over that surface alone.
[[vk::binding(1, 0)]] DepthTexture2D uDepth;
[[vk::binding(2, 0)]] Texture2D<float4> uNormal;

// The twelve taps of the spread, over a unit disc.
static const float2 kSpread[12] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

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
    int2 px = int2(input.position.xy);
    float4 centre = uReflection.Load(int3(px, 0));
    float3 reflection = centre.rgb;

    // A rough surface's reflection, spread over the footprint its roughness implies: one jittered ray a
    // frame is a grain, and the temporal resolve averages frames, not neighbours. Only over the same
    // surface -- the same depth to within a few per cent and a normal that agrees -- and only from
    // neighbours that are rough reflections themselves, so nothing sharp is smeared into it and its blur
    // does not run off onto the wall behind.
    if (centre.a >= 2.0)
    {
        float roughness = centre.a - 2.0;
        float radius = clamp(roughness * roughness * 28.0, 0.0, 10.0);
        if (radius >= 1.0)
        {
            uint width;
            uint height;
            uDepth.GetDimensions(width, height);
            int2 limit = int2(int(width) - 1, int(height) - 1);
            float depth = uDepth.Load(int3(px, 0));
            float3 normal = normalize((uNormal.Load(int3(px, 0)).xyz * 2.0) - 1.0);

            float angle = frac(52.9829189 * frac(dot(float2(px), float2(0.06711056, 0.00583715)))) * 6.2831853;
            float s = sin(angle);
            float c = cos(angle);

            float3 sum = reflection;
            float weightSum = 1.0;
            for (int i = 0; i < 12; i++)
            {
                float2 tap = kSpread[i];
                int2 at = clamp(px + int2(round(float2((tap.x * c) - (tap.y * s), (tap.x * s) + (tap.y * c)) * radius)), int2(0, 0), limit);
                float4 sample = uReflection.Load(int3(at, 0));
                if (sample.a < 2.0)
                {
                    continue;
                }

                float sampleDepth = uDepth.Load(int3(at, 0));
                float3 sampleNormal = normalize((uNormal.Load(int3(at, 0)).xyz * 2.0) - 1.0);
                float sameDepth = abs(sampleDepth - depth) <= (0.03 * max(depth, 1e-6)) ? 1.0 : 0.0;
                float facing = saturate(dot(sampleNormal, normal));
                float weight = sameDepth * facing * facing * facing * facing;
                sum += sample.rgb * weight;
                weightSum += weight;
            }

            reflection = sum / weightSum;
        }
    }

    return float4(max(reflection, float3(0.0, 0.0, 0.0)), 0.0);
}
