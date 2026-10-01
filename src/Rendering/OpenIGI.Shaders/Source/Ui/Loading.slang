// The loading backdrop: a held frame of the level, defocused and graded under a title card.
//
// One fullscreen pass over the present target, blended over whatever the frame already holds so the
// reveal can fade it off the live level. The picture is sampled through a sixteen-tap disc whose
// orientation is hashed per pixel, which turns what would be sixteen visible ghosts at a wide radius
// into a smooth bokeh at the cost of a little noise -- and the grain that is added anyway hides it.
//
// Without a picture -- a level started from the command line with nothing yet on screen -- the same
// pass draws a slow, drifting steel surface with a faint grid, so a cold start reads as designed
// rather than as a colour with text on it.

#include "../Common/Fullscreen.slang"
#include "../Common/PushConstants.slang"
#include "../Common/SampledColour.slang"
#include "../Common/TargetEncoding.slang"

// Sixteen floats; see LoadingBackdropSettings.
struct LoadingPush
{
    float time;
    float blur;
    float desaturate;
    float vignette;
    float zoom;
    float grain;
    float scanline;
    float opacity;
    float letterbox;
    float hasPicture;
    float aspect;
    float sweep;
    float reserved0;
    float reserved1;
    float reserved2;
    float reserved3;
};

OPENIGI_PUSH_CONSTANT(LoadingPush, p);

[[vk::binding(0, 0)]] Texture2D<float4> uTexture;
[[vk::binding(1, 0)]] SamplerState uTextureSampler;

/// The vertex stage: one full-screen triangle generated from the vertex index.
[shader("vertex")]
FullscreenVaryings vertexMain(uint vertexIndex : SV_VertexID)
{
    return FullscreenTriangle(vertexIndex);
}

float Hash(float2 v)
{
    return frac(sin(dot(v, float2(127.1, 311.7))) * 43758.5453);
}

float ValueNoise(float2 v)
{
    float2 i = floor(v);
    float2 f = frac(v);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash(i);
    float b = Hash(i + float2(1.0, 0.0));
    float c = Hash(i + float2(0.0, 1.0));
    float d = Hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float Fbm(float2 v)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += amplitude * ValueNoise(v);
        v = (v * 2.03) + float2(17.0, 9.0);
        amplitude *= 0.5;
    }

    return sum;
}

// A Poisson disc: sixteen points spread evenly over the unit circle.
static const float2 kDisc[16] = {
    float2(-0.613, -0.617), float2(0.170, -0.040), float2(-0.299, 0.735), float2(0.645, 0.507),
    float2(0.421, -0.735), float2(-0.859, 0.118), float2(0.902, -0.117), float2(-0.183, -0.291),
    float2(0.219, 0.418), float2(-0.556, 0.343), float2(0.702, 0.129), float2(-0.114, 0.976),
    float2(-0.383, -0.912), float2(0.512, -0.372), float2(-0.762, -0.290), float2(0.061, -0.665),
};

/// The fragment stage: the defocused picture, or the drifting steel, graded and vignetted.
[shader("fragment")]
float4 fragmentMain(FullscreenVaryings input) : SV_Target
{
    float2 uv = input.uv;
    float3 colour;

    if (p.hasPicture > 0.5)
    {
        // The slow push in, about the centre.
        float2 centred = ((uv - 0.5) / max(p.zoom, 0.5)) + 0.5;

        // Disc radius as a fraction of the height, corrected to stay round on a wide frame, and
        // turned per pixel so the taps do not line up into visible copies of the picture.
        float radius = p.blur * 0.022;
        float2 scale = float2(radius / max(p.aspect, 0.1), radius);
        float turn = Hash(uv * float2(1919.0, 1079.0)) * 6.2831853;

        // Column-major, matching the GLSL mat2(cos, sin, -sin, cos) this replaces: that constructor
        // fills the first COLUMN with (cos, sin), so the same product needs mul(v, m) here.
        float2x2 rotate = float2x2(cos(turn), sin(turn), -sin(turn), cos(turn));

        float3 sum = float3(0.0, 0.0, 0.0);
        for (int i = 0; i < 16; i++)
        {
            float2 tap = centred + (mul(kDisc[i], rotate) * scale);
            sum += SampledDisplay(uTexture.Sample(uTextureSampler, clamp(tap, float2(0.002, 0.002), float2(0.998, 0.998))).rgb);
        }

        colour = sum / 16.0;
    }
    else
    {
        // Brushed steel in the dark: a drifting low-frequency field with a faint grid drawn over.
        float2 field = (uv * float2(3.0 * p.aspect, 3.0)) + float2(p.time * 0.025, -p.time * 0.017);
        float n = Fbm(field);
        float3 deep = float3(0.030, 0.038, 0.050);
        float3 lift = float3(0.105, 0.130, 0.165);
        colour = lerp(deep, lift, (n * 0.85) + ((1.0 - uv.y) * 0.20));

        float2 cell = frac(uv * float2(20.0 * p.aspect, 20.0));
        float edge = min(min(cell.x, cell.y), min(1.0 - cell.x, 1.0 - cell.y));
        float grid = 1.0 - smoothstep(0.0, 0.05, edge);
        colour += grid * float3(0.55, 0.70, 0.90) * 0.045;
    }

    // A cool monochrome grade, taken as far as asked and dimmed with it, so the card reads.
    float luma = dot(colour, float3(0.299, 0.587, 0.114));
    float3 graded = lerp(float3(luma, luma, luma), float3(luma, luma, luma) * float3(0.84, 0.94, 1.10), 0.65);
    colour = lerp(colour, graded, p.desaturate);
    colour *= lerp(1.0, 0.52, p.desaturate);

    // A soft diagonal light drifting across, so the still is never quite still.
    float band = uv.x + (uv.y * 0.35) - p.sweep;
    float light = exp(-band * band * 36.0);
    colour += light * float3(0.085, 0.100, 0.125) * p.desaturate;

    // The edges fall away.
    float2 v = (uv - 0.5) * float2(1.10, 1.0);
    float falloff = smoothstep(0.98, 0.28, length(v));
    colour *= lerp(1.0, falloff, p.vignette);

    // Scanlines and grain, both slight; the grain also hides the disc's noise.
    colour *= 1.0 - (p.scanline * (0.5 + (0.5 * sin(uv.y * 1800.0))));
    colour += (Hash((uv * float2(1920.0, 1080.0)) + frac(p.time * 7.31)) - 0.5) * p.grain;

    // The cinematic bars, with a soft edge.
    float bars = p.letterbox;
    float inside = smoothstep(bars - 0.003, bars + 0.003, uv.y)
        * smoothstep(bars - 0.003, bars + 0.003, 1.0 - uv.y);
    colour *= inside;

    // Graded at the display encoding, as the held frame was authored; see TargetEncoding.slang.
    return ToTarget(float4(max(colour, float3(0.0, 0.0, 0.0)), p.opacity));
}
