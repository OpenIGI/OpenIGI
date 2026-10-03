// Spreads each tile's fastest motion to the neighbours it could smear across.
//
// A pixel can only be blurred by something that moves over it, and an object near a tile's edge moves
// over the tiles beside it. So every tile looks a few tiles out in each direction and takes in any
// neighbour whose motion, laid along that neighbour's direction of travel, reaches back to it.

// Read by texel, so the sampler in the slot after goes unused; the slot is reserved because the
// compute pass gives every sampled binding an image slot and a sampler slot.
[[vk::binding(0, 0)]] Texture2D<float4> uTile;

[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uDilated;

struct Params
{
    // tile grid width, height, 1/width, 1/height.
    float4 size;

    // tileScale, unused, unused, unused.
    float4 control;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

// A velocity as (length, angle), the angle zero for no motion.
float2 ToPolar(float2 v)
{
    float len = length(v);
    return float2(len, len > 0.0 ? atan2(v.y, v.x) : 0.0);
}

float2 FromPolar(float2 polar)
{
    return float2(cos(polar.y), sin(polar.y)) * polar.x;
}

// How many tiles out the search looks, each way.
static const int kDilateReach = 3;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 tile = int2(id.xy);
    int2 grid = int2(p.size.xy);
    if (tile.x >= grid.x || tile.y >= grid.y)
    {
        return;
    }

    float4 centre = uTile.Load(int3(tile, 0));
    float2 fastest = ToPolar(centre.zw);
    float2 slowest = ToPolar(centre.xy);

    for (int y = -kDilateReach; y <= kDilateReach; y++)
    {
        for (int x = -kDilateReach; x <= kDilateReach; x++)
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            int2 neighbour = tile + int2(x, y);
            if (neighbour.x < 0 || neighbour.y < 0 || neighbour.x >= grid.x || neighbour.y >= grid.y)
            {
                continue;
            }

            float4 record = uTile.Load(int3(neighbour, 0));

            // The velocity is in pixels and the offset in tiles. Mixing them is deliberate: it makes the
            // reach test generous, so anything moving more than a few pixels is spread along its axis.
            float2 velocity = record.zw * p.control.x;
            float lengthSq = dot(velocity, velocity);
            float inverse = rsqrt(lengthSq + 1e-8);
            float speed = lengthSq * inverse;
            float2 direction = velocity * inverse;

            // A box around the neighbour stretched along its motion: as long as the motion plus a margin
            // and as wide as the margin. Standing still gives no margin, so it never spreads.
            float margin = 0.99 * (abs(direction.x) + abs(direction.y));
            float along = speed + margin;
            float across = margin;

            float2 offset = float2(float(x), float(y));
            float alongDistance = dot(direction, offset);
            float acrossDistance = dot(float2(-direction.y, direction.x), offset);
            if (abs(alongDistance) < along && abs(acrossDistance) < across)
            {
                float2 neighbourFast = ToPolar(velocity);
                float2 neighbourSlow = ToPolar(record.xy);
                fastest = fastest.x > neighbourFast.x ? fastest : neighbourFast;
                slowest = slowest.x > neighbourSlow.x ? neighbourSlow : slowest;
            }
        }
    }

    uDilated.Store(tile, float4(FromPolar(slowest), FromPolar(fastest)));
}
