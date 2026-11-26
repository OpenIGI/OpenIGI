// A race's ghost laid over the lit scene, before the bloom and the tone map, as a runner under an optical cloak.
//
// Its bodies are the ghost mask's (World/GhostMask.slang). Over each:
//
//   - the scene behind bent through the body along its normal, more at its turning edges, a little apart in its three colours
//     and a little green: the runner is bent air, not a fill;
//   - a thin contour about it, quiet at rest, and a sweep climbing down it every few seconds that lights the contour and shows
//     the cloak's cells for a moment as it passes;
//   - where the scene stands nearer than the ghost -- it is behind a wall -- no cloak but a tracked signal: the contour alone
//     and a sparse dither, so the runner is never lost but never mistaken for being in the open;
//   - close to the eye, dithered away, so it never stands in the player's face at the start they share.
//
// When the cloak fails (its strength and seed set each frame by the presentation) it breaks in blocks: patches of the body
// show solid, in the cloak's green and shaded flat, their rows a little torn; the contour's three colours pull apart; a line of
// signal crosses it. As the failure ends the cloak closes again from the middle of the body out. As the ghost comes and goes
// its body burns in or out along a front of shards.
//
// The result is linear light; the ghost's own colours are divided by the exposure the resolve will multiply back, so it looks
// the same in a dark room as in the sun, and on every preset. Bound by name, in the shape of the mods' passes
// (Mod/PostPass.slang), which every backend already runs.

#include "../Common/ViewDepth.slang"
#include "../Common/Unroll.slang"

// The lit scene, linear and unexposed.
[[vk::binding(0, 0)]] Texture2D<float4> uSource;

// The scene's depth, reverse-Z, nought for the sky, at the scene's own resolution.
[[vk::binding(1, 0)]] DepthTexture2D uDepth;

// The ghost mask's shape and skin, at the source's resolution.
[[vk::binding(2, 0)]] Texture2D<float4> uGhostShape;
[[vk::binding(3, 0)]] Texture2D<float4> uGhostSkin;

// Where the picture goes, every texel of it.
[[vk::binding(4, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct GhostParams
{
    // the target's width, height, 1/width, 1/height.
    float4 size;

    // the depth's width and height, and the depth's size over the target's.
    float4 depth;

    // the projection's x scale, y scale, near and far, in metres.
    float4 unproject;

    // the seconds shown, the exposure the resolve applies, the cloak's failure (nought to one), the failure's seed.
    float4 look;

    // the ghost's colour, linear, and its strength.
    float4 tint;

    // the near-white of its brightest edges, linear; w unused.
    float4 hot;

    // x how much of the ghost is there, y how far the cloak has closed again after a failure; zw unused.
    float4 cloak;
};

[[vk::binding(5, 0)]] ConstantBuffer<GhostParams> p;

// How long the sweep takes to run down the body, in seconds.
static const float SweepSeconds = 3.5;

// Where the ghost is dithered away as it comes up to the eye, in metres: gone at the first, whole from the second.
static const float FadeGone = 0.7;
static const float FadeWhole = 2.0;

// How wide the front it burns in and out along is, in the noise's units.
static const float BurnWidth = 0.05;

// A cloak's cell across, in metres on the body.
static const float CellMetres = 0.09;

struct GhostSample
{
    float covered;
    float2 normal;
    float metres;
    float height;
    float noise;
    float rim;
};

float Hash(float2 seed)
{
    float3 q = frac(float3(seed.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float Square(float x)
{
    return x * x;
}

// An ordered 4x4 threshold, nought to one: the fades are dithered rather than blended, so nothing of the ghost is half there.
float Bayer4(int2 pixel)
{
    uint x = uint(pixel.x) & 3u;
    uint y = uint(pixel.y) & 3u;
    uint a = x ^ y;
    uint v = ((a & 1u) << 3u) | ((y & 1u) << 2u) | (a & 2u) | ((y & 2u) >> 1u);
    return (float(v) + 0.5) / 16.0;
}

// How far a point is from the middle of its hexagonal cell, nought there to a half at its edge.
float HexDistance(float2 at)
{
    float2 r = float2(1.0, 1.7320508);
    float2 h = r * 0.5;
    float2 a = at - (r * floor(at / r)) - h;
    float2 b = (at - h) - (r * floor((at - h) / r)) - h;
    float2 g = dot(a, a) < dot(b, b) ? a : b;
    float2 q = abs(g);
    return max(dot(q, float2(0.5, 0.8660254)), q.x);
}

float3 SourceAt(int2 pixel)
{
    int2 last = int2(p.size.xy) - 1;
    return uSource.Load(int3(clamp(pixel, int2(0, 0), last), 0)).rgb;
}

// The scene between its texels, so the bend through the body runs smoothly rather than in steps.
float3 SourceBetween(float2 at)
{
    float2 base = floor(at - 0.5);
    float2 f = (at - 0.5) - base;
    int2 i = int2(base);
    float3 top = lerp(SourceAt(i), SourceAt(i + int2(1, 0)), f.x);
    float3 bottom = lerp(SourceAt(i + int2(0, 1)), SourceAt(i + int2(1, 1)), f.x);
    return lerp(top, bottom, f.y);
}

// The mask at a pixel, or nothing off the image.
GhostSample GhostAt(int2 pixel)
{
    GhostSample g;
    int2 size = int2(p.size.xy);
    if (any(pixel < int2(0, 0)) || any(pixel >= size))
    {
        g.covered = 0.0;
        g.normal = float2(0.0, 0.0);
        g.metres = 0.0;
        g.height = 0.0;
        g.noise = 0.0;
        g.rim = 0.0;
        return g;
    }

    float4 shape = uGhostShape.Load(int3(pixel, 0));
    float4 skin = uGhostSkin.Load(int3(pixel, 0));
    g.covered = shape.w;
    g.normal = shape.xy;
    g.metres = shape.z;
    g.height = skin.x;
    g.noise = skin.y;
    g.rim = skin.z;
    return g;
}

float CoverageAt(int2 pixel)
{
    int2 size = int2(p.size.xy);
    return any(pixel < int2(0, 0)) || any(pixel >= size) ? 0.0 : uGhostShape.Load(int3(pixel, 0)).w;
}

// The contour at a pixel of the figure: any of the eight about it, one and a half pixels off, outside the figure.
float EdgeAt(int2 at)
{
    if (CoverageAt(at) <= 0.0)
    {
        return 0.0;
    }

    float around = 0.0;
    OPENIGI_UNROLL
    for (int k = 0; k < 8; k++)
    {
        float angle = float(k) * 0.78539816;
        around += CoverageAt(at + int2(round(float2(cos(angle), sin(angle)) * 1.5)));
    }

    return saturate((8.0 - around) * 0.5);
}

// The halo just outside the figure: how much of a ring four pixels about it is inside, and the nearest of it, in metres.
float2 HaloAt(int2 at)
{
    float ring = 0.0;
    float nearest = 1e6;
    OPENIGI_UNROLL
    for (int k = 0; k < 8; k++)
    {
        float angle = float(k) * 0.78539816;
        int2 tap = at + int2(round(float2(cos(angle), sin(angle)) * 4.0));
        GhostSample g = GhostAt(tap);
        ring += g.covered;
        nearest = g.covered > 0.0 ? min(nearest, g.metres) : nearest;
    }

    return float2(ring, nearest);
}

// How far ahead of the eye the scene stands under a pixel of the target, in metres, or the far plane for the sky.
float SceneMetresAt(int2 pixel)
{
    int2 last = int2(p.depth.xy) - 1;
    int2 at = int2((float2(pixel) + 0.5) * p.depth.zw);
    float d = uDepth.Load(int3(clamp(at, int2(0, 0), last), 0));
    return d > 0.0 ? ViewDepthAt(d, p.unproject) : p.unproject.w;
}

// The body as the failing cloak shows it: solid, in its green, lit flat from above and to the left, in three steps, its rows
// faintly lined.
float3 SolidBody(GhostSample g, int2 pixel)
{
    float nz = -sqrt(saturate(1.0 - dot(g.normal, g.normal)));
    float lambert = saturate(dot(float3(g.normal, nz), normalize(float3(-0.35, 0.55, -0.75))));
    float shade = 0.22 + (0.78 * (floor((lambert * 3.0) + 0.5) / 3.0));
    float rows = (pixel.y & 1) == 0 ? 1.0 : 0.82;
    return (p.tint.rgb * shade * rows * 0.9) + (p.hot.rgb * Square(g.rim) * 0.35);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= uint2(p.size.xy)))
    {
        return;
    }

    int2 pixel = int2(id.xy);
    float3 scene = SourceAt(pixel);
    float seconds = p.look.x;
    float glitch = saturate(p.look.z);
    float seed = p.look.w;
    float presence = saturate(p.cloak.x);
    float recloak = saturate(p.cloak.y);
    float strength = p.tint.w / max(p.look.y, 1e-3);
    float3 tint = p.tint.rgb;
    float3 hot = p.hot.rgb;

    // The cloak failing: the body broken into blocks, some rows of them torn a little sideways, some of them showing solid.
    int2 at = pixel;
    int2 torn = pixel;
    bool solid = false;
    int split = 0;
    if (glitch > 0.0)
    {
        int blockSize = 6 + int(Hash(float2(seed, 1.7)) * 10.0);
        int2 block = pixel / blockSize;
        if (Hash(float2(float(block.y), seed)) < glitch * 0.5)
        {
            at.x += int((Hash(float2(float(block.y), seed + 7.3)) - 0.5) * glitch * 4.0 * float(blockSize));
        }

        // The body in a failed block is read once, at the block's middle, so it shows as a block; its contour is still read
        // where each pixel is torn to, or one pixel of edge would fill the whole block with it.
        torn = at;
        solid = Hash(float2(block) + float2(seed * 0.37, seed)) < glitch * 0.9;
        if (solid)
        {
            at = ((at / blockSize) * blockSize) + (blockSize / 2);
        }

        split = int(glitch * (2.0 + (Hash(float2(seed, 3.1)) * 6.0)));
    }

    GhostSample g = GhostAt(at);

    // Close to the eye, dithered away.
    if (g.covered > 0.0 && Bayer4(pixel) >= smoothstep(FadeGone, FadeWhole, g.metres))
    {
        g.covered = 0.0;
    }

    if (g.covered <= 0.0)
    {
        // Outside the figure: its faint halo, and while it fails the contour's colours pulled out past it.
        float2 halo = HaloAt(torn);
        float3 light = tint * (halo.x * 0.012 * smoothstep(FadeGone, FadeWhole, halo.y));
        if (split != 0)
        {
            light += float3(tint.r * EdgeAt(torn + int2(split, 0)), 0.0, tint.b * EdgeAt(torn - int2(split, 0))) * 0.8;
        }

        uTarget.Store(pixel, float4(scene + (light * strength), 1.0));
        return;
    }

    float edge = EdgeAt(torn);
    float sweepAt = 1.15 - (frac(seconds / SweepSeconds) * 1.3);
    float band = exp(-Square((g.height - sweepAt) * 12.0));
    float sceneMetres = SceneMetresAt(pixel);
    bool hidden = g.metres - sceneMetres > 0.25 + (g.metres * 0.01);

    // A line of signal across the figure while the cloak fails.
    float signal = Hash(float2(float(pixel.y), seed * 1.31)) < glitch * 0.03 ? glitch : 0.0;

    // Burning in or out along its front of shards.
    float burn = presence < 0.999 ? saturate(1.0 - ((presence - g.noise) / BurnWidth)) : 0.0;

    float3 picture;
    if (hidden)
    {
        // Behind something: a tracked signal, the contour and a sparse dither, no cloak.
        float fill = Bayer4(pixel) < 0.14 ? 1.0 : 0.0;
        float light = (edge * (0.45 + (0.3 * band))) + (fill * 0.1) + (burn * 0.6);
        picture = scene + (tint * light * strength);
    }
    else if (solid)
    {
        // Where the cloak has failed: the body solid, its contour's colours pulled apart.
        float3 contour = float3(tint.r * EdgeAt(torn + int2(split, 0)), tint.g * edge, tint.b * EdgeAt(torn - int2(split, 0)));
        picture = lerp(scene, SolidBody(g, pixel) * strength, 0.9) + (((contour * 0.9) + (hot * signal * 0.5)) * strength);
    }
    else
    {
        // The cloak: the scene bent through the body, more at its turning edges, rippling slowly.
        float2 bend = float2(g.normal.x, -g.normal.y);
        float ripple = (sin((seconds * 2.3) + (g.height * 20.0) + (g.noise * 6.2831853)) * 0.5) + 0.5;
        float reach = (2.0 + (7.0 * g.rim)) * (0.8 + (0.4 * ripple)) * (p.size.y / 1080.0) * (1.0 + ((1.0 - recloak) * 1.5));
        float2 centre = float2(pixel) + 0.5;
        float2 offset = bend * reach;
        float3 bent = float3(
            SourceBetween(centre + (offset * 1.2)).r,
            SourceBetween(centre + offset).g,
            SourceBetween(centre + (offset * 0.8)).b);
        float luminance = dot(bent, float3(0.2126, 0.7152, 0.0722));
        float tintLuminance = max(dot(tint, float3(0.2126, 0.7152, 0.0722)), 1e-3);
        bent = lerp(bent, tint * (luminance / tintLuminance), 0.14);

        // The contour, quiet until the sweep lights it; the rim faint; the cloak's cells shown as the sweep passes.
        float cellPixels = max(5.0, CellMetres * p.unproject.y * p.size.y * 0.5 / max(g.metres, 0.1));
        float cells = smoothstep(0.40, 0.48, HexDistance(float2(pixel) / cellPixels)) * band * 0.35;
        float contour = edge * (0.22 + (0.95 * band));
        float light = contour + (Square(g.rim) * (0.06 + (0.25 * band))) + cells + 0.02;

        // Closing again after a failure, from the middle of the body out: a bright front, and the body solid beyond it.
        float3 rest = float3(0.0, 0.0, 0.0);
        if (recloak < 1.0)
        {
            float outward = abs(g.height - 0.5) * 2.0;
            light += exp(-Square((outward - recloak) * 10.0)) * (1.0 - recloak) * 0.8;
            rest = outward > recloak ? SolidBody(g, pixel) * 0.35 * (1.0 - recloak) : rest;
        }

        float3 colour = lerp(tint, hot, saturate((contour - 0.7) * 2.5)) * light;
        picture = bent + ((colour + rest + (hot * signal * 0.4)) * strength);
    }

    // The burning front: near-white at its edge, the body solid just behind it.
    if (burn > 0.0 && !hidden)
    {
        picture = lerp(picture, SolidBody(g, pixel) * strength, burn * 0.6) + (hot * burn * 1.2 * strength);
    }

    uTarget.Store(pixel, float4(picture, 1.0));
}
