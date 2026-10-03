// Halves the resolution, with the four-tap box that also does the first blur.

[[vk::binding(0, 0)]] Texture2D<float4> uSource;
[[vk::binding(1, 0)]] SamplerState uSourceSampler;
[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct Params
{
    // width, height, 1/width, 1/height of the TARGET.
    float4 target;

    // bloom threshold, or negative for none; rest unused.
    float4 control;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.target.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    // Four filtered taps half a target texel from the centre. When the source is exactly twice the
    // target each lands on the corner shared by four source texels, so the average is an even 4x4 box.
    float2 uv = (float2(px) + 0.5) * p.target.zw;
    float2 offset = 0.5 * p.target.zw;
    float3 colour = uSource.SampleLevel(uSourceSampler, uv + float2(-offset.x, -offset.y), 0.0).rgb
        + uSource.SampleLevel(uSourceSampler, uv + float2(offset.x, -offset.y), 0.0).rgb
        + uSource.SampleLevel(uSourceSampler, uv + float2(-offset.x, offset.y), 0.0).rgb
        + uSource.SampleLevel(uSourceSampler, uv + float2(offset.x, offset.y), 0.0).rgb;
    colour = max(colour * 0.25, float3(0.0, 0.0, 0.0));

    // A soft knee: nothing below the threshold, ramping to full two units of luma above it. Nought
    // means no threshold at all, which is not the same as cutting at nought.
    float threshold = p.control.x;
    if (threshold > 0.0)
    {
        float luma = dot(colour, float3(0.3, 0.59, 0.11));
        colour *= saturate((luma - threshold) * 0.5);
    }

    uTarget.Store(px, float4(colour, 1.0));
}
