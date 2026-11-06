// Builds the lamp grid (see Common/LampGrid.slang): for every cell of the view, the lamps whose reach meets it.
//
// One thread a cell. Its bounds are the box round the eight points where the cell's screen rectangle meets its
// nearest and farthest depth -- the view position is linear in the screen fraction at a depth and in the depth at
// a screen fraction, so those corners are its extremes -- widened a little, as each lamp's reach is, so that no
// rounding between this and the reader's own reconstruction can leave out a lamp that reaches a pixel. A lamp
// listed that reaches no pixel of the cell costs its reader one reach test and adds nothing.

#ifndef OPENIGI_MAX_LAMPS
#error "OPENIGI_MAX_LAMPS must be defined by the shader build."
#endif

struct Params
{
    // tiles across, tiles down, slices, lamp count.
    float4 shape;

    // x scale, y scale, near, far: how a depth becomes a view position, as the shading has it.
    float4 unproject;

    // the nearest depth a slice starts at, slices per doubling of distance, tiles per screen across and down.
    float4 depth;

    // xyz view position, w reach.
    float4 positionRadius[OPENIGI_MAX_LAMPS];
};

[[vk::binding(0, 0)]] ConstantBuffer<Params> p;
[[vk::binding(1, 0)]] RWStructuredBuffer<uint> uLampGrid;

#include "../Common/LampGrid.slang"

// The margin every bound is widened by: a thousandth of its size and a millimetre.
static const float kRelativeMargin = 1e-3;
static const float kAbsoluteMargin = 1e-3;

float3 CornerAt(float u, float v, float z)
{
    float2 ndc = float2((u * 2.0) - 1.0, (v * 2.0) - 1.0);
    return float3((ndc.x * z) / p.unproject.x, (-ndc.y * z) / p.unproject.y, z);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    uint tilesX = uint(p.shape.x);
    uint tilesY = uint(p.shape.y);
    uint slices = uint(p.shape.z);
    int lampCount = int(p.shape.w + 0.5);

    if (id.x == 0u)
    {
        uLampGrid[0] = tilesX;
        uLampGrid[1] = tilesY;
        uLampGrid[2] = slices;
        uLampGrid[3] = asuint(p.depth.x);
        uLampGrid[4] = asuint(p.depth.y);
        uLampGrid[5] = asuint(p.depth.z);
        uLampGrid[6] = asuint(p.depth.w);
        uLampGrid[7] = 0u;
    }

    uint cells = tilesX * tilesY * slices;
    uint cell = id.x;
    if (cell >= cells)
    {
        return;
    }

    uint tx = cell % tilesX;
    uint ty = (cell / tilesX) % tilesY;
    uint sz = cell / (tilesX * tilesY);

    // The cell's screen rectangle, and its depths: the first slice runs from the eye, the last to the far plane.
    float u0 = float(tx) / p.depth.z;
    float u1 = float(tx + 1u) / p.depth.z;
    float v0 = float(ty) / p.depth.w;
    float v1 = float(ty + 1u) / p.depth.w;
    float z0 = sz == 0u ? 0.0 : p.depth.x * exp2(float(sz) / p.depth.y);
    float z1 = sz + 1u >= slices ? max(p.unproject.w, p.depth.x) * 1.01 : p.depth.x * exp2(float(sz + 1u) / p.depth.y);

    float3 lo = float3(1e30, 1e30, 1e30);
    float3 hi = float3(-1e30, -1e30, -1e30);
    for (int corner = 0; corner < 8; corner++)
    {
        float3 at = CornerAt((corner & 1) != 0 ? u1 : u0, (corner & 2) != 0 ? v1 : v0, (corner & 4) != 0 ? z1 : z0);
        lo = min(lo, at);
        hi = max(hi, at);
    }

    float3 margin = (abs(lo) + abs(hi)) * kRelativeMargin + kAbsoluteMargin;
    lo -= margin;
    hi += margin;

    uint base = kLampGridHeader + (cell * kLampGridStride);
    uint count = 0u;
    for (int i = 0; i < lampCount; i++)
    {
        float4 lamp = p.positionRadius[i];
        float reach = (lamp.w * (1.0 + kRelativeMargin)) + kAbsoluteMargin;
        float3 nearest = clamp(lamp.xyz, lo, hi) - lamp.xyz;
        if (dot(nearest, nearest) < reach * reach)
        {
            if (count < uint(kLampGridCellLamps))
            {
                uLampGrid[base + 1u + count] = uint(i);
            }

            count++;
        }
    }

    uLampGrid[base] = count > uint(kLampGridCellLamps) ? kLampGridAll : count;
}
