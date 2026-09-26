// One axis of the separable gaussian; run twice, once per axis.
//
// A two-dimensional gaussian is the product of two one-dimensional ones, so an n-by-n kernel is two
// n-tap passes rather than one n-squared-tap pass. At the radii the coarse levels want that is the
// difference between a pass that costs nothing and one that does not.

[[vk::binding(0, 0)]] Texture2D<float4> uSource;
[[vk::binding(1, 0)]] SamplerState uSourceSampler;
[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;

    // axisX, axisY, unused, unused.
    float4 control;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.size.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float2 uv = (float2(px) + 0.5) * p.size.zw;
    float2 step = p.control.xy * p.size.zw;

    // Nine taps of a sigma-two gaussian gathered as five bilinear fetches: each pair of neighbouring
    // taps is fetched once, at the weighted position between them, which halves the sampling for an
    // identical result. Weights normalised so the pass conserves energy.
    const float kWeight0 = 0.2270270270;
    const float kWeight1 = 0.3162162162;
    const float kWeight2 = 0.0702702703;
    const float kOffset1 = 1.3846153846;
    const float kOffset2 = 3.2307692308;

    float3 sum = uSource.SampleLevel(uSourceSampler, uv, 0.0).rgb * kWeight0;
    sum += uSource.SampleLevel(uSourceSampler, uv + (step * kOffset1), 0.0).rgb * kWeight1;
    sum += uSource.SampleLevel(uSourceSampler, uv - (step * kOffset1), 0.0).rgb * kWeight1;
    sum += uSource.SampleLevel(uSourceSampler, uv + (step * kOffset2), 0.0).rgb * kWeight2;
    sum += uSource.SampleLevel(uSourceSampler, uv - (step * kOffset2), 0.0).rgb * kWeight2;

    uTarget.Store(px, float4(max(sum, float3(0.0, 0.0, 0.0)), 1.0));
}
