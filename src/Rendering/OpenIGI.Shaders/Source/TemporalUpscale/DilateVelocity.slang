#include "../Common/DepthSlot.slang"
#include "../Common/Unroll.slang"

OPENIGI_DEPTH_SLOT(0) uDepth;
[[vk::binding(2, 0)]] Texture2D<float4> uVelocity;
[[vk::binding(4, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uDilatedVelocity;
[[vk::binding(5, 0)]] [[vk::image_format("r32f")]] WTexture2D<float4> uClosestDepth;
[[vk::binding(6, 0)]] RWStructuredBuffer<Atomic<uint>> uOccluder;

struct Params
{
    float4x4 viewToWorld;
    float4x4 prevWorldToView;
    float4 unproject;
    float4 resolution;
    float4 cameraDelta;
    float4 prevProjection;
    float4 jitter;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

// Motion dilation for the temporal upscaler.
//
// Each pixel takes its motion from the nearest of its axis neighbours when they both stand clearly in
// front of it, so a thin foreground silhouette carries its own motion one pixel outwards and the edge
// is not reprojected with the background's. The pass also scatters every pixel's previous-frame view
// distance into a buffer over the previous grid; the parallax test reads that back to find surfaces that
// were hidden behind something nearer last frame.

// Positive view distance from reverse-Z device depth.
float ViewDistance(float d)
{
    float near = p.unproject.z;
    float far = p.unproject.w;
    float n = 1.0 - (2.0 * d);
    return (2.0 * near * far) / ((far + near) - (n * (far - near)));
}

// Device depth from a positive view distance (the inverse of ViewDistance).
float DeviceDepth(float z)
{
    float near = p.unproject.z;
    float far = p.unproject.w;
    float n = ((far + near) - ((2.0 * near * far) / max(z, 1e-6))) / (far - near);
    return (1.0 - n) * 0.5;
}

// The view-space height one input pixel covers at a distance.
float PixelHeight(float z)
{
    return z / max(0.5 * p.resolution.y * p.unproject.y, 1e-6);
}

// The view position of an unjittered coordinate at a device depth.
float3 ViewPosition(float2 uv, float d)
{
    float z = ViewDistance(d);
    float2 ndc = (2.0 * uv) - 1.0;
    return float3((ndc.x * z) / p.unproject.x, (-ndc.y * z) / p.unproject.y, z);
}

// The view distance a surface had in the previous frame's view.
float PreviousViewZ(float3 viewPos)
{
    float3 world = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz + p.cameraDelta.xyz;
    return mul(p.prevWorldToView, float4(world, 0.0)).z;
}

float LoadDepth(int2 at, int2 size)
{
    return OPENIGI_DEPTH_LOAD(uDepth, clamp(at, int2(0, 0), size - 1));
}

[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.resolution.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float c = LoadDepth(px, size);

    // How far the device depth moves over two pixels of view-space height: anything smaller than that
    // is the surface's own slope, not a separate object in front.
    float z = ViewDistance(c);
    float h = PixelHeight(z);
    float noiseFloor = abs(DeviceDepth(z + (2.0 * h)) - c);

    int2 bestOffset = int2(0, 0);
    float bestDepth = c;
    const int2 axes[2] = { int2(1, 0), int2(0, 1) };
    OPENIGI_UNROLL for (int i = 0; i < 2; i++)
    {
        int2 a = axes[i];
        float s0 = LoadDepth(px + a, size);
        float s1 = LoadDepth(px - a, size);
        if ((((s0 + s1) * 0.5) - c) > max(0.25 * abs(s1 - s0), noiseFloor))
        {
            if (s0 > bestDepth)
            {
                bestDepth = s0;
                bestOffset = a;
            }

            if (s1 > bestDepth)
            {
                bestDepth = s1;
                bestOffset = -a;
            }
        }
    }

    float2 dilated = uVelocity.Load(int3(clamp(px + bestOffset, int2(0, 0), size - 1), 0)).xy;
    uDilatedVelocity.Store(px, float4(dilated, 0.0, 0.0));
    uClosestDepth.Store(px, float4(bestDepth, 0.0, 0.0, 0.0));

    // Scatter this pixel's own previous-frame distance over the bilinear footprint it lands on. The
    // complemented bits make the nearest distance the largest word, and zero the empty state.
    float2 rasterUv = (float2(px) + 0.5) * p.resolution.zw;
    float2 currentUv = rasterUv - (p.jitter.xy * p.resolution.zw);
    float prevViewZ = p.prevProjection.w;
    if (c > 0.0)
    {
        prevViewZ = PreviousViewZ(ViewPosition(currentUv, c));
    }

    if (prevViewZ > p.prevProjection.z)
    {
        float2 prevRasterUv = currentUv - dilated + (p.jitter.zw * p.resolution.zw);
        int2 base = int2(floor((prevRasterUv * p.resolution.xy) - 0.5));
        uint encoded = ~asuint(prevViewZ);
        OPENIGI_UNROLL for (int t = 0; t < 4; t++)
        {
            int2 at = base + int2(t & 1, t >> 1);
            if (at.x >= 0 && at.y >= 0 && at.x < size.x && at.y < size.y)
            {
                uOccluder[(at.y * size.x) + at.x].max(encoded);
            }
        }
    }
}
