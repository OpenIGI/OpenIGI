// The frame's luminance, counted into sixty-four logarithmic buckets for the auto exposure.
//
// Counted into a storage buffer rather than an image: WGSL has atomics on storage buffers and never on
// textures, so this is the form every target compiles.

[[vk::binding(0, 0)]] Texture2D<float4> uScene;
[[vk::binding(1, 0)]] SamplerState uSceneSampler;

[[vk::binding(2, 0)]] RWStructuredBuffer<Atomic<uint>> uBins;

// The lit scene, read for its alpha alone: which pixels are the world. The same image as uScene unless the
// picture metered is a reconstruction's output, which carries no such marker and may be larger.
[[vk::binding(4, 0)]] Texture2D<float4> uMarker;

struct Params
{
    // width, height, unused, unused.
    float4 size;

    // histogram scale, histogram bias, luminance floor, count scale.
    float4 range;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

// One shared bin per thread of the 8x8 group. Every pixel adding straight to the global histogram
// serialised millions of atomics onto sixty-four addresses and cost more than the whole shadow pass;
// accumulating per group in shared memory first leaves one global add per bin per group -- thousands,
// not millions.
//
// Declared as an explicit atomic rather than a plain uint with InterlockedAdd over it: WGSL's workgroup
// atomics are a distinct type, and Slang will not infer one from the operation.
groupshared Atomic<uint> sHistogram[64];

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID, uint slot : SV_GroupIndex)
{
    sHistogram[slot].store(0u);
    GroupMemoryBarrierWithGroupSync();

    int2 px = int2(id.xy);
    if (px.x < int(p.size.x) && px.y < int(p.size.y))
    {
        // Geometry that only exists in front of the eye is not part of the scene being metered. The
        // forward draws leave zero in the lit alpha for it and one for the world; a white knife held up
        // to the camera would otherwise raise the metered luminance and darken the whole frame, so that
        // merely holstering it changed the exposure of a room that had not moved.
        float4 lit = uScene.Load(int3(px, 0));
        uint markerWidth;
        uint markerHeight;
        uMarker.GetDimensions(markerWidth, markerHeight);
        int2 markerPx = int2((float2(px) + 0.5) * float2(float(markerWidth), float(markerHeight)) / p.size.xy);
        if (uMarker.Load(int3(markerPx, 0)).a >= 0.5)
        {
            // Luminance, as a light meter reads: the exposure puts the scene's middle at the middle of the
            // display, and that is a matter of how bright the scene looks. A plain mean of the channels
            // read a blue sky two or three times brighter than it is and every green or amber wall dimmer,
            // so an open sky darkened the ground under it.
            float raw = dot(lit.rgb, float3(0.2126, 0.7152, 0.0722));
            float floored = max(raw, p.range.z);

            // Where the pixel sits in the log range, split linearly between the two buckets either side
            // so a slow change in brightness moves the count smoothly rather than in steps.
            float position = saturate((p.range.x * log2(floored)) + p.range.y) * 63.0;
            uint low = uint(position);
            uint high = min(low + 1u, 63u);
            float upper = frac(position);
            float lower = 1.0 - upper;

            // A truly black pixel (cleared sky, unlit void) is not metered at all; a merely dim one
            // still counts, at the bottom of the range.
            if (low == 0u && raw <= 1e-7)
            {
                lower = 0.0;
            }

            uint lowCount = uint((lower * p.range.w) + 0.5);
            uint highCount = uint((upper * p.range.w) + 0.5);
            if (lowCount > 0u)
            {
                sHistogram[low].add(lowCount);
            }

            if (highCount > 0u)
            {
                sHistogram[high].add(highCount);
            }
        }
    }

    GroupMemoryBarrierWithGroupSync();

    uint total = sHistogram[slot].load();
    if (total > 0u)
    {
        uBins[slot].add(total);
    }
}
