// Sums the six blurred levels of the bloom pyramid at the finest level's size, each by its own weight.
//
// Every level is read at the same coordinate and upscaled with a cubic B-spline, so a coarse level arrives
// as a smooth wide halo and a fine one as a tight glow; the weights set how the halo falls off with
// distance. The sampler's own bilinear upscale was not smooth enough: a coarse level is a sixty-fourth of
// the screen across, and a small bright source -- the moon at night's exposure, the sun's disc -- came out
// of it as a stack of square, stepped rings.

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

/// A level read with a cubic B-spline over its texels: four bilinear fetches, each placed so the
/// hardware's own weights make up the spline's (Sigg and Hadwiger, GPU Gems 2 ch. 20).
float3 SampleSmooth(Texture2D<float4> level, SamplerState levelSampler, float2 uv)
{
    uint width;
    uint height;
    level.GetDimensions(width, height);
    float2 size = float2(width, height);
    float2 texel = (uv * size) - 0.5;
    float2 base = floor(texel);
    float2 f = texel - base;

    float2 f2 = f * f;
    float2 f3 = f2 * f;
    float2 w0 = (1.0 / 6.0) * ((-f3) + (3.0 * f2) - (3.0 * f) + 1.0);
    float2 w1 = (1.0 / 6.0) * ((3.0 * f3) - (6.0 * f2) + 4.0);
    float2 w2 = (1.0 / 6.0) * ((-3.0 * f3) + (3.0 * f2) + (3.0 * f) + 1.0);
    float2 w3 = (1.0 / 6.0) * f3;

    float2 g0 = w0 + w1;
    float2 g1 = w2 + w3;
    float2 h0 = ((w1 / g0) - 1.0 + base + 0.5) / size;
    float2 h1 = ((w3 / g1) + 1.0 + base + 0.5) / size;

    return (g0.y * ((g0.x * level.SampleLevel(levelSampler, float2(h0.x, h0.y), 0.0).rgb)
                + (g1.x * level.SampleLevel(levelSampler, float2(h1.x, h0.y), 0.0).rgb)))
        + (g1.y * ((g0.x * level.SampleLevel(levelSampler, float2(h0.x, h1.y), 0.0).rgb)
                + (g1.x * level.SampleLevel(levelSampler, float2(h1.x, h1.y), 0.0).rgb)));
}

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
        + (SampleSmooth(uLevel1, uLevel1Sampler, uv) * p.tintA.y)
        + (SampleSmooth(uLevel2, uLevel2Sampler, uv) * p.tintA.z)
        + (SampleSmooth(uLevel3, uLevel3Sampler, uv) * p.tintA.w)
        + (SampleSmooth(uLevel4, uLevel4Sampler, uv) * p.tintB.x)
        + (SampleSmooth(uLevel5, uLevel5Sampler, uv) * p.tintB.y);
    uTarget.Store(px, float4(max(sum * p.tintB.z, float3(0.0, 0.0, 0.0)), 1.0));
}
