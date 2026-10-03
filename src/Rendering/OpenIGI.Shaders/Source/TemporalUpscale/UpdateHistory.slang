#include "../Common/DepthSlot.slang"
#include "../Common/Unroll.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uCurrent;
[[vk::binding(1, 0)]] SamplerState uCurrentSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uVelocity;
[[vk::binding(3, 0)]] SamplerState uVelocitySampler;
[[vk::binding(4, 0)]] Texture2D<float4> uPrevHistoryColour;
[[vk::binding(5, 0)]] SamplerState uPrevHistoryColourSampler;
[[vk::binding(6, 0)]] Texture2D<float4> uPrevHistoryMetadata;
[[vk::binding(7, 0)]] SamplerState uPrevHistoryMetadataSampler;
[[vk::binding(8, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uHistoryColour;
[[vk::binding(9, 0)]] [[vk::image_format("r8")]] WTexture2D<float4> uHistoryMetadata;
[[vk::binding(10, 0)]] Texture2D<float4> uHistoryRejection;
[[vk::binding(11, 0)]] SamplerState uHistoryRejectionSampler;
[[vk::binding(12, 0)]] Texture2D<float4> uSpatialAa;
[[vk::binding(13, 0)]] SamplerState uSpatialAaSampler;
[[vk::binding(14, 0)]] Texture2D<float4> uShadingRejection;
[[vk::binding(15, 0)]] SamplerState uShadingRejectionSampler;
[[vk::binding(16, 0)]] Texture2D<float4> uRejectionBlend;
[[vk::binding(17, 0)]] SamplerState uRejectionBlendSampler;
OPENIGI_DEPTH_SLOT(18) uDepth;
[[vk::binding(19, 0)]] SamplerState uDepthSampler;

struct Params
{
    float4 input;
    float4 history;
    float4 control;
    float4 control2;
    float4 control3;
};

[[vk::binding(20, 0)]] ConstantBuffer<Params> p;

// Accumulates the jittered frames into a history twice the render size on each axis.
//
// Every history texel reconstructs this frame's colour from the 3x3 input pixels around it with a
// kernel that narrows as the history earns trust, fetches its own previous value with a Catmull-Rom
// filter at the reprojected position, clips that toward the box of the current neighbourhood (Karis
// 2014; the box is the mean plus and minus one standard deviation, Salvi 2016) and blends the two with
// HDR-aware weights. How far the clip is relaxed, and how much of the new frame goes in, follow from
// the rejection verdicts the earlier passes computed.

// The accumulation is tone-weighted so a bright sample cannot dominate the average.
float HdrWeight(float3 c)
{
    return max(1e-4, 1.0 / (c.r + (2.0 * c.g) + c.b + 4.0));
}

float Luma(float3 c)
{
    return c.r + (2.0 * c.g) + c.b;
}

// The space the neighbourhood box is built in: compressive, so the box is not dominated by highlights.
float3 ToBox(float3 x)
{
    float3 t = x / (x + 0.17);
    return t * t;
}

float3 FromBox(float3 s)
{
    float3 g = sqrt(max(s, 0.0));
    return (g * 0.17) / max(1.0 - g, 1e-4);
}

// The reconstruction kernel for an input sample at offset delta (input pixels) under kernel factor k:
// a smooth fall-off reaching its floor at a distance of 1/k pixels.
float KernelWeight(float k, float2 delta)
{
    float s = saturate((k * k) * dot(delta, delta));
    return saturate(1.0 - (1.9 * s) + (0.9 * s * s) + 0.005);
}

bool IsFiniteColour(float3 c)
{
    return !(any(isnan(c)) || any(isinf(c)));
}

// The previous history at uv, Catmull-Rom filtered and evaluated with nine bilinear fetches: the two
// inner taps of each axis are merged into one fetch placed between them.
float4 SampleHistoryCatmullRom(float2 uv)
{
    float2 historySize = p.history.xy;
    float2 samplePos = uv * historySize;
    float2 t1 = floor(samplePos - 0.5) + 0.5;
    float2 f = samplePos - t1;

    float2 w0 = f * (-0.5 + (f * (1.0 - (0.5 * f))));
    float2 w1 = 1.0 + ((f * f) * (-2.5 + (1.5 * f)));
    float2 w2 = f * (0.5 + (f * (2.0 - (1.5 * f))));
    float2 w3 = (f * f) * (-0.5 + (0.5 * f));

    float2 w12 = w1 + w2;
    float2 t12 = t1 + (w2 / max(w12, 1e-5));
    float2 t0 = t1 - 1.0;
    float2 t3 = t1 + 2.0;

    float2 uv0 = t0 * p.history.zw;
    float2 uv12 = t12 * p.history.zw;
    float2 uv3 = t3 * p.history.zw;

    float xs[3] = { uv0.x, uv12.x, uv3.x };
    float ys[3] = { uv0.y, uv12.y, uv3.y };
    float wx[3] = { w0.x, w12.x, w3.x };
    float wy[3] = { w0.y, w12.y, w3.y };

    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    OPENIGI_UNROLL for (int y = 0; y < 3; y++)
    {
        OPENIGI_UNROLL for (int x = 0; x < 3; x++)
        {
            float4 c = uPrevHistoryColour.SampleLevel(uPrevHistoryColourSampler, float2(xs[x], ys[y]), 0.0);
            result += c * (wx[x] * wy[y]);
        }
    }

    return result;
}

[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 historyPixels = int2(p.history.xy);
    if (px.x >= historyPixels.x || px.y >= historyPixels.y)
    {
        return;
    }

    float N = max(1.0, p.control.y);
    float scale = max(p.control.z, 1.0);
    float2 jitter = float2(p.control.w, p.control2.x);
    float2 inputSize = p.input.xy;
    int2 inputPixels = int2(inputSize);

    float2 inputPos = ((float2(px) + 0.5) * inputSize) / p.history.xy;
    float2 uv = inputPos / inputSize;
    float3 bilin = max(uCurrent.SampleLevel(uCurrentSampler, uv, 0.0).rgb, 0.0);

    // The first-person layer is drawn without the jitter, so it is reconstructed unjittered and never
    // accumulated: it is always this frame's.
    float4 depths = OPENIGI_DEPTH_GATHER(uDepth, uDepthSampler, uv);
    bool foreground = max(max(depths.x, depths.y), max(depths.z, depths.w)) >= 0.9;
    float2 sj = foreground ? float2(0.0, 0.0) : jitter;
    float2 uvJ = uv + (sj / inputSize);
    float3 bilinJ = max(uCurrent.SampleLevel(uCurrentSampler, uvJ, 0.0).rgb, 0.0);

    if (p.control.x < 0.5)
    {
        uHistoryColour.Store(px, float4(bilin, 1.0));
        uHistoryMetadata.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float2 v = uVelocity.SampleLevel(uVelocitySampler, uvJ, 0.0).xy;
    float2 prevUv = uv - v;

    // Speed in pixels of the grid the result is seen on: the output grid when upscaling.
    float2 grid = inputSize;
    if (abs(p.control.z - p.control3.w) > 1e-5)
    {
        grid = p.history.xy / max(p.control3.w, 1.0);
    }

    float speed = length(v * grid);
    bool offScreen = any(prevUv < 0.0) || any(prevUv > 1.0);

    float believable = p.control2.y > 0.5 ? uHistoryRejection.SampleLevel(uHistoryRejectionSampler, uvJ, 0.0).r : 1.0;
    float shading = p.control2.w > 0.5 ? uShadingRejection.SampleLevel(uShadingRejectionSampler, uvJ, 0.0).r : 1.0;
    float relaxClamp = p.control2.w > 0.5 ? uRejectionBlend.SampleLevel(uRejectionBlendSampler, uvJ, 0.0).r : 1.0;
    if (offScreen || believable < 0.5)
    {
        relaxClamp = 0.0;
    }

    relaxClamp *= believable;

    // The kernel narrows toward one history texel only where the history is trusted; elsewhere this
    // frame alone has to fill the texel, so it keeps the full input-pixel footprint.
    float refine = (offScreen || foreground) ? 0.0 : saturate((16.0 * believable * shading) - 13.0);
    float k = lerp(1.0, scale, refine);

    if (p.control2.z > 0.5)
    {
        int2 rp = clamp(int2(inputPos + sj), int2(0, 0), inputPixels - 1);
        float2 o = uSpatialAa.Load(int3(rp, 0)).xy;
        float lf = min(shading, relaxClamp);
        float lerpAmt = (offScreen || believable < 0.5) ? 1.0 : saturate(1.0 - (4.0 * lf));
        inputPos += o * lerpAmt;
    }

    // This frame's colour at the texel, from the 3x3 input pixels around it.
    int2 corner = int2(floor(inputPos - 1.0));
    float3 sumColour = float3(0.0, 0.0, 0.0);
    float sumWeight = 0.0;
    float cov = 0.0;
    OPENIGI_UNROLL for (int gy = 0; gy < 3; gy++)
    {
        OPENIGI_UNROLL for (int gx = 0; gx < 3; gx++)
        {
            int2 kk = corner + int2(gx, gy);
            if (kk.x < 0 || kk.y < 0 || kk.x >= inputPixels.x || kk.y >= inputPixels.y)
            {
                continue;
            }

            float2 pos = float2(kk) + 0.5 - sj;
            float w = KernelWeight(k, pos - inputPos);
            float3 c = max(uCurrent.Load(int3(kk, 0)).rgb, 0.0);
            float hw = w * HdrWeight(c);
            sumColour += c * hw;
            sumWeight += hw;
            cov += w;
        }
    }

    float3 current = sumWeight > 1e-6 ? sumColour / sumWeight : bilinJ;
    cov = saturate(cov);

    if (foreground)
    {
        uHistoryColour.Store(px, float4(current, 1.0));
        uHistoryMetadata.Store(px, float4(cov / N, 0.0, 0.0, 0.0));
        return;
    }

    // The neighbourhood box, and how much of it was drawn additively this frame.
    float add = 0.0;
    float3 lo = float3(1e30, 1e30, 1e30);
    float3 hi = float3(-1e30, -1e30, -1e30);
    float3 m1 = float3(0.0, 0.0, 0.0);
    float3 m2 = float3(0.0, 0.0, 0.0);
    OPENIGI_UNROLL for (int ny = -1; ny <= 1; ny++)
    {
        OPENIGI_UNROLL for (int nx = -1; nx <= 1; nx++)
        {
            float4 tap = uCurrent.SampleLevel(uCurrentSampler, uvJ + (float2(nx, ny) / inputSize), 0.0);
            add = max(add, tap.a - 1.0);
            float3 s = ToBox(max(tap.rgb, 0.0));
            lo = min(lo, s);
            hi = max(hi, s);
            m1 += s;
            m2 += s * s;
        }
    }

    add = max(0.0, add);
    float reactive = saturate(4.0 * add);
    float3 mu = m1 / 9.0;
    float3 sigma = sqrt(max((m2 / 9.0) - (mu * mu), 0.0));
    float3 boxLo = max(lo, mu - sigma);
    float3 boxHi = min(hi, mu + sigma);

    if (offScreen)
    {
        uHistoryColour.Store(px, float4(current, 1.0 - reactive));
        uHistoryMetadata.Store(px, float4(1.0 / N, 0.0, 0.0, 0.0));
        return;
    }

    if (believable < 0.5)
    {
        uHistoryColour.Store(px, float4(current, 1.0 - reactive));
        uHistoryMetadata.Store(px, float4(cov / N, 0.0, 0.0, 0.0));
        return;
    }

    float4 hist4 = SampleHistoryCatmullRom(prevUv);
    float3 hLin = max(hist4.rgb, 0.0);
    float count = N * uPrevHistoryMetadata.SampleLevel(uPrevHistoryMetadataSampler, prevUv, 0.0).r;
    if (!IsFiniteColour(hLin) || count < 1e-3)
    {
        uHistoryColour.Store(px, float4(current, 1.0 - reactive));
        uHistoryMetadata.Store(px, float4(cov / N, 0.0, 0.0, 0.0));
        return;
    }

    // Clip the history toward the centre of the box along the line joining them, which keeps its hue.
    float3 h = ToBox(hLin);
    float3 centre = (boxLo + boxHi) * 0.5;
    float3 ext = max((boxHi - boxLo) * 0.5, 1e-5);
    float3 e = h - centre;
    float3 ratio = abs(e) / ext;
    float spill = max(ratio.x, max(ratio.y, ratio.z));
    if (spill > 1.0)
    {
        h = centre + (e / spill);
    }

    float3 hClip = FromBox(h);

    // Where an additive effect was remembered, the unclipped history is not to be trusted either.
    float remembered = saturate(1.0 - hist4.a);
    relaxClamp = min(relaxClamp, 1.0 - remembered);

    float wClip = (1.0 - relaxClamp) * HdrWeight(hClip);
    float wLin = relaxClamp * HdrWeight(hLin);
    float3 hist = ((hClip * wClip) + (hLin * wLin)) / max(wClip + wLin, 1e-6);

    // How much of this frame goes in.
    float limit = lerp(N, 3.0, saturate(speed / 8.0));
    float newCount = min(min(count + cov, N), limit);
    float blend = newCount > 1e-6 ? saturate(cov / newCount) : 1.0;

    float amp = saturate(1.0 - (4.0 / N));
    float maxValid = 1.0 - (amp * saturate(speed));
    float lc = Luma(current);
    float lh = Luma(hist);
    maxValid = max(maxValid, saturate(abs(lc - lh) / max(max(lc, lh), 1e-4)));
    blend = max(blend, 1.0 - maxValid);

    float lf = min(shading, relaxClamp);
    blend = max(blend, saturate(2.0 * (0.5 - lf)));
    blend = max(blend, 0.85 * reactive);
    newCount = min(newCount, max(cov, 0.25) / max(blend, 1e-3));

    float wc = blend * HdrWeight(current);
    float wh = (1.0 - blend) * HdrWeight(hist);
    float3 result = ((current * wc) + (hist * wh)) / max(wc + wh, 1e-6);

    float memory = max(reactive, 0.5 * remembered);
    uHistoryColour.Store(px, float4(max(result, 0.0), 1.0 - memory));
    uHistoryMetadata.Store(px, float4(newCount / N, 0.0, 0.0, 0.0));
}
