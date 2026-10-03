// Resolves the accumulated history (twice the render size on each axis) onto the presented frame.
//
// Each output pixel is reconstructed from the 4x4 history texels around its centre with the
// Mitchell-Netravali cubic (B = C = 1/3), weighted toward dim samples so a single bright texel cannot
// ring across its neighbours, optionally sharpened against the plain average of the same footprint,
// and finally clamped into the footprint's range so the cubic's negative lobes never overshoot.

#include "../Common/Unroll.slang"

// The history colour this frame just wrote, and the presented frame it resolves into.
[[vk::binding(0, 0)]] Texture2D<float4> uHistoryColour;
[[vk::binding(1, 0)]] SamplerState uHistoryColourSampler;
[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uOutput;

// Read only by the debug views, which let the tone map show what the temporal pass believed rather
// than what it produced: the motion, the two verdicts and the accumulated count.
[[vk::binding(3, 0)]] Texture2D<float4> uVelocity;
[[vk::binding(4, 0)]] SamplerState uVelocitySampler;
[[vk::binding(5, 0)]] Texture2D<float4> uParallax;
[[vk::binding(6, 0)]] SamplerState uParallaxSampler;
[[vk::binding(7, 0)]] Texture2D<float4> uShading;
[[vk::binding(8, 0)]] SamplerState uShadingSampler;
[[vk::binding(9, 0)]] Texture2D<float4> uHistoryMetadata;
[[vk::binding(10, 0)]] SamplerState uHistoryMetadataSampler;

struct Params
{
    // width, height, 1/width, 1/height of the presented frame.
    float4 outputSize;

    // width, height, 1/width, 1/height of the history.
    float4 historySize;

    // history texels per output pixel, per axis; z debug view; w sharpen.
    float4 control;
};

[[vk::binding(11, 0)]] ConstantBuffer<Params> p;

// Weights a sample down as it gets brighter, so the filters below average in a tone-mapped sense
// and one hot texel does not dominate its footprint.
float HdrWeight(float3 c)
{
    return max(1e-4, 1.0 / (c.r + (2.0 * c.g) + c.b + 4.0));
}

// Mitchell & Netravali (1988) cubic with B = C = 1/3, support 2.
float MitchellNetravali(float x)
{
    const float B = 1.0 / 3.0;
    const float C = 1.0 / 3.0;
    float ax = abs(x);
    float ax2 = ax * ax;
    float ax3 = ax2 * ax;
    if (ax < 1.0)
    {
        return (((12.0 - (9.0 * B) - (6.0 * C)) * ax3)
            + ((-18.0 + (12.0 * B) + (6.0 * C)) * ax2)
            + (6.0 - (2.0 * B))) / 6.0;
    }

    if (ax < 2.0)
    {
        return (((-B - (6.0 * C)) * ax3)
            + (((6.0 * B) + (30.0 * C)) * ax2)
            + (((-12.0 * B) - (48.0 * C)) * ax)
            + ((8.0 * B) + (24.0 * C))) / 6.0;
    }

    return 0.0;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.outputSize.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    // History texels per output pixel, per axis. Two when resolving to the render size; smaller when a
    // render scale makes the output larger than the render -- the upscale case, where this same
    // reconstruction becomes the upscaler.
    float2 scale = max(p.control.xy, float2(1e-3, 1e-3));

    int debugView = int(p.control.z + 0.5);
    if (debugView != 0)
    {
        float2 uv = (float2(px) + 0.5) * p.outputSize.zw;
        float3 shown = float3(0.0, 0.0, 0.0);
        if (debugView == 1)
        {
            // Motion in input pixels, twenty of them across the whole range: red down, green right,
            // mid-grey still.
            uint velocityWidth;
            uint velocityHeight;
            uVelocity.GetDimensions(velocityWidth, velocityHeight);
            float2 v = uVelocity.SampleLevel(uVelocitySampler, uv, 0.0).xy
                * float2(velocityWidth, velocityHeight);
            shown = float3(0.5 + (v.y / 20.0), 0.5 + (v.x / 20.0), 0.5);
        }
        else if (debugView == 2)
        {
            shown = uParallax.SampleLevel(uParallaxSampler, uv, 0.0).rrr;
        }
        else if (debugView == 3)
        {
            shown = uShading.SampleLevel(uShadingSampler, uv, 0.0).rrr;
        }
        else
        {
            shown = uHistoryMetadata.SampleLevel(
                uHistoryMetadataSampler,
                uv * p.historySize.xy * p.historySize.zw,
                0.0).rrr;
        }

        uOutput.Store(px, float4(clamp(shown, float3(0.0, 0.0, 0.0), float3(1.0, 1.0, 1.0)), 1.0));
        return;
    }

    float2 hp = (float2(px) + 0.5) * scale;
    float2 tl = floor(hp - 1.5) + 0.5;

    float3 sum = float3(0.0, 0.0, 0.0);
    float sumWeight = 0.0;
    float3 box = float3(0.0, 0.0, 0.0);
    float boxWeight = 0.0;
    float3 lo = float3(1e30, 1e30, 1e30);
    float3 hi = float3(-1e30, -1e30, -1e30);
    OPENIGI_UNROLL for (int y = 0; y < 4; y++)
    {
        OPENIGI_UNROLL for (int x = 0; x < 4; x++)
        {
            float2 pos = tl + float2(x, y);
            float2 uv = clamp(pos * p.historySize.zw, 0.0, 1.0);
            float3 c = max(uHistoryColour.SampleLevel(uHistoryColourSampler, uv, 0.0).rgb, 0.0);
            float hw = HdrWeight(c);
            float wt = MitchellNetravali(abs(pos.x - hp.x)) * MitchellNetravali(abs(pos.y - hp.y)) * hw;
            sum += c * wt;
            sumWeight += wt;
            box += c * hw;
            boxWeight += hw;
            lo = min(lo, c);
            hi = max(hi, c);
        }
    }

    float3 col = sumWeight != 0.0 ? sum / sumWeight : float3(0.0, 0.0, 0.0);
    float sharpen = p.control.w;
    if (sharpen > 0.0 && boxWeight > 0.0)
    {
        col += (col - (box / boxWeight)) * sharpen;
    }

    col = clamp(col, lo, hi);
    uOutput.Store(px, float4(max(col, 0.0), 1.0));
}
