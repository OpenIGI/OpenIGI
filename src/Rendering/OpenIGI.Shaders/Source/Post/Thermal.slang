// The remastered thermal sensor: the lit scene re-imaged as heat, before the bloom and the tone map.
//
// Not the original's view, which leaves the world as it was and colours only its bodies (World/Infrared.slang). A sensor
// measures how warm everything is, so the whole picture is made over:
//
//   - the world's temperature is read off how much light it gives back, exposed as the frame is (the sensor's automatic gain)
//     and squeezed into a cool band, so the ground and the walls keep their shape and detail without competing with a person;
//     what blazes -- a muzzle flash, a fire, an explosion -- is hot; the sky is coldest, warmer towards the horizon; distance
//     fades everything towards the temperature of the air between;
//   - detail is lifted as a sensor's digital detail enhancement lifts it: what the picture holds against its own surround,
//     plus a faint line where the depth breaks, so edges stay crisp under the palette;
//   - the bodies are the heat mask's (World/ThermalMask.slang): hot where one stands in the open, warm air in a small glow
//     about it, and seen through cover a little way, fading out over the reach;
//   - the temperature goes through the palette, and then the sensor's own look over it: a faint fixed pattern of columns, a
//     grain that moves, a soft vignette and the slight warm spot a cooled sensor sees of itself at its centre;
//   - while the sensor calibrates, a shutter holds the picture at a flat field and the picture comes back noisy.
//
// The result is linear light divided by the exposure the resolve will apply, so the resolve gives back the palette's colours
// and blooms its hottest. It is mixed with the scene by the sensor's amount, so it fades in as the goggles come up.
//
// Bound by name, in the shape of the mods' passes (Mod/PostPass.slang), which every backend already runs.

#include "../Common/GBufferEncoding.slang"
#include "../Common/ViewDepth.slang"
#include "../Common/Unroll.slang"

// The lit scene, linear and unexposed.
[[vk::binding(0, 0)]] Texture2D<float4> uSource;

// The scene's depth, reverse-Z, nought for the sky, at the scene's own resolution.
[[vk::binding(1, 0)]] DepthTexture2D uDepth;

// The heat mask, at the source's resolution.
[[vk::binding(2, 0)]] Texture2D<float4> uHeat;

// Where the sensor's picture goes, every texel of it.
[[vk::binding(3, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct ThermalParams
{
    // the target's width, height, 1/width, 1/height.
    float4 size;

    // the depth's width and height, and the depth's size over the target's.
    float4 depth;

    // the projection's x scale, y scale, near and far, in metres.
    float4 unproject;

    // the amount, the palette (0 white-hot, 1 black-hot, 2 ironbow, 3 rainbow), the seconds, the calibration.
    float4 sensor;

    // the exposure the resolve applies, the noise strength, the see-through reach in metres, the frame's number.
    float4 tuning;
};

[[vk::binding(4, 0)]] ConstantBuffer<ThermalParams> p;

static const float3 Luma = float3(0.2126, 0.7152, 0.0722);

// The band the world's temperatures sit in, and where a body's start.
static const float WorldCold = 0.16;
static const float WorldWarm = 0.50;
static const float BodyFloor = 0.50;

// --------------------------------------------------------------------------------------------------------------------------
// Reads

float3 SourceAt(int2 pixel)
{
    int2 last = int2(p.size.xy) - 1;
    return uSource.Load(int3(clamp(pixel, int2(0, 0), last), 0)).rgb;
}

float4 HeatAt(int2 pixel)
{
    int2 last = int2(p.size.xy) - 1;
    return uHeat.Load(int3(clamp(pixel, int2(0, 0), last), 0));
}

// The scene's depth under a pixel of the target; the depth may be smaller after the scene is upscaled.
float DepthAt(int2 pixel)
{
    int2 last = int2(p.depth.xy) - 1;
    int2 at = int2((float2(pixel) + 0.5) * p.depth.zw);
    return uDepth.Load(int3(clamp(at, int2(0, 0), last), 0));
}

// How far ahead of the eye the depth stands, in metres, or the far plane for the sky.
float MetresAt(float depth)
{
    return depth > 0.0 ? ViewDepthAt(depth, p.unproject) : p.unproject.w;
}

// The exposed luminance the sensor's gain reads.
float SignalAt(int2 pixel)
{
    return dot(max(SourceAt(pixel), float3(0.0, 0.0, 0.0)), Luma) * p.tuning.x;
}

// --------------------------------------------------------------------------------------------------------------------------
// Noise

float Hash(float2 seed)
{
    float3 q = frac(float3(seed.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// --------------------------------------------------------------------------------------------------------------------------
// Temperature

// The world's temperature from its exposed luminance: a log curve squeezed into the cool band, so detail survives in the dark
// and in the bright alike, and only what truly blazes leaves the band.
float WorldTemperature(float signal)
{
    float shaped = log2(1.0 + (max(signal, 0.0) * 4.0)) / log2(5.0);
    float band = lerp(WorldCold, WorldWarm, saturate(shaped));

    // A blaze: a flash, a fire, a lamp's bulb, far brighter than anything lit.
    float blaze = smoothstep(2.5, 9.0, signal);
    return lerp(band, 0.97, blaze);
}

// The sky's temperature: cold overhead, warmer towards the horizon below it.
float SkyTemperature(float v)
{
    return lerp(0.03, 0.17, smoothstep(0.05, 0.62, v));
}

// --------------------------------------------------------------------------------------------------------------------------
// Palettes, as chained steps: each takes over from the last across its own eighth of the range. Written so rather than as an
// array, which WGSL will not index with a value it does not know until the shader runs.

float3 Ramp(float t, float3 c0, float3 c1, float3 c2, float3 c3, float3 c4, float3 c5, float3 c6, float3 c7)
{
    float x = saturate(t) * 7.0;
    float3 c = c0;
    c = lerp(c, c1, saturate(x));
    c = lerp(c, c2, saturate(x - 1.0));
    c = lerp(c, c3, saturate(x - 2.0));
    c = lerp(c, c4, saturate(x - 3.0));
    c = lerp(c, c5, saturate(x - 4.0));
    c = lerp(c, c6, saturate(x - 5.0));
    c = lerp(c, c7, saturate(x - 6.0));
    return c;
}

// A temperature as the palette shows it, at the display encoding.
float3 Palette(float t, int palette)
{
    if (palette == 1)
    {
        float k = pow(1.0 - saturate(t), 0.9);
        return float3(k, k, k) * float3(0.97, 0.99, 1.0);
    }

    if (palette == 2)
    {
        return Ramp(
            t,
            float3(0.00, 0.00, 0.04),
            float3(0.12, 0.01, 0.30),
            float3(0.40, 0.02, 0.52),
            float3(0.72, 0.08, 0.40),
            float3(0.90, 0.30, 0.10),
            float3(0.98, 0.58, 0.02),
            float3(1.00, 0.84, 0.30),
            float3(1.00, 0.99, 0.86));
    }

    if (palette == 3)
    {
        return Ramp(
            t,
            float3(0.00, 0.00, 0.10),
            float3(0.05, 0.08, 0.55),
            float3(0.00, 0.45, 0.85),
            float3(0.00, 0.75, 0.45),
            float3(0.55, 0.88, 0.05),
            float3(1.00, 0.70, 0.00),
            float3(0.95, 0.15, 0.05),
            float3(1.00, 0.95, 0.92));
    }

    // White-hot, a touch cool in its shadows as a sensor's display is.
    float k = pow(saturate(t), 0.9);
    return lerp(float3(0.015, 0.02, 0.03), float3(1.0, 1.0, 0.985), k);
}

// --------------------------------------------------------------------------------------------------------------------------

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= uint2(p.size.xy)))
    {
        return;
    }

    int2 pixel = int2(id.xy);
    float2 uv = (float2(pixel) + 0.5) * p.size.zw;
    float3 scene = SourceAt(pixel);
    float amount = saturate(p.sensor.x);
    if (amount <= 0.0)
    {
        uTarget.Store(pixel, float4(scene, 1.0));
        return;
    }

    // The 3x3 about the pixel: the signal for the gain and the detail, the depth for the breaks. A sensor resolves a little less
    // than a camera, so its own reading is a soft one.
    float signals[9];
    float metres[9];
    OPENIGI_UNROLL
    for (int dy = -1; dy <= 1; dy++)
    {
        OPENIGI_UNROLL
        for (int dx = -1; dx <= 1; dx++)
        {
            int k = ((dy + 1) * 3) + dx + 1;
            signals[k] = SignalAt(pixel + int2(dx, dy));
            metres[k] = MetresAt(DepthAt(pixel + int2(dx, dy)));
        }
    }

    float around = (signals[1] + signals[3] + signals[5] + signals[7]) * 0.25;
    float corners = (signals[0] + signals[2] + signals[6] + signals[8]) * 0.25;
    float signal = (signals[4] * 0.5) + (around * 0.375) + (corners * 0.125);
    float distance = metres[4];
    bool sky = DepthAt(pixel) <= 0.0;

    // The world, or the sky.
    float t = sky ? SkyTemperature(uv.y) : WorldTemperature(signal);

    // Digital detail enhancement: what the pixel holds against its surround, lifted, so texture and shape read through the
    // palette; and a faint line where the depth breaks, by the log of the distance so a near edge and a far one weigh alike.
    if (!sky)
    {
        float local = (around + corners) * 0.5;
        float detail = (WorldTemperature(signals[4]) - WorldTemperature(local)) * 0.9;
        float gx = (log2(metres[2] + 1.0) + (2.0 * log2(metres[5] + 1.0)) + log2(metres[8] + 1.0))
            - (log2(metres[0] + 1.0) + (2.0 * log2(metres[3] + 1.0)) + log2(metres[6] + 1.0));
        float gy = (log2(metres[6] + 1.0) + (2.0 * log2(metres[7] + 1.0)) + log2(metres[8] + 1.0))
            - (log2(metres[0] + 1.0) + (2.0 * log2(metres[1] + 1.0)) + log2(metres[2] + 1.0));
        float edge = saturate(sqrt((gx * gx) + (gy * gy)) * 0.35);
        t += clamp(detail, -0.08, 0.08) + (edge * 0.035);

        // The air between: distance pulls the world towards the air's own temperature.
        float haze = 1.0 - exp(-distance / 900.0);
        t = lerp(t, 0.24, haze * 0.65);
    }

    // The bodies. The mask holds the nearest body under the pixel; it stands in the open where the scene's depth is no nearer
    // than it, give or take its own thickness.
    float4 heat = HeatAt(pixel);
    float halo = 0.0;
    OPENIGI_UNROLL
    for (int ring = 0; ring < 8; ring++)
    {
        float angle = float(ring) * 0.78539816;
        float2 offset = float2(cos(angle), sin(angle));
        halo += HeatAt(pixel + int2(round(offset * 3.0))).g;
        halo += HeatAt(pixel + int2(round(offset * 7.0))).g * 0.6;
    }

    halo /= 12.8;
    if (heat.a > 0.0)
    {
        float body = (1.0 / max(heat.b, 1e-4)) - 1.0;
        float gap = body - (sky ? p.unproject.w : distance);
        float tolerance = 0.2 + (body * 0.01);
        if (gap <= tolerance)
        {
            t = max(t, BodyFloor + (0.5 * heat.r));
        }
        else
        {
            // Seen through cover: soft, cooler, fading out over the reach as what stands between thickens.
            float through = heat.g * (1.0 - smoothstep(0.0, p.tuning.z, gap - tolerance)) * 0.6;
            t = lerp(t, 0.42 + (0.36 * heat.g), through);
            halo *= 0.5;
        }
    }
    else
    {
        // The warm air about a body, only outside it.
        t += halo * 0.12;
    }

    // The sensor's look.
    float2 centre = (uv - 0.5) * float2(p.size.x * p.size.w, 1.0);
    float radius = length(centre);
    t += 0.018 * (1.0 - smoothstep(0.0, 0.32, radius));

    float calibration = saturate(p.sensor.w);
    float noise = p.tuning.y * (1.0 + (calibration * 4.0));
    float frame = p.tuning.w;
    float grain = Hash(float2(pixel) + float2(frame * 7.13, frame * 3.71)) - 0.5;
    float column = Hash(float2(float(pixel.x), 17.0)) - 0.5;
    float row = Hash(float2(41.0, float(pixel.y))) - 0.5;
    t += (grain * 0.03 * noise) + (column * 0.012 * p.tuning.y) + (row * 0.005 * p.tuning.y);

    // The shutter: a flat field while it is closed, the picture returning as it opens.
    float shutter = smoothstep(0.62, 0.8, calibration);
    t = lerp(t, 0.34 + (grain * 0.02), shutter);

    float3 display = Palette(t, int(p.sensor.y + 0.5));

    // A soft vignette, the eyepiece's, darker for a palette that shows heat bright.
    float vignette = 1.0 - (0.4 * smoothstep(0.42, 0.95, radius));
    display *= vignette;

    // Linear light, made a little brighter than white at the palette's peak so its hottest blooms, and divided by the exposure
    // the resolve will multiply back.
    float3 linearColour = DecodeSrgb(saturate(display)) * 1.3 / max(p.tuning.x, 1e-3);
    uTarget.Store(pixel, float4(lerp(scene, linearColour, amount), 1.0));
}
