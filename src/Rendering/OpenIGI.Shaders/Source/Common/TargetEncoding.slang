// Whether the target applies the sRGB transfer itself as it stores, and the one line that allows for it.
//
// The shaders that composite at the DISPLAY encoding finish there, which is right for a UNORM target
// that stores what it is given. A target with an sRGB format encodes again on the way in, and a picture
// encoded twice is a washed-out one -- so for such a target the result goes out decoded, and the store
// puts back exactly the encoding it took off.
//
// It DEFAULTS to a UNORM target; a pipeline drawing into an sRGB one supplies it from the target's
// format (see Mesh3DConstants).

#ifndef OPENIGI_TARGET_ENCODING_INCLUDED
#define OPENIGI_TARGET_ENCODING_INCLUDED

#include "GBufferEncoding.slang"

/// Whether the target encodes as it stores. Non-zero where it does.
[vk::constant_id(65)] const int cTargetEncodes = 0;

/// A colour at the display encoding, as the target wants to be handed it.
float4 ToTarget(float4 display)
{
    return cTargetEncodes != 0 ? float4(DecodeSrgb(display.rgb), display.a) : display;
}

#endif
