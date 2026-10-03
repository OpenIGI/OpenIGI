// Sums the six blurred levels of the bloom pyramid at the finest level's size, each by its own weight.
//
// Every level is read at the same coordinate and the filtering sampler does the upscale, so a coarse
// level arrives as a smooth wide halo and a fine one as a tight glow; the weights set how the halo falls
// off with distance.

[[vk::binding(0, 0)]] Texture2D<float4> uLevel0;
[[vk::binding(1, 0)]] SamplerState uLevel0Sampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLevel1;
[[vk::binding(3, 0)]] SamplerState uLevel1Sampler;
[[vk::binding(4, 0)]] Texture2D<float4> uLevel2;
[[vk::binding(5, 0)]] SamplerState uLevel2Sampler;
[[vk::binding(6, 0)]] Texture2D<float4> uLevel3;
[[vk::binding(7, 0)]] SamplerState uLevel3Sampler;
[[vk::binding(8, 0)]] Texture2D<float4> uLevel4;
[[vk::binding(9, 0)]] SamplerState uLevel4Sampler;
[[vk::binding(10, 0)]] Texture2D<float4> uLevel5;
[[vk::binding(11, 0)]] SamplerState uLevel5Sampler;
[[vk::binding(12, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct Params
{
    // width, height, 1/width, 1/height of the target.
    float4 size;

    // stages 1-4.
    float4 tintA;

    // stages 5-6, then intensity, unused.
    float4 tintB;
};

[[vk::binding(13, 0)]] ConstantBuffer<Params> p;

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
    float3 sum = (uLevel0.SampleLevel(uLevel0Sampler, uv, 0.0).rgb * p.tintA.x)
        + (uLevel1.SampleLevel(uLevel1Sampler, uv, 0.0).rgb * p.tintA.y)
        + (uLevel2.SampleLevel(uLevel2Sampler, uv, 0.0).rgb * p.tintA.z)
        + (uLevel3.SampleLevel(uLevel3Sampler, uv, 0.0).rgb * p.tintA.w)
        + (uLevel4.SampleLevel(uLevel4Sampler, uv, 0.0).rgb * p.tintB.x)
        + (uLevel5.SampleLevel(uLevel5Sampler, uv, 0.0).rgb * p.tintB.y);
    uTarget.Store(px, float4(max(sum * p.tintB.z, float3(0.0, 0.0, 0.0)), 1.0));
}
