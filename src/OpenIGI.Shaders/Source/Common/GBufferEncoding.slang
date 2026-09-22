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

// Shading models, matching the reference's numbering so a ported pass reads the same values.
static const int kShadingModelUnlit = 0;
static const int kShadingModelDefaultLit = 1;

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

#endif
