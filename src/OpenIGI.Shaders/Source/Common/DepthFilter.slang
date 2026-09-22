// A depth read between texels, filtered by the sampler where the target can and by the shader where
// it cannot.
//
// WebGPU cannot filter a depth image at all, and cannot filter a 32-bit float one without a feature
// many adapters lack; it binds either only as an unfilterable texture. So on WGSL the read is four
// plain loads weighted as a bilinear sampler weights them. On SPIR-V it stays the sampler's own, and
// that is not a matter of taste: the hardware's weights carry less precision than these, which at a
// silhouette -- where the four depths are metres apart -- moves the answer, and the legacy renderer's
// picture has to stay what it was. Measured: loading on SPIR-V as well moved mission 1's occlusion by
// up to 129 levels along edges.

#ifndef OPENIGI_DEPTH_FILTER_INCLUDED
#define OPENIGI_DEPTH_FILTER_INCLUDED

/// The depth at a texture coordinate, bilinearly weighted between the four nearest texels and clamped
/// at the edges, as a linear sampler with clamp-to-edge addressing reads it.
float DepthBilinear(Texture2D<float4> depth, SamplerState linear, float2 uv)
{
    __target_switch
    {
    case spirv:
        return depth.SampleLevel(linear, uv, 0.0).r;
    default:
        return DepthBilinearLoaded(depth, uv);
    }
}

/// The same read from a depth image declared as one, which is how WGSL has to see a depth attachment
/// bound for reading: a float texture of depth format is a binding WebGPU refuses.
float DepthBilinear(DepthTexture2D depth, SamplerState linear, float2 uv)
{
    __target_switch
    {
    case spirv:
        return depth.SampleLevel(linear, uv, 0.0);
    default:
        return DepthBilinearLoaded(depth, uv);
    }
}

/// The four loads and their weights, for a float image.
float DepthBilinearLoaded(Texture2D<float4> depth, float2 uv)
{
    uint width;
    uint height;
    depth.GetDimensions(width, height);

    int2 last = int2(int(width) - 1, int(height) - 1);
    float2 position = (uv * float2(float(width), float(height))) - 0.5;
    float2 floored = floor(position);
    float2 weight = position - floored;
    int2 origin = int2(floored);

    float d00 = depth.Load(int3(clamp(origin, int2(0, 0), last), 0)).r;
    float d10 = depth.Load(int3(clamp(origin + int2(1, 0), int2(0, 0), last), 0)).r;
    float d01 = depth.Load(int3(clamp(origin + int2(0, 1), int2(0, 0), last), 0)).r;
    float d11 = depth.Load(int3(clamp(origin + int2(1, 1), int2(0, 0), last), 0)).r;

    return lerp(lerp(d00, d10, weight.x), lerp(d01, d11, weight.x), weight.y);
}

/// The four loads and their weights, for a depth image.
float DepthBilinearLoaded(DepthTexture2D depth, float2 uv)
{
    uint width;
    uint height;
    depth.GetDimensions(width, height);

    int2 last = int2(int(width) - 1, int(height) - 1);
    float2 position = (uv * float2(float(width), float(height))) - 0.5;
    float2 floored = floor(position);
    float2 weight = position - floored;
    int2 origin = int2(floored);

    float d00 = depth.Load(int3(clamp(origin, int2(0, 0), last), 0));
    float d10 = depth.Load(int3(clamp(origin + int2(1, 0), int2(0, 0), last), 0));
    float d01 = depth.Load(int3(clamp(origin + int2(0, 1), int2(0, 0), last), 0));
    float d11 = depth.Load(int3(clamp(origin + int2(1, 1), int2(0, 0), last), 0));

    return lerp(lerp(d00, d10, weight.x), lerp(d01, d11, weight.x), weight.y);
}

#endif
