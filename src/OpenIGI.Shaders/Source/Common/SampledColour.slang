// What a colour texture's sample is, and the one line that makes it linear.
//
// The engine's textures hold display-encoded colour. The legacy renderer stores them in UNORM images,
// so a sample comes back still encoded and a stage that lights in linear space decodes it itself. The
// renderer over the RHI stores them in sRGB images, where the SAMPLER decodes -- before it filters,
// which is the more correct of the two -- and a stage that decoded again would square the curve: a
// dark green of 36 comes out at 5, and the whole world goes near black.
//
// So which of the two a pipeline is bound to is a constant of the pipeline, not of the shader. It
// DEFAULTS to the legacy renderer's images, so a pipeline built without supplying it draws exactly what
// it drew before. Only the RHI path sets it.

#ifndef OPENIGI_SAMPLED_COLOUR_INCLUDED
#define OPENIGI_SAMPLED_COLOUR_INCLUDED

#include "GBufferEncoding.slang"

/// Whether the sampler has already decoded a colour texture to linear. Non-zero where it has.
[vk::constant_id(66)] const int cSamplerDecodes = 0;

/// A colour texture's sample, in linear space whichever kind of image it came from.
float3 SampledLinear(float3 sampled)
{
    return cSamplerDecodes != 0 ? sampled : DecodeSrgb(sampled);
}

/// A colour texture's sample at the display encoding, whichever kind of image it came from -- for the
/// stages that composite the way the original did, in the encoding the texel was authored in.
float3 SampledDisplay(float3 sampled)
{
    return cSamplerDecodes != 0 ? EncodeSrgb(sampled) : sampled;
}

#endif
