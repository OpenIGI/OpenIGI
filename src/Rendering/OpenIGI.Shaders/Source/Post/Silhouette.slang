// The silhouette highlight: what the binoculars marked, drawn over the finished picture as a translucent fill inside a soft
// outline, seen through walls.
//
// The marked bodies were drawn first into a mask of their own (one channel a group: hostile, team, disabled), with no depth
// test, so a body behind a wall is in it as much as one in the open. This pass reads that mask at each pixel and the
// twenty-four round it, two either way:
//
//   - a pixel is inside a group where the group's channel is set;
//   - every neighbour whose membership differs from the pixel's is an edge neighbour; their count n, the sum S of their
//     offsets and the least squared offset m (one, two, four, five or eight) give the pixel's distance from the edge,
//     0.9 max(|S|/n, sqrt m), and no edge neighbour at all puts it out of reach;
//   - the outline's weight falls from its full opacity as exp(-4 (s max(d - 1, 0))^2), s being two over its width, so it
//     straddles the silhouette and is gone three pixels from it whatever the width;
//   - inside, the fill's colour at its opacity; the outline laid over it, premultiplied: rgb = fill + e (outline - fill),
//     alpha = 1 - (1 - fill alpha)(1 - e);
//   - a group may fade to nothing towards the screen's edges, as 16 sat(.5 + u.x) sat(.5 + u.y) sat(.5 - u.x) sat(.5 - u.y)
//     where u is the pixel's offset from the centre over the height, scaled by one over each axis's radius; the hostile has
//     no such fade, the other two share one.
//
// The groups are laid lowest first, each over the last, so the hostile lies on top. The result is premultiplied and blended
// over the picture (one, one minus source alpha), its alpha left alone.

#include "../Common/Fullscreen.slang"
#include "../Common/PushConstants.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uMask;

struct SilhouetteParams
{
    // width, height, two over the outline's width, unused.
    float4 frame;

    // The hostile's screen-edge fade, one over each radius across and up (nought for none), then the other groups'.
    float4 vignettes;

    // Each group's fill and outline colour, rgb and opacity, in the target's own light: hostile, team, disabled.
    float4 fill0;
    float4 outline0;
    float4 fill1;
    float4 outline1;
    float4 fill2;
    float4 outline2;
};

OPENIGI_PUSH_CONSTANT(SilhouetteParams, p);

typedef FullscreenVaryings Varyings;

[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

// The mask at a pixel, or nothing off the image.
float4 Mask(int2 pixel)
{
    int2 size = int2(p.frame.xy);
    if (any(pixel < int2(0, 0)) || any(pixel >= size))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    return uMask.Load(int3(pixel, 0));
}

// The screen-edge fade at a pixel, by one over each radius; a radius of nought turns it off.
float EdgeFade(float2 position, float2 scale)
{
    float2 u = (position - (p.frame.xy * 0.5)) / p.frame.y * scale;
    return 16.0 * saturate(0.5 + u.x) * saturate(0.5 + u.y) * saturate(0.5 - u.x) * saturate(0.5 - u.y);
}

// One group laid over what is under it: its fill where the pixel is inside, its outline about the edge.
void Lay(inout float4 under, bool inside, float n, float2 offsets, float least, float4 fill, float4 outline, float scale, float fade)
{
    float mean = n > 0.0 ? length(offsets) / n : 100.0;
    float d = 0.9 * max(mean, sqrt(least));
    float x = scale * max(d - 1.0, 0.0);
    float e = outline.a * exp(-4.0 * x * x);

    float fillAlpha = inside ? fill.a : 0.0;
    float3 fillColour = fill.rgb * fillAlpha;
    float3 colour = fillColour + (e * (outline.rgb - fillColour));
    float alpha = 1.0 - ((1.0 - fillAlpha) * (1.0 - e));

    colour *= fade;
    alpha *= fade;
    under = float4(colour + (under.rgb * (1.0 - alpha)), alpha + (under.a * (1.0 - alpha)));
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float2 position = input.position.xy;
    float hostileFade = EdgeFade(position, p.vignettes.xy);
    float otherFade = EdgeFade(position, p.vignettes.zw);
    if (max(hostileFade, otherFade) <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    int2 pixel = int2(position);
    bool3 inside = Mask(pixel).rgb > 0.5;

    // The edge search, for all three groups at once: rows from below to above, each from right to left.
    float3 n = float3(0.0, 0.0, 0.0);
    float2 s0 = float2(0.0, 0.0);
    float2 s1 = float2(0.0, 0.0);
    float2 s2 = float2(0.0, 0.0);
    float3 least = float3(100.0, 100.0, 100.0);
    for (int dy = 2; dy >= -2; dy--)
    {
        for (int dx = 2; dx >= -2; dx--)
        {
            if (dx == 0 && dy == 0)
            {
                continue;
            }

            bool3 there = Mask(pixel + int2(dx, dy)).rgb > 0.5;
            float2 offset = float2(dx, dy);
            float squared = dot(offset, offset);
            if (there.x != inside.x)
            {
                n.x += 1.0;
                s0 += offset;
                least.x = min(least.x, squared);
            }

            if (there.y != inside.y)
            {
                n.y += 1.0;
                s1 += offset;
                least.y = min(least.y, squared);
            }

            if (there.z != inside.z)
            {
                n.z += 1.0;
                s2 += offset;
                least.z = min(least.z, squared);
            }
        }
    }

    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    Lay(result, inside.z, n.z, s2, least.z, p.fill2, p.outline2, p.frame.z, otherFade);
    Lay(result, inside.y, n.y, s1, least.y, p.fill1, p.outline1, p.frame.z, otherFade);
    Lay(result, inside.x, n.x, s0, least.x, p.fill0, p.outline0, p.frame.z, hostileFade);
    return result;
}
