// Gathers along the tile's velocity, weighting each sample by depth and by its own reach.

[[vk::binding(0, 0)]] Texture2D<float4> uColour;
[[vk::binding(1, 0)]] SamplerState uColourSampler;

// The same colour at half resolution.
[[vk::binding(2, 0)]] Texture2D<float4> uHalf;
[[vk::binding(3, 0)]] SamplerState uHalfSampler;

// (lengthPixels, angle01, viewDepth)
[[vk::binding(4, 0)]] Texture2D<float4> uFlat;
[[vk::binding(5, 0)]] SamplerState uFlatSampler;

// (min.xy, max.xy) in pixels
[[vk::binding(6, 0)]] Texture2D<float4> uDilated;
[[vk::binding(7, 0)]] SamplerState uDilatedSampler;

[[vk::binding(8, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;

    // tileGridW, tileGridH, maxSampleCount, unused.
    float4 control;
};

[[vk::binding(9, 0)]] ConstantBuffer<Params> p;

#include "../Common/FrameNoise.slang"

// Two decorrelated values per pixel: the noise at "frame" nought and one. It does not move over time.
float InterleavedGradientNoise(float2 pos, float frame)
{
    return FrameNoise(pos, frame);
}

// The largest sample count any pixel of the group needs, so the whole group loops the same number of
// times.
groupshared Atomic<uint> sGroupSamples;

// A colour along the motion, drawn from the half-resolution copy as far as the step spacing allows.
float4 BlurTap(float2 uv, float halfMix)
{
    return lerp(
        uColour.SampleLevel(uColourSampler, uv, 0.0),
        uHalf.SampleLevel(uHalfSampler, uv, 0.0),
        halfMix);
}

// How much a sample whose own blur is this wide reaches a point this many steps away.
float Reach(float spread, float steps, float stepsPerPixel)
{
    return saturate((stepsPerPixel * spread) - max(steps - 1.0, 0.0));
}

// How much one sample contributes: as background behind the centre if it lies further away, reached by
// the centre's own blur, and as foreground smeared over the centre if it lies nearer, reached by its
// blur. The split is soft over one unit of view depth.
float SampleWeight(float2 centreFlat, float2 sampleFlat, float steps, float stepsPerPixel)
{
    float depthDelta = 1.0 * (sampleFlat.y - centreFlat.y);
    float behind = saturate(0.5 + depthDelta);
    float inFront = saturate(0.5 - depthDelta);
    return (behind * Reach(centreFlat.x, steps, stepsPerPixel))
        + (inFront * Reach(sampleFlat.x, steps, stepsPerPixel));
}
[shader("compute")]
[numthreads(16, 16, 1)]
void computeMain(uint3 id : SV_DispatchThreadID, uint localIndex : SV_GroupIndex)
{
    int2 px = int2(id.xy);
    float2 pixel = float2(px);
    float noiseA = InterleavedGradientNoise(pixel, 0.0);
    float noiseB = InterleavedGradientNoise(pixel, 1.0);

    // The tile is read with a jitter of up to a quarter tile, filtered, so tile edges do not show as a
    // grid in the blur.
    float2 grid = p.control.xy;
    float2 jitter = (float2(noiseA, noiseB) - 0.5) * 0.5;
    float2 tileUv = ((pixel / 16.0) + jitter) / grid;
    tileUv = clamp(tileUv, float2(0.0, 0.0), float2(1.0, 1.0) - (0.5 / grid));
    float4 record = uDilated.SampleLevel(uDilatedSampler, tileUv, 0.0);
    float2 fastest = record.zw;
    float2 slowest = record.xy;
    float speed = length(fastest);

    // Every thread, in the frame or not, votes on the group's sample count before any of them leaves.
    uint wanted = uint(clamp(4.0 * ceil(speed / 4.0), 4.0, p.control.z));
    if (localIndex == 0u)
    {
        sGroupSamples.store(0u);
    }

    GroupMemoryBarrierWithGroupSync();
    sGroupSamples.max(wanted);
    GroupMemoryBarrierWithGroupSync();
    uint samples = sGroupSamples.load();
    uint halfSamples = samples / 2u;

    if (px.x >= int(p.size.x) || px.y >= int(p.size.y))
    {
        return;
    }

    float2 uv = (pixel + 0.5) * p.size.zw;
    float4 centre = uColour.SampleLevel(uColourSampler, uv, 0.0);
    if (speed < 0.5)
    {
        uTarget.Store(px, centre);
        return;
    }

    float2 search = fastest * p.size.zw;
    float halfMix = clamp((speed / float(samples)) - 1.0, 0.0, 1.0);
    float sides = float(halfSamples);

    // The whole tile moves nearly as one: every sample counts the same.
    if (dot(slowest, slowest) > 0.4 * speed * speed)
    {
        float4 sum = float4(0.0, 0.0, 0.0, 0.0);
        for (uint s = 0u; s < halfSamples; s++)
        {
            float ahead = (float(s) + noiseA) / sides;
            float behind = (float(s) + 1.0 - noiseA) / sides;
            sum += BlurTap(clamp(uv + (ahead * search), 0.0, 1.0), halfMix);
            sum += BlurTap(clamp(uv - (behind * search), 0.0, 1.0), halfMix);
        }

        uTarget.Store(px, sum * (1.0 / float(samples)));
        return;
    }

    float4 centreFlat = uFlat.SampleLevel(uFlatSampler, uv, 0.0);
    float2 centreSpreadDepth = centreFlat.xz;
    float stepsPerPixel = sides / length(fastest);

    float4 colour = float4(0.0, 0.0, 0.0, 0.0);
    float weightSum = 0.0;
    for (uint s = 0u; s < halfSamples; s++)
    {
        float ahead = (float(s) + noiseA) / sides;
        float behind = (float(s) + 1.0 - noiseA) / sides;
        float2 uvAhead = clamp(uv + (ahead * search), 0.0, 1.0);
        float2 uvBehind = clamp(uv - (behind * search), 0.0, 1.0);

        float4 flatAhead = uFlat.SampleLevel(uFlatSampler, uvAhead, 0.0);
        float4 flatBehind = uFlat.SampleLevel(uFlatSampler, uvBehind, 0.0);
        float4 colourAhead = BlurTap(uvAhead, halfMix);
        float4 colourBehind = BlurTap(uvBehind, halfMix);

        float steps = float(s) + 0.5;
        float weightAhead = SampleWeight(centreSpreadDepth, flatAhead.xz, steps, stepsPerPixel);
        float weightBehind = SampleWeight(centreSpreadDepth, flatBehind.xz, steps, stepsPerPixel);

        // Swap the pair's weights where one side is the further or the slower, so a foreground edge
        // smears over what lies behind it from both sides alike.
        bool furtherAhead = flatAhead.z > flatBehind.z;
        bool slowerAhead = flatAhead.x < flatBehind.x;
        float usedAhead = (furtherAhead && slowerAhead) ? weightBehind : weightAhead;
        float usedBehind = (furtherAhead || slowerAhead) ? weightBehind : weightAhead;

        colour += (colourAhead * usedAhead) + (colourBehind * usedBehind);
        weightSum += usedAhead + usedBehind;
    }

    float normalise = 0.5 / sides;
    colour *= normalise;
    weightSum *= normalise;
    uTarget.Store(px, colour + (max(1.0 - weightSum, 0.0) * centre));
}
