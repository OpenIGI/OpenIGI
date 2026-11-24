// A race's ghost laid over the lit scene, before the bloom and the tone map, so its glow blooms as a light's would.
//
// Its bodies are the ghost mask's (World/GhostMask.slang). Over each:
//
//   - a faint fill, the scene behind dimmed a little through it, so the figure reads as a body and not a hole;
//   - its rim glowing, and a line along its edge from the mask's coverage, brighter than either, with a soft halo about it;
//   - the scan's bands and sweep climbing it, and a fine raster of rows across it, as a projection's;
//   - grain, and a slow uneven flicker;
//   - where the scene stands nearer than the ghost -- it is behind a wall -- the same figure at a third of its strength, so the
//     runner is never lost but never mistaken for being in the open.
//
// And now and then the projection fails for a moment (the glitch, its strength and seed set each frame by the presentation):
// rows of it torn sideways by bands, its three colours pulled apart across the screen, blocks of it dropped out, and bright
// lines of noise through it. At nought none of that is computed.
//
// The result is linear light; the ghost's own colours are divided by the exposure the resolve will multiply back, so it looks
// the same in a dark room as in the sun. Bound by name, in the shape of the mods' passes (Mod/PostPass.slang), which every
// backend already runs.

#include "../Common/ViewDepth.slang"
#include "../Common/Unroll.slang"

// The lit scene, linear and unexposed.
[[vk::binding(0, 0)]] Texture2D<float4> uSource;

// The scene's depth, reverse-Z, nought for the sky, at the scene's own resolution.
[[vk::binding(1, 0)]] DepthTexture2D uDepth;

// The ghost mask, at the source's resolution.
[[vk::binding(2, 0)]] Texture2D<float4> uGhost;

// Where the picture goes, every texel of it.
[[vk::binding(3, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct GhostParams
{
    // the target's width, height, 1/width, 1/height.
    float4 size;

    // the depth's width and height, and the depth's size over the target's.
    float4 depth;

    // the projection's x scale, y scale, near and far, in metres.
    float4 unproject;

    // the seconds shown, the exposure the resolve applies, the glitch's strength (nought to one), the glitch's seed.
    float4 look;

    // the ghost's colour, linear, and its strength.
    float4 tint;
};

[[vk::binding(4, 0)]] ConstantBuffer<GhostParams> p;

float Hash(float2 seed)
{
    float3 q = frac(float3(seed.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float3 SourceAt(int2 pixel)
{
    int2 last = int2(p.size.xy) - 1;
    return uSource.Load(int3(clamp(pixel, int2(0, 0), last), 0)).rgb;
}

// The mask at a pixel, or nothing off the image.
float4 GhostAt(int2 pixel)
{
    int2 size = int2(p.size.xy);
    if (any(pixel < int2(0, 0)) || any(pixel >= size))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    return uGhost.Load(int3(pixel, 0));
}

// How far ahead of the eye the scene stands under a pixel of the target, in metres, or the far plane for the sky.
float SceneMetresAt(int2 pixel)
{
    int2 last = int2(p.depth.xy) - 1;
    int2 at = int2((float2(pixel) + 0.5) * p.depth.zw);
    float d = uDepth.Load(int3(clamp(at, int2(0, 0), last), 0));
    return d > 0.0 ? ViewDepthAt(d, p.unproject) : p.unproject.w;
}

// The ghost's brightness at a mask pixel, and its fill, as the figure is drawn there; the scene's depth is read where the
// picture's pixel is, so a torn row is hidden by what stands where it lands.
float2 Figure(int2 at, float sceneMetres)
{
    float4 m = GhostAt(at);

    // The edge: any of the eight about it, one and two pixels off, outside the figure; and the halo, how much of a ring about
    // a pixel outside it is inside.
    float around = 0.0;
    float ring = 0.0;
    OPENIGI_UNROLL
    for (int k = 0; k < 8; k++)
    {
        float angle = float(k) * 0.78539816;
        int2 offset = int2(round(float2(cos(angle), sin(angle)) * 1.5));
        around += GhostAt(at + offset).a;
        ring += GhostAt(at + int2(round(float2(cos(angle), sin(angle)) * 4.0))).a;
    }

    if (m.a <= 0.0)
    {
        return float2(ring * 0.035, 0.0);
    }

    float edge = saturate((8.0 - around) * 0.5);
    float body = (1.0 / max(m.b, 1e-4)) - 1.0;
    float gap = body - sceneMetres;
    float shown = gap <= 0.25 + (body * 0.01) ? 1.0 : 0.33;

    float fill = 0.08;
    float bright = fill + (0.8 * m.r) + (0.5 * m.g) + (1.2 * edge);
    return float2(bright * shown, shown);
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

    // The glitch: a row band torn sideways, the colours pulled apart, a block dropped out.
    int2 at = pixel;
    int split = 0;
    bool dropped = false;
    float noiseLine = 0.0;
    if (glitch > 0.0)
    {
        float bandHeight = 4.0 + floor(Hash(float2(seed, 1.7)) * 28.0);
        float band = floor(float(pixel.y) / bandHeight);
        if (Hash(float2(band, seed)) < glitch * 0.7)
        {
            at.x += int((Hash(float2(band, seed + 7.3)) - 0.5) * glitch * 0.14 * p.size.x);
        }

        split = int(glitch * (3.0 + (Hash(float2(seed, 3.1)) * 12.0)));
        float2 block = floor(float2(pixel) / 24.0);
        dropped = Hash(block + float2(seed, seed * 0.37)) < glitch * 0.22;
        noiseLine = Hash(float2(float(pixel.y), seed * 1.31)) < glitch * 0.04 ? 1.0 : 0.0;
    }

    float sceneMetres = SceneMetresAt(pixel);
    float2 g = Figure(at, sceneMetres);
    float red = split != 0 ? Figure(at + int2(split, 0), sceneMetres).x : g.x;
    float blue = split != 0 ? Figure(at - int2(split, 0), sceneMetres).x : g.x;
    if (max(max(g.x, red), blue) <= 0.0)
    {
        uTarget.Store(pixel, float4(scene, 1.0));
        return;
    }

    // The projection's own grain, raster and flicker.
    float frame = floor(seconds * 30.0);
    float grain = Hash(float2(pixel) + float2(frame * 7.13, frame * 3.71));
    float raster = (pixel.y % 3) == 0 ? 0.7 : 1.0;
    float flicker = 0.9 + (0.1 * sin(seconds * 23.0) * sin(seconds * 7.3));
    float shimmer = (0.85 + (0.3 * grain)) * raster * flicker;
    if (dropped)
    {
        shimmer = 0.0;
    }

    // White at the brightest, the ghost's colour about it; the three colours each from where the glitch pulled them.
    float3 tint = p.tint.rgb;
    float3 white = float3(0.85, 0.97, 1.0);
    float3 colour = float3(
        lerp(tint.r, white.r, saturate(red - 0.8)) * red,
        lerp(tint.g, white.g, saturate(g.x - 0.8)) * g.x,
        lerp(tint.b, white.b, saturate(blue - 0.8)) * blue);

    // A bright line of noise where one crosses the figure.
    colour += white * noiseLine * (0.4 + (0.6 * grain)) * glitch;

    float strength = p.tint.w / max(p.look.y, 1e-3);
    float3 dimmed = scene * (1.0 - (0.22 * g.y * (dropped ? 0.0 : 1.0)));
    uTarget.Store(pixel, float4(dimmed + (colour * shimmer * strength), 1.0));
}
