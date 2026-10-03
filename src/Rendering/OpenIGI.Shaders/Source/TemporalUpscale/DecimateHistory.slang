#include "../Common/DepthSlot.slang"
#include "../Common/Unroll.slang"

OPENIGI_DEPTH_SLOT(0) uDepth;
[[vk::binding(1, 0)]] SamplerState uDepthSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uDilatedVelocity;
[[vk::binding(3, 0)]] SamplerState uDilatedVelocitySampler;
[[vk::binding(4, 0)]] Texture2D<float4> uPrevDepth;
[[vk::binding(6, 0)]] [[vk::image_format("r8")]] WTexture2D<float4> uRejection;
[[vk::binding(7, 0)]] StructuredBuffer<uint> uOccluder;

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

[[vk::binding(8, 0)]] ConstantBuffer<Params> p;

// The parallax verdict of the temporal upscaler: does the history at this pixel's previous position
// belong to the same surface? 1 means yes. Two tests are combined. The previous frame's depth around the
// reprojected position has to agree with the distance this surface had then, and nothing scattered into
// the occluder buffer may have stood in front of it there (a surface that was hidden last frame has no
// history of its own to inherit).

float ViewDistanceWith(float d, float near, float far)
{
    float n = 1.0 - (2.0 * d);
    return (2.0 * near * far) / ((far + near) - (n * (far - near)));
}

float ViewDistance(float d)
{
    return ViewDistanceWith(d, p.unproject.z, p.unproject.w);
}

// A distance, mapping the sky to the far plane.
float DistanceOrFar(float d)
{
    return d <= 0.0 ? p.unproject.w : ViewDistance(d);
}

float PixelHeight(float z)
{
    return z / max(0.5 * p.resolution.y * p.unproject.y, 1e-6);
}

float3 ViewPosition(float2 uv, float d)
{
    float z = ViewDistance(d);
    float2 ndc = (2.0 * uv) - 1.0;
    return float3((ndc.x * z) / p.unproject.x, (-ndc.y * z) / p.unproject.y, z);
}

float PreviousViewZ(float3 viewPos)
{
    float3 world = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz + p.cameraDelta.xyz;
    return mul(p.prevWorldToView, float4(world, 0.0)).z;
}

// The share of a bilinear footprint on the occluder buffer at which nothing nearer than zRef landed.
float OccluderMask(float2 prevRasterUv, float zRef, float eOcc, int2 size)
{
    int2 limit = size - 1;
    float2 q = (prevRasterUv * p.resolution.xy) - 0.5;
    float2 b = floor(q);
    float2 f = q - b;
    int2 base = int2(b);
    float mask = 0.0;
    OPENIGI_UNROLL for (int t = 0; t < 4; t++)
    {
        int2 o = int2(t & 1, t >> 1);
        float wx = o.x == 0 ? (1.0 - f.x) : f.x;
        float wy = o.y == 0 ? (1.0 - f.y) : f.y;
        float weight = wx * wy;
        int2 at = clamp(base + o, int2(0, 0), limit);
        uint word = uOccluder[(at.y * size.x) + at.x];
        if (word == 0u)
        {
            mask += weight;
        }
        else
        {
            float zOcc = asfloat(~word);
            float eps = max(6.0 * PixelHeight(zOcc), 1e-4) + eOcc;
            mask += weight * saturate(2.0 - ((zRef - zOcc) / eps));
        }
    }

    return mask;
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

    int2 limit = size - 1;
    float2 rasterUv = (float2(px) + 0.5) * p.resolution.zw;
    float d = OPENIGI_DEPTH_SAMPLE(uDepth, uDepthSampler, rasterUv);

    float2 currentUv = rasterUv - (p.jitter.xy * p.resolution.zw);
    float2 v = uDilatedVelocity.SampleLevel(uDilatedVelocitySampler, rasterUv, 0.0).xy;
    float2 prevRasterUv = currentUv - v + (p.jitter.zw * p.resolution.zw);
    if (any(prevRasterUv < 0.0) || any(prevRasterUv > 1.0))
    {
        uRejection.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    int2 prevPixel = int2(prevRasterUv * p.resolution.xy);
    const int2 kCross[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };

    // How much the occluder test has to forgive: the depth spread across neighbours that move with this
    // pixel, so a surface steep enough to cover several distances in one pixel does not occlude itself.
    float ownZ = DistanceOrFar(d);
    float2 ownV = uDilatedVelocity.Load(int3(px, 0)).xy * p.resolution.xy;
    float eOcc = 0.0;
    OPENIGI_UNROLL for (int i = 0; i < 4; i++)
    {
        int2 at = clamp(px + kCross[i], int2(0, 0), limit);
        float2 thatV = uDilatedVelocity.Load(int3(at, 0)).xy * p.resolution.xy;
        if (length(thatV - ownV) > 0.5)
        {
            continue;
        }

        float thatZ = DistanceOrFar(OPENIGI_DEPTH_LOAD(uDepth, at));
        eOcc = max(eOcc, abs(thatZ - ownZ));
    }

    if (d <= 0.0)
    {
        // Sky: its history is good where the sky was also visible last frame and nothing has moved in
        // front of it since.
        float wasSky = 0.0;
        OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
        {
            OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
            {
                int2 at = clamp(prevPixel + int2(x, y), int2(0, 0), limit);
                if (uPrevDepth.Load(int3(at, 0)).r <= 0.0)
                {
                    wasSky = 1.0;
                }
            }
        }

        float sky = min(wasSky, OccluderMask(prevRasterUv, p.prevProjection.w, eOcc, size));
        uRejection.Store(px, float4(sky, 0.0, 0.0, 0.0));
        return;
    }

    float3 viewPos = ViewPosition(currentUv, d);
    float prevViewZ = PreviousViewZ(viewPos);
    if (prevViewZ <= p.prevProjection.z)
    {
        uRejection.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // The tolerance the depth comparison grants: how much this surface's own distance varies across
    // its neighbours.
    float eSurf = 0.0;
    OPENIGI_UNROLL for (int j = 0; j < 4; j++)
    {
        float nd = OPENIGI_DEPTH_LOAD(uDepth, clamp(px + kCross[j], int2(0, 0), limit));
        if (nd <= 0.0)
        {
            continue;
        }

        eSurf = max(eSurf, abs(ViewDistance(nd) - viewPos.z));
    }

    float agreement = 0.0;
    OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
    {
        OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
        {
            int2 at = clamp(prevPixel + int2(x, y), int2(0, 0), limit);
            float prevD = uPrevDepth.Load(int3(at, 0)).r;
            if (prevD <= 0.0)
            {
                continue;
            }

            float zHist = ViewDistanceWith(prevD, p.prevProjection.z, p.prevProjection.w);
            float eps = max(6.0 * PixelHeight(zHist), 1e-4) + eSurf;
            agreement = max(agreement, saturate(2.0 - (abs(zHist - prevViewZ) / eps)));
        }
    }

    float verdict = min(agreement, OccluderMask(prevRasterUv, prevViewZ, eOcc, size));
    uRejection.Store(px, float4(verdict, 0.0, 0.0, 0.0));
}
