// Spreads the shading rejection outwards, so a pixel NEXT to a rejecting one is clamped too.
//
// The median of each three-by-three neighbourhood is computed once into shared memory and the
// minimum is taken from there, which costs one median per pixel instead of nine. The result is
// identical to computing each in place -- this is a rearrangement of where the same arithmetic
// happens, not a different filter -- so the picture must not move at all.

#include "../Common/Unroll.slang"

// The rejection is read by TEXEL and never sampled, so the sampler in the slot after it goes unused.
// It is reserved all the same: the compute pass gives every sampled binding an image slot and a
// sampler slot, and the numbering of everything after depends on that.
[[vk::binding(0, 0)]] Texture2D<float4> uRejection;
[[vk::binding(2, 0)]] [[vk::image_format("r8")]] WTexture2D<float4> uBlend;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

// An eight-by-eight group plus the one-pixel apron the three-by-three minimum reaches into.
static const int kApron = 10;
groupshared float sMedian[kApron * kApron];

float Median3(float a, float b, float c)
{
    return max(min(a, b), min(max(a, b), c));
}

// Tukey's ninther over the 3x3 around c: the median of the three row medians. Cheaper than a true
// nine-value median and close to it, which is all a speckle filter on a verdict needs.
float MedianAt(int2 c, int2 size)
{
    float rows[3];
    OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
    {
        float a = uRejection.Load(int3(clamp(c + int2(-1, y), int2(0, 0), size - 1), 0)).r;
        float b = uRejection.Load(int3(clamp(c + int2(0, y), int2(0, 0), size - 1), 0)).r;
        float e = uRejection.Load(int3(clamp(c + int2(1, y), int2(0, 0), size - 1), 0)).r;
        rows[y + 1] = Median3(a, b, e);
    }

    return Median3(rows[0], rows[1], rows[2]);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(
    uint3 id : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID,
    uint localIndex : SV_GroupIndex)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.size.xy);

    // No early exit for a pixel off the edge: it still has to reach the barrier, and a barrier some of
    // the group skipped is undefined. MedianAt clamps its own reads; only the store is guarded.
    int2 apron = (int2(groupId.xy) * 8) - int2(1, 1);
    for (uint i = localIndex; i < uint(kApron * kApron); i += 64u)
    {
        int2 cell = int2(int(i) % kApron, int(i) / kApron);
        sMedian[i] = MedianAt(apron + cell, size);
    }

    GroupMemoryBarrierWithGroupSync();

    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    // Take the minimum, so a pixel beside a rejecting one is treated as rejecting too.
    int2 local = int2(localId.xy) + int2(1, 1);
    float blend = 1.0;
    OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
    {
        OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
        {
            blend = min(blend, sMedian[((local.y + y) * kApron) + local.x + x]);
        }
    }

    uBlend.Store(px, float4(blend, 0.0, 0.0, 0.0));
}
