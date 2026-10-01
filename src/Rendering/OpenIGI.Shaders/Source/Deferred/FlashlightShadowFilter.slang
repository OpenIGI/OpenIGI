// The flashlight's shadow, spread over the penumbra its occluders imply.
//
// One ray a pixel is a hard, slightly grainy edge: the point on the lens moves every frame, so the few
// pixels a penumbra covers flicker between lit and not. This averages each pixel with a disc of its
// neighbours sized by how far its shadow's caster stood from it -- a pixel beside a railing's foot keeps a
// knife edge, the same railing's shadow on a wall behind it softens -- and weighted by how nearly each
// neighbour lies on the same surface, so the shadow on a wall does not bleed onto the doorframe in front of
// it. Loads only, and no history: see Deferred/FlashlightShadow.slang.

#include "../Common/ViewDepth.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uRaw;
[[vk::binding(2, 0)]] DepthTexture2D uDepth;
[[vk::binding(4, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uMask;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 resolution;

    // xScale, yScale, near, far.
    float4 unproject;

    // the lens radius in metres, _, _, _.
    float4 lens;
};

[[vk::binding(5, 0)]] ConstantBuffer<Params> p;

// Twelve points spread evenly over the unit disc (a Vogel spiral), so the kernel is round and covers its
// area without the rows and columns a grid leaves in a soft edge.
static const float2 kDisc[12] =
{
    float2(0.2041, 0.0000),
    float2(-0.2677, 0.2455),
    float2(0.0369, -0.4941),
    float2(0.3421, 0.4696),
    float2(-0.6389, -0.1063),
    float2(0.6203, -0.3712),
    float2(-0.2033, 0.7394),
    float2(-0.3712, -0.7250),
    float2(0.8120, 0.2248),
    float2(-0.8455, 0.3283),
    float2(0.3176, -0.8911),
    float2(0.3419, 0.9398),
};

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.resolution.x) || px.y >= int(p.resolution.y))
    {
        return;
    }

    float4 centre = uRaw.Load(int3(px, 0));
    float depth = uDepth.Load(int3(px, 0));
    if (depth <= 0.0)
    {
        uMask.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    float viewZ = ViewDepthAt(depth, p.unproject);
    int2 last = int2(p.resolution.xy) - 1;

    // The widest penumbra among the nearest neighbours, so an edge softens on BOTH sides of it: the lit side
    // of a shadow's border has no blocked ray of its own to say how soft the border is.
    float ratio = centre.y;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            ratio = max(ratio, uRaw.Load(int3(clamp(px + (int2(x, y) * 2), int2(0, 0), last), 0)).y);
        }
    }

    // A pixel no shadow reaches keeps what it is.
    if (ratio <= 0.0)
    {
        uMask.Store(px, float4(centre.x, 0.0, 0.0, 0.0));
        return;
    }

    // The penumbra's half-width on the receiver in metres, then in pixels at this depth: a lens of radius r
    // casts an edge r * t / (D - t) wide either side of the hard one. At least a pixel, so the one-ray grain
    // is always averaged; at most a dozen, so a far wall's shadow does not dissolve.
    float metresPerPixel = (2.0 * viewZ) / max(p.unproject.x * p.resolution.x, 1e-4);
    float radius = clamp((p.lens.x * ratio) / max(metresPerPixel, 1e-6), 1.0, 12.0);

    // Rotated per pixel, so the twelve points do not stamp the same pattern into every edge.
    float angle = frac(sin(dot(float2(px), float2(12.9898, 78.233))) * 43758.5453) * 6.28318530718;
    float2 rotation = float2(cos(angle), sin(angle));

    float total = centre.x;
    float weights = 1.0;
    for (int i = 0; i < 12; i++)
    {
        float2 d = kDisc[i];
        float2 offset = float2((d.x * rotation.x) - (d.y * rotation.y), (d.x * rotation.y) + (d.y * rotation.x));
        int2 at = clamp(px + int2(round(offset * radius)), int2(0, 0), last);

        float tapDepth = uDepth.Load(int3(at, 0));
        if (tapDepth <= 0.0)
        {
            continue;
        }

        float gap = abs(ViewDepthAt(tapDepth, p.unproject) - viewZ);
        float weight = exp2(-gap / max(0.02 * viewZ, 0.01));
        total += uRaw.Load(int3(at, 0)).x * weight;
        weights += weight;
    }

    uMask.Store(px, float4(total / weights, ratio, 0.0, 0.0));
}
