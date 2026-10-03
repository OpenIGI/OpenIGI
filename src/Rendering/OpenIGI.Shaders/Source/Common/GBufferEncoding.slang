// How a surface is written into the G-buffer, and the colour decode that precedes it.
//
// These are the parts of DeferredShaderSource's shared GLSL prelude that the geometry pass uses.
// The prelude itself stays where it is until the passes that READ the G-buffer move too -- the
// lighting, the reflections and the rest are still GLSL and still splice it in. Until then the
// encode side lives here and the decode side lives there, and the two have to agree; that is the
// cost of migrating one pass at a time, and it is paid off when the readers follow.

#ifndef OPENIGI_GBUFFER_ENCODING_INCLUDED
#define OPENIGI_GBUFFER_ENCODING_INCLUDED

// The smallest roughness the G-buffer will carry. A perfectly smooth surface makes the specular
// lobe a delta function, which no finite sample count can integrate and which shows up as a single
// blindingly bright pixel; clamping just above zero keeps the lobe narrow but finite.
static const float kMinRoughness = 0.015625;

// Which lighting path the shading pass takes for a pixel, stored in the material target's alpha as a
// byte. Unlit surfaces (the sky and emissive draws) are shown as their own colour; default-lit ones
// take the full direct and indirect lighting.
static const int kShadingModelUnlit = 0;
static const int kShadingModelDefaultLit = 1;

// A flag carried above the shading model in the same byte, which the model's own decode masks off.
// MATTE: a surface with no specular response at all -- a body. The geometry pass already writes it
// with no specular and full roughness; the flag is for the two passes that would otherwise put shine
// back regardless, the wetness in the shading (a puddle on a level helmet) and the reflections.
static const int kShadingFlagMatte = 16;

float4 EncodeGBufferNormal(float3 worldNormal, float flags)
{
    return float4((normalize(worldNormal) * 0.5) + 0.5, flags);
}

float4 EncodeGBufferMaterial(float metallic, float specular, float roughness, int shadingModel)
{
    return float4(
        metallic,
        specular,
        max(roughness, kMinRoughness),
        float(shadingModel) / 255.0);
}

// How much light an unlit surface gives off, as a multiple of its own colour. It rides in the
// SPECULAR slot, which an unlit surface has no use for -- nothing reflects off something that is
// being treated as a source -- so a lamp's brightness costs no channel of its own. The packing is
// the game layer's: a byte at 128 + 16x is a strength of x, so the sky dome and every emissive draw
// that predates lamps sit at exactly one and come back exactly one.
float EncodeEmissiveStrength(float strength)
{
    return (128.0 + (clamp(strength, 0.0625, 7.9375) * 16.0)) / 255.0;
}

float3 DecodeSrgb(float3 c)
{
    float3 low = c / 12.92;
    float3 high = pow(max((c + 0.055) / 1.055, float3(0.0, 0.0, 0.0)), float3(2.4, 2.4, 2.4));
    return lerp(high, low, step(c, float3(0.04045, 0.04045, 0.04045)));
}

// The reading side, for the shading pass that unpacks what the geometry pass wrote.
float3 DecodeGBufferNormal(float4 packed)
{
    return normalize((packed.xyz * 2.0) - 1.0);
}

int DecodeShadingModel(float4 material)
{
    return int((material.a * 255.0) + 0.5) & 0xF;
}

bool DecodeMatte(float4 material)
{
    return (int((material.a * 255.0) + 0.5) & kShadingFlagMatte) != 0;
}

// One over the packing above: the byte carries 128 plus sixteen times the strength, and anything at or
// below the bias is a surface that was never marked emissive and so glows at exactly one.
float DecodeEmissiveStrength(float4 material)
{
    float packed = (material.g * 255.0) - 128.0;
    return packed > 0.5 ? (packed / 16.0) : 1.0;
}

float3 EncodeSrgb(float3 c)
{
    float3 low = c * 12.92;
    float3 high = (1.055 * pow(max(c, float3(0.0, 0.0, 0.0)), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)))
        - 0.055;
    return lerp(high, low, step(c, float3(0.0031308, 0.0031308, 0.0031308)));
}

#endif
