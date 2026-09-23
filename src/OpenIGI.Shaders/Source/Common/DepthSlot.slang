// The scene's depth, bound for reading, in the form each target allows.
//
// WebGPU refuses a depth-format view on a float texture slot, and cannot filter a depth texture at all;
// so on WGSL the slot is declared a depth texture and every read is made of plain loads. SPIR-V keeps the
// float slot and the sampler's own reads it has always had: those are what the legacy renderer runs, and
// its picture has to stay what it was to the bit -- which is also why these are macros that expand to the
// very expressions they replace, rather than functions the compiler might emit differently.
//
// Declare with OPENIGI_DEPTH_SLOT(slot) uDepth; and read through the three macros below.

#ifndef OPENIGI_DEPTH_SLOT_INCLUDED
#define OPENIGI_DEPTH_SLOT_INCLUDED

#include "DepthFilter.slang"

#ifdef OPENIGI_TARGET_WGSL

#define OPENIGI_DEPTH_SLOT(slot) [[vk::binding(slot, 0)]] DepthTexture2D

// One texel.
#define OPENIGI_DEPTH_LOAD(depth, at) (depth).Load(int3((at), 0))

// Bilinear at a coordinate, made of four loads.
#define OPENIGI_DEPTH_SAMPLE(depth, filter, uv) DepthBilinearLoaded((depth), (uv))

// The four texels a bilinear footprint covers, in a gather's order.
#define OPENIGI_DEPTH_GATHER(depth, filter, uv) DepthGatherLoaded((depth), (uv))

/// The 2x2 footprint around a coordinate, ordered as GatherRed orders it: (0,1), (1,1), (1,0), (0,0).
float4 DepthGatherLoaded(DepthTexture2D depth, float2 uv)
{
    uint width;
    uint height;
    depth.GetDimensions(width, height);

    int2 last = int2(int(width) - 1, int(height) - 1);
    int2 origin = int2(floor((uv * float2(float(width), float(height))) - 0.5));

    return float4(
        depth.Load(int3(clamp(origin + int2(0, 1), int2(0, 0), last), 0)),
        depth.Load(int3(clamp(origin + int2(1, 1), int2(0, 0), last), 0)),
        depth.Load(int3(clamp(origin + int2(1, 0), int2(0, 0), last), 0)),
        depth.Load(int3(clamp(origin, int2(0, 0), last), 0)));
}

#else

#define OPENIGI_DEPTH_SLOT(slot) [[vk::binding(slot, 0)]] Texture2D<float4>
#define OPENIGI_DEPTH_LOAD(depth, at) (depth).Load(int3((at), 0)).r
#define OPENIGI_DEPTH_SAMPLE(depth, filter, uv) (depth).SampleLevel((filter), (uv), 0.0).r
#define OPENIGI_DEPTH_GATHER(depth, filter, uv) (depth).GatherRed((filter), (uv))

#endif

#endif
