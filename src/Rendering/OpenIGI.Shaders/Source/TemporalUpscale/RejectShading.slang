#ifndef OPENIGI_UPSCALE_GROUP
#error "OPENIGI_UPSCALE_GROUP must be defined by the shader build."
#endif

[[vk::binding(0, 0)]] Texture2D<float4> uCurrent;
[[vk::binding(1, 0)]] SamplerState uCurrentSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uDilatedVelocity;
[[vk::binding(3, 0)]] SamplerState uDilatedVelocitySampler;
[[vk::binding(4, 0)]] Texture2D<float4> uPrevGuide;
[[vk::binding(5, 0)]] SamplerState uPrevGuideSampler;
[[vk::binding(6, 0)]] Texture2D<float4> uParallaxRejection;
[[vk::binding(7, 0)]] SamplerState uParallaxRejectionSampler;
[[vk::binding(8, 0)]] [[vk::image_format("r8")]] WTexture2D<float4> uRejection;
[[vk::binding(9, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uGuide;
[[vk::binding(10, 0)]] Texture2D<float4> uThinGeometry;
[[vk::binding(11, 0)]] SamplerState uThinGeometrySampler;

struct Params
{
    float4 size;
    float4 control;
};

[[vk::binding(12, 0)]] ConstantBuffer<Params> p;

#define GROUP OPENIGI_UPSCALE_GROUP

#include "../Common/Unroll.slang"

// The shading verdict of the temporal upscaler: does the reprojected history still LOOK like this
// frame? 1 means it does; toward 0, the history has to give way.
//
// Both images are taken into a compressive space first, then each is clamped into the other's local
// range and back again (two rounds of mutual neighbourhood clamping), which strips the differences that
// are only aliasing -- a texel that one frame's jitter caught and the other's missed. What remains is
// compared after a small binomial blur: the energy a box around this frame's neighbourhood would have
// to remove from the blurred history, against how much the two disagree at all. That energy is
// median-filtered (it is speckly) and spread over a 3x3 before it is turned into the verdict.
//
// The pass also writes this frame's guide: the colour the next frame compares against, which follows
// this frame where the history was rejected and keeps the history where it was not.
//
// Every stage is a 3x3 filter over the previous one, so the work is tiled through group-shared memory:
// each stage is evaluated over the group plus the apron the stages after it still need, and only the
// two base images are clamped at the frame's edge.

static const int R_LOAD = 6;
static const int T_LOAD = GROUP + 12;
static const int R_MID = 5;
static const int T_MID = GROUP + 10;
static const int R_IN = 4;
static const int T_IN = GROUP + 8;

// This frame and the reprojected guide, in the compressive space (radius 6). Once the history-side
// chain no longer needs the guide, its tile holds the removed energy instead.
groupshared float sInput[T_LOAD * T_LOAD * 3];
groupshared float sHistory[T_LOAD * T_LOAD * 3];

// The first-round clamps (radius 5): the history clamped into this frame's range, then this frame
// clamped into the history's, then the blurred second-round history.
groupshared float sMid[T_MID * T_MID * 3];

// The second-round clamps (radius 4), and the blurred second-round input -- replaced by the median of
// the removed energy once that blur has been consumed.
groupshared float sInClamped[T_IN * T_IN * 3];
groupshared float sHistClamped[T_IN * T_IN * 3];
groupshared float sBlur[T_IN * T_IN * 3];

// Offsets of a cell, relative to the group's origin, into a tile of a given extent and radius.
#define TILE_INDEX(T, R, q) (((((q).y + (R)) * (T)) + ((q).x + (R))) * 3)
#define TILE_READ(tile, T, R, q) float3(tile[TILE_INDEX(T, R, q)], tile[TILE_INDEX(T, R, q) + 1], tile[TILE_INDEX(T, R, q) + 2])
#define TILE_WRITE(tile, T, R, q, v) \
    { \
        int at_ = TILE_INDEX(T, R, q); \
        float3 v_ = (v); \
        tile[at_] = v_.x; \
        tile[at_ + 1] = v_.y; \
        tile[at_ + 2] = v_.z; \
    }

// The cell a thread handles on one pass over a region of radius r around the group.
int2 RegionCell(uint i, int r)
{
    int extent = GROUP + (2 * r);
    return int2(int(i) % extent, int(i) / extent) - r;
}

uint RegionCount(int r)
{
    int extent = GROUP + (2 * r);
    return uint(extent * extent);
}

static const float kQ = 0.5 / 1024.0;

// The binomial weight of a 3x3 offset.
float Binomial(int x, int y)
{
    float wx = x == 0 ? 0.5 : 0.25;
    float wy = y == 0 ? 0.5 : 0.25;
    return wx * wy;
}

// Puts the per-channel smaller of two values in a and the larger in b.
void Order(inout float3 a, inout float3 b)
{
    float3 lo = min(a, b);
    b = max(a, b);
    a = lo;
}

// The exact per-channel median of nine values: a 19-exchange sorting network that orders only as much
// as the middle value needs.
float3 Median9(float3 v[9])
{
    float3 p0 = v[0];
    float3 p1 = v[1];
    float3 p2 = v[2];
    float3 p3 = v[3];
    float3 p4 = v[4];
    float3 p5 = v[5];
    float3 p6 = v[6];
    float3 p7 = v[7];
    float3 p8 = v[8];
    Order(p1, p2);
    Order(p4, p5);
    Order(p7, p8);
    Order(p0, p1);
    Order(p3, p4);
    Order(p6, p7);
    Order(p1, p2);
    Order(p4, p5);
    Order(p7, p8);
    Order(p0, p3);
    Order(p5, p8);
    Order(p4, p7);
    Order(p3, p6);
    Order(p1, p4);
    Order(p2, p5);
    Order(p4, p7);
    Order(p4, p2);
    Order(p6, p4);
    Order(p4, p2);
    return p4;
}

[numthreads(GROUP, GROUP, 1)]
void computeMain(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID, uint lid : SV_GroupIndex)
{
    int2 size = int2(p.size.xy);
    int2 limit = size - 1;
    int2 origin = int2(groupId.xy) * GROUP;
    int2 local = int2(localId.xy);
    int2 px = origin + local;
    float exposure = p.control.x;
    const uint stride = uint(GROUP * GROUP);

    // Stage 0: the two base images, edge-replicated, over the full apron.
    for (uint i = lid; i < RegionCount(R_LOAD); i += stride)
    {
        int2 q = RegionCell(i, R_LOAD);
        int2 t = clamp(origin + q, int2(0, 0), limit);
        float3 x = max(uCurrent.Load(int3(t, 0)).rgb, 0.0) * exposure;
        float3 inputValue = x / (x + 0.17);
        float2 v = uDilatedVelocity.Load(int3(t, 0)).xy;
        float2 prevUv = clamp(((float2(t) + 0.5) * p.size.zw) - v, 0.0, 1.0);
        float3 historyValue = max(uPrevGuide.SampleLevel(uPrevGuideSampler, prevUv, 0.0).rgb, 0.0);
        TILE_WRITE(sInput, T_LOAD, R_LOAD, q, inputValue);
        TILE_WRITE(sHistory, T_LOAD, R_LOAD, q, historyValue);
    }

    GroupMemoryBarrierWithGroupSync();

    // This thread's own two values, kept before either tile is reused.
    float3 inputPx = TILE_READ(sInput, T_LOAD, R_LOAD, local);
    float3 historyPx = TILE_READ(sHistory, T_LOAD, R_LOAD, local);

    // Stage 1a: the history clamped into this frame's 3x3 range.
    for (uint i = lid; i < RegionCount(R_MID); i += stride)
    {
        int2 q = RegionCell(i, R_MID);
        float3 lo = float3(1e30, 1e30, 1e30);
        float3 hi = float3(-1e30, -1e30, -1e30);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                float3 s = TILE_READ(sInput, T_LOAD, R_LOAD, q + int2(x, y));
                lo = min(lo, s);
                hi = max(hi, s);
            }
        }

        TILE_WRITE(sMid, T_MID, R_MID, q, clamp(TILE_READ(sHistory, T_LOAD, R_LOAD, q), lo, hi));
    }

    GroupMemoryBarrierWithGroupSync();

    // Stage 2a: this frame clamped into the range of that clamped history.
    for (uint i = lid; i < RegionCount(R_IN); i += stride)
    {
        int2 q = RegionCell(i, R_IN);
        float3 lo = float3(1e30, 1e30, 1e30);
        float3 hi = float3(-1e30, -1e30, -1e30);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                float3 s = TILE_READ(sMid, T_MID, R_MID, q + int2(x, y));
                lo = min(lo, s);
                hi = max(hi, s);
            }
        }

        TILE_WRITE(sInClamped, T_IN, R_IN, q, clamp(TILE_READ(sInput, T_LOAD, R_LOAD, q), lo, hi));
    }

    GroupMemoryBarrierWithGroupSync();

    // Stage 1b: this frame clamped into the history's 3x3 range (reusing the stage-1a tile).
    for (uint i = lid; i < RegionCount(R_MID); i += stride)
    {
        int2 q = RegionCell(i, R_MID);
        float3 lo = float3(1e30, 1e30, 1e30);
        float3 hi = float3(-1e30, -1e30, -1e30);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                float3 s = TILE_READ(sHistory, T_LOAD, R_LOAD, q + int2(x, y));
                lo = min(lo, s);
                hi = max(hi, s);
            }
        }

        TILE_WRITE(sMid, T_MID, R_MID, q, clamp(TILE_READ(sInput, T_LOAD, R_LOAD, q), lo, hi));
    }

    GroupMemoryBarrierWithGroupSync();

    // Stage 2b: the history clamped into the range of that clamped input.
    for (uint i = lid; i < RegionCount(R_IN); i += stride)
    {
        int2 q = RegionCell(i, R_IN);
        float3 lo = float3(1e30, 1e30, 1e30);
        float3 hi = float3(-1e30, -1e30, -1e30);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                float3 s = TILE_READ(sMid, T_MID, R_MID, q + int2(x, y));
                lo = min(lo, s);
                hi = max(hi, s);
            }
        }

        TILE_WRITE(sHistClamped, T_IN, R_IN, q, clamp(TILE_READ(sHistory, T_LOAD, R_LOAD, q), lo, hi));
    }

    GroupMemoryBarrierWithGroupSync();

    // Stage 3: both clamped images blurred, over radius 3.
    for (uint i = lid; i < RegionCount(3); i += stride)
    {
        int2 q = RegionCell(i, 3);
        float3 blurIn = float3(0.0, 0.0, 0.0);
        float3 blurHist = float3(0.0, 0.0, 0.0);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                float w = Binomial(x, y);
                blurIn += TILE_READ(sInClamped, T_IN, R_IN, q + int2(x, y)) * w;
                blurHist += TILE_READ(sHistClamped, T_IN, R_IN, q + int2(x, y)) * w;
            }
        }

        TILE_WRITE(sBlur, T_IN, R_IN, q, blurIn);
        TILE_WRITE(sMid, T_MID, R_MID, q, blurHist);
    }

    GroupMemoryBarrierWithGroupSync();

    // The disagreement floor at this thread's own pixel, read before the blur tile is reused.
    float3 disagreement;
    {
        float3 lo = float3(1e30, 1e30, 1e30);
        float3 hi = float3(-1e30, -1e30, -1e30);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                float3 s = TILE_READ(sInClamped, T_IN, R_IN, local + int2(x, y));
                lo = min(lo, s);
                hi = max(hi, s);
            }
        }

        float3 fi = TILE_READ(sBlur, T_IN, R_IN, local);
        float3 fh = TILE_READ(sMid, T_MID, R_MID, local);
        disagreement = max(abs(fi - fh), ((hi - lo) * 0.25) + (kQ * 0.5));
    }

    // Stage 4: the energy a box around this frame's blurred neighbourhood removes from the blurred
    // history, over radius 2. The guide tile is free now and takes it.
    for (uint i = lid; i < RegionCount(2); i += stride)
    {
        int2 q = RegionCell(i, 2);
        float3 fh = TILE_READ(sMid, T_MID, R_MID, q);
        float3 inC = TILE_READ(sInClamped, T_IN, R_IN, q);
        float3 inRaw = TILE_READ(sInput, T_LOAD, R_LOAD, q);

        float3 inLo = float3(1e30, 1e30, 1e30);
        float3 inHi = float3(-1e30, -1e30, -1e30);
        float3 boxLo = float3(1e30, 1e30, 1e30);
        float3 boxHi = float3(-1e30, -1e30, -1e30);
        float3 sumIn = float3(0.0, 0.0, 0.0);
        float3 sumDiff = float3(0.0, 0.0, 0.0);
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                int2 r = q + int2(x, y);
                float3 c = TILE_READ(sInClamped, T_IN, R_IN, r);
                float3 b = TILE_READ(sBlur, T_IN, R_IN, r);
                inLo = min(inLo, c);
                inHi = max(inHi, c);
                boxLo = min(boxLo, b);
                boxHi = max(boxHi, b);
                sumIn += c;
                sumDiff += abs(TILE_READ(sInput, T_LOAD, R_LOAD, r) - c);
            }
        }

        // On a wire too thin to resolve, let the box take in the history's own range as well, since
        // the two frames legitimately disagree there every frame.
        if (p.control.w > 0.0)
        {
            int2 t = clamp(origin + q, int2(0, 0), limit);
            float relax = saturate(uThinGeometry.Load(int3(t, 0)).g * p.control.w);
            if (relax > (1.0 / 127.0))
            {
                float3 hLo = float3(1e30, 1e30, 1e30);
                float3 hHi = float3(-1e30, -1e30, -1e30);
                OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
                {
                    OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
                    {
                        float3 b = TILE_READ(sMid, T_MID, R_MID, q + int2(x, y));
                        hLo = min(hLo, b);
                        hHi = max(hHi, b);
                    }
                }

                boxLo = lerp(boxLo, min(boxLo, hLo), relax);
                boxHi = lerp(boxHi, max(boxHi, hHi), relax);
            }
        }

        // Slack for the box: how much the centre stands out from its neighbourhood, so a lone
        // feature the box cannot represent is not counted as removed energy.
        float3 inSize = inHi - inLo;
        float3 lapIn = abs(((9.0 * inC) - sumIn) / 8.0);
        float3 lapDiff = abs(((9.0 * abs(inRaw - inC)) - sumDiff) / 8.0);
        float3 err = max(kQ, min(lapDiff, lapIn));
        err = max(err, inSize / 16.0);
        err += kQ;

        float3 removed = abs(clamp(fh, boxLo - err, boxHi + err) - fh);
        TILE_WRITE(sHistory, T_LOAD, R_LOAD, q, removed);
    }

    GroupMemoryBarrierWithGroupSync();

    // Stage 5: the removed energy median-filtered, over radius 1, into the blur tile.
    for (uint i = lid; i < RegionCount(1); i += stride)
    {
        int2 q = RegionCell(i, 1);
        float3 values[9];
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                values[((y + 1) * 3) + (x + 1)] = TILE_READ(sHistory, T_LOAD, R_LOAD, q + int2(x, y));
            }
        }

        TILE_WRITE(sBlur, T_IN, R_IN, q, Median9(values));
    }

    GroupMemoryBarrierWithGroupSync();

    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float3 energy = float3(0.0, 0.0, 0.0);
    OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
    {
        OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
        {
            energy = max(energy, TILE_READ(sBlur, T_IN, R_IN, local + int2(x, y)));
        }
    }

    float3 factor = saturate(1.0 - (energy / max(disagreement, 1e-6)));
    float rejection = min(factor.x, min(factor.y, factor.z));
    uRejection.Store(px, float4(rejection, 0.0, 0.0, 0.0));

    // The guide follows this frame wherever the history was rejected, by parallax or by shading.
    float parallax = 1.0;
    if (p.control.z > 0.5)
    {
        float2 uv = (float2(px) + 0.5) * p.size.zw;
        parallax = uParallaxRejection.SampleLevel(uParallaxRejectionSampler, uv, 0.0).r;
    }

    float total = parallax < 0.5 ? 1.0 : saturate(1.0 - (4.0 * rejection));
    float blend = max(p.control.y, max(1.0 - rejection, total));
    uGuide.Store(px, float4(lerp(historyPx, inputPx, blend), 1.0));
}
