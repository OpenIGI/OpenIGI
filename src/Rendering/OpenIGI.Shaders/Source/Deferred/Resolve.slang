// The resolve: the lit HDR scene brought to the display, with everything that belongs to the lens rather
// than to the world -- chromatic aberration, sharpening, bloom, the vignette, the tone map, the grade and
// the grain.
//
// Compiled for SPIR-V alone, for one reason only: it binds its three images as combined image samplers,
// which is the shape the descriptor set it shares with the rest of the deferred chain already has, and
// WGSL has no combined samplers. Nothing else in it is Vulkan-specific, so a browser form is the same
// code with the textures and samplers declared apart.

#include "../Common/Fullscreen.slang"
#include "../Common/GBufferEncoding.slang"
#include "../Common/TargetEncoding.slang"
#include "../Common/PushConstants.slang"
#include "../Common/AcesToneMapping.slang"

// Declared apart rather than as combined samplers: on the WGSL target a combined sampler takes TWO
// binding slots and collides with whatever is declared next, so neither target can use one. Every other
// module in the library already reads its textures this way.
[[vk::binding(0, 0)]] Texture2D<float4> uScene;
[[vk::binding(1, 0)]] SamplerState uSceneSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uBloom;
[[vk::binding(3, 0)]] SamplerState uBloomSampler;
[[vk::binding(4, 0)]] Texture3D<float4> uColourLut;
[[vk::binding(5, 0)]] SamplerState uColourLutSampler;

struct Resolve
{
    // exposure, tone-map operator, white point, mask-view flag.
    float4 params;

    // intensity, aspect (height/width), bloomEnabled, quantization dither (one output code value).
    float4 vignette;

    // amount, animation phase, grade amount, grade cube edge.
    float4 grain;

    // chromatic aberration in corner pixels, sharpen, one texel x and y.
    float4 lens;

    // contrast, saturation, green phosphor, night shift.
    float4 grade;
};

OPENIGI_PUSH_CONSTANT(Resolve, resolve);

typedef FullscreenVaryings Varyings;

// The full-screen triangle, generated from the vertex index so there is no vertex buffer to bind.
[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

// The scene, with the two things a lens does to it before anything else: the channels pulled apart
// towards the corners, and an unsharp mask.
float3 SceneColour(float2 uv)
{
    float2 texel = resolve.lens.zw;
    float3 colour;

    if (resolve.lens.x > 0.0)
    {
        float2 fromCentre = uv - 0.5;
        float2 shift = fromCentre * dot(fromCentre, fromCentre) * resolve.lens.x * texel * 4.0;
        colour = float3(
            uScene.Sample(uSceneSampler, uv + shift).r,
            uScene.Sample(uSceneSampler, uv).g,
            uScene.Sample(uSceneSampler, uv - shift).b);
    }
    else
    {
        colour = uScene.Sample(uSceneSampler, uv).rgb;
    }

    if (resolve.lens.y > 0.0)
    {
        float3 blurred =
            uScene.Sample(uSceneSampler, uv + float2(texel.x, 0.0)).rgb
            + uScene.Sample(uSceneSampler, uv - float2(texel.x, 0.0)).rgb
            + uScene.Sample(uSceneSampler, uv + float2(0.0, texel.y)).rgb
            + uScene.Sample(uSceneSampler, uv - float2(0.0, texel.y)).rgb;
        colour = max(colour + ((colour - (blurred * 0.25)) * resolve.lens.y), float3(0.0, 0.0, 0.0));
    }

    return colour;
}

// Saturation, contrast, and the green-phosphor monochrome the night-vision goggles are.
float3 ApplyGrade(float3 colour)
{
    if (resolve.grade.y != 1.0)
    {
        float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
        colour = lerp(float3(luminance, luminance, luminance), colour, resolve.grade.y);
    }

    if (resolve.grade.x != 1.0)
    {
        colour = ((colour - 0.5) * resolve.grade.x) + 0.5;
    }

    if (resolve.grade.z > 0.5)
    {
        float luminance = dot(max(colour, float3(0.0, 0.0, 0.0)), float3(0.2126, 0.7152, 0.0722));
        float phosphor = pow(luminance, 1.10);
        colour = float3(phosphor * 0.018, phosphor * 0.55, phosphor * 0.060);
    }

    return colour;
}

// A colour cube, read with the half-texel inset every lookup table needs so that the ends of the range
// land on texel centres rather than on the border.
float3 ApplyLut(float3 colour)
{
    float amount = resolve.grain.z;
    float size = resolve.grain.w;
    if (amount <= 0.0 || size < 2.0)
    {
        return colour;
    }

    float scale = (size - 1.0) / size;
    float offset = 0.5 / size;
    float3 graded = uColourLut.Sample(uColourLutSampler, (clamp(colour, 0.0, 1.0) * scale) + offset).rgb;
    return lerp(colour, graded, clamp(amount, 0.0, 1.0));
}

float3 ApplyGrain(float3 colour, float2 uv)
{
    float noise = frac(
        sin(dot(uv + frac(resolve.grain.y), float2(12.9898, 78.233))) * 43758.5453);

    // The film grain, an artistic choice, uniform and as strong as r_grain asks.
    if (resolve.grain.x > 0.0)
    {
        colour += (noise - 0.5) * resolve.grain.x;
    }

    // The quantization dither, so the 8-bit target does not band in slow gradients. The same hash is
    // folded into a triangular distribution reaching one output code either side: with that shape the
    // rounding error's mean and its variance no longer depend on the signal, which a flat dither of the
    // same width does not achieve.
    if (resolve.vignette.w > 0.0)
    {
        float signed = (noise * 2.0) - 1.0;
        float triangular = sign(signed) * (1.0 - sqrt(saturate(1.0 - abs(signed))));
        colour += triangular * resolve.vignette.w;
    }

    return colour;
}

// Natural vignetting of a thin lens: light reaching the film at an angle theta off the axis falls off as
// cos^4(theta). The screen position stands in for tan(theta), in the same units on both axes and
// normalised so the corners always sit at radius sqrt(2) whatever the aspect; the intensity scales it.
float VignetteMask(float2 uv)
{
    float intensity = resolve.vignette.x;
    if (intensity <= 0.0)
    {
        return 1.0;
    }

    float aspect = resolve.vignette.y;
    float2 s = (uv * 2.0) - 1.0;
    s.y *= aspect;
    float2 r = s * (sqrt(2.0 / (1.0 + (aspect * aspect))) * intensity);

    // cos^2 = 1 / (1 + tan^2), squared once more for the fourth power.
    float cosSquared = 1.0 / (1.0 + dot(r, r));
    return cosSquared * cosSquared;
}

// The Purkinje shift: the eye's move from cone to rod vision as the light fails, applied to the exposed
// scene before the tone map.
//
// Rods see one channel only, weighted towards blue-green, so a surface dim enough for them to carry is seen
// by its scotopic brightness alone and without colour. Moonlit ground therefore reads as a blue-grey, and
// red goes dark before blue does. Brighter light stays with the cones, so a lamp pool or a lit window stays
// in full colour. The blend crosses between the two over the mesopic range, by each pixel's own exposed
// luminance, which is what keeps it from being a blue filter over the whole frame.
//
// The blue itself is not in the rods; it is the brain's reading of their grey against the cones it knows
// have gone quiet. Artists paint moonlight blue for the same reason, and a cool tint carries it.
float3 ApplyNightShift(float3 colour)
{
    float amount = resolve.grade.w;
    if (amount <= 0.0)
    {
        return colour;
    }

    const float3 photopic = float3(0.2126, 0.7152, 0.0722);

    // Only a breath of cool: the dim parts lose most of their colour, not all of it, and keep their own
    // hue under it. Taken to a strong blue at four-fifths, every dusk shadow went the same cold grey, where
    // a night photograph keeps warm darks warm.
    const float3 nightTint = float3(0.86, 0.93, 1.0);

    float luminance = dot(colour, photopic);

    // Scotopic sensitivity peaks near 507 nm; this is that curve reduced to linear-sRGB primaries.
    float scotopic = dot(colour, float3(0.03, 0.56, 0.41));
    float3 rodColour = scotopic * nightTint / dot(nightTint, photopic);

    float rods = amount * (1.0 - smoothstep(0.02, 0.45, luminance));
    return lerp(colour, rodColour, rods * 0.35);
}

float3 Reinhard(float3 c, float white)
{
    return c * (1.0 + (c / (white * white))) / (1.0 + c);
}

float4 ResolveDisplay(Varyings input);

// Everything below arrives at the display encoding; see TargetEncoding.slang for why a target that
// encodes is handed it decoded.
[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    return ToTarget(ResolveDisplay(input));
}

/// The resolved frame at the display encoding, whatever the target does with it.
float4 ResolveDisplay(Varyings input)
{
    float2 vUv = input.uv;

    // The mask view: the target shown as it stands, with nothing applied to it at all.
    if (resolve.params.w > 0.5)
    {
        return float4(EncodeSrgb(clamp(uScene.Sample(uSceneSampler, vUv).rgb, 0.0, 1.0)), 1.0);
    }

    float3 colour = SceneColour(vUv) * resolve.params.x;
    int op = int(resolve.params.y + 0.5);

    if (op == 1)
    {
        return float4(
            clamp(
                ApplyGrain(
                    ApplyGrade(
                        ApplyLut(EncodeSrgb(clamp(Reinhard(colour, resolve.params.z), 0.0, 1.0)))),
                    vUv),
                0.0,
                1.0),
            1.0);
    }

    if (op >= 2)
    {
        float vignette = VignetteMask(vUv);
        // The bloom is built from the scene before exposure, so it is exposed with it. Added raw, its glow
        // swung against the eye's adaptation: a night's lamps and moon, exposed many times over, kept a
        // daylight-sized halo that all but vanished beside them, and a bright view's bloomed too strongly.
        if (resolve.vignette.z > 0.5)
        {
            colour += max(uBloom.Sample(uBloomSampler, vUv).rgb, float3(0.0, 0.0, 0.0)) * resolve.params.x;
        }

        colour = ApplyNightShift(colour) * vignette;
        return float4(
            clamp(
                ApplyGrain(ApplyGrade(ApplyLut(LinearToSrgbDisplay(ToneMapScene(colour)))), vUv),
                0.0,
                1.0),
            1.0);
    }

    return float4(
        clamp(ApplyGrain(ApplyGrade(ApplyLut(EncodeSrgb(clamp(colour, 0.0, 1.0)))), vUv), 0.0, 1.0),
        1.0);
}
