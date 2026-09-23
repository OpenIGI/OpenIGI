// A storage image a module reads back before writing, in the form each target allows.
//
// Some passes blend this frame's answer onto what a texel already held: the surface cache's probe gather
// and the radiance cache both read a texel and write it again in the same dispatch. SPIR-V does that on
// one read-write image. WGSL cannot: it allows read-write storage on three single-channel formats, and
// these images are rgba16float. So on WGSL the image is written only, and what it held is read from a
// copy the device takes just before the dispatch -- the same values, since one thread reads and writes
// each texel and nothing else touches it.
//
// The copy is declared on BOTH targets, as a texture named after the image with History appended, so the
// two share one binding table. The SPIR-V never reads it; the device supplies it where the form does.
// Nothing else is needed from a module than to declare the pair here and to reach the image through the
// two accessors.

#ifndef OPENIGI_IMAGE_HISTORY_INCLUDED
#define OPENIGI_IMAGE_HISTORY_INCLUDED

#include "ComputeBindings.slang"

// The texture slot a history takes: one of the four at the top of the block, well clear of the units the
// game sets.
#define OPENIGI_HISTORY_SLOT(image) ((image) + 12)

#ifdef OPENIGI_TARGET_WGSL

#define OPENIGI_HISTORY_IMAGE_2D(slot, name) \
    OPENIGI_COMPUTE_IMAGE(slot) [[vk::image_format("rgba16f")]] WTexture2D<float4> name; \
    OPENIGI_COMPUTE_TEXTURE(OPENIGI_HISTORY_SLOT(slot)) Texture2D<float4> name##History

#define OPENIGI_HISTORY_IMAGE_3D(slot, name) \
    OPENIGI_COMPUTE_IMAGE(slot) [[vk::image_format("rgba16f")]] WTexture3D<float4> name; \
    OPENIGI_COMPUTE_TEXTURE(OPENIGI_HISTORY_SLOT(slot)) Texture3D<float4> name##History

#define OPENIGI_HISTORY_LOAD_2D(name, at) name##History.Load(int3((at), 0))
#define OPENIGI_HISTORY_LOAD_3D(name, at) name##History.Load(int4((at), 0))
#define OPENIGI_HISTORY_STORE(name, at, value) name.Store((at), (value))

#else

#define OPENIGI_HISTORY_IMAGE_2D(slot, name) \
    OPENIGI_COMPUTE_IMAGE(slot) [[vk::image_format("rgba16f")]] RWTexture2D<float4> name; \
    OPENIGI_COMPUTE_TEXTURE(OPENIGI_HISTORY_SLOT(slot)) Texture2D<float4> name##History

#define OPENIGI_HISTORY_IMAGE_3D(slot, name) \
    OPENIGI_COMPUTE_IMAGE(slot) [[vk::image_format("rgba16f")]] RWTexture3D<float4> name; \
    OPENIGI_COMPUTE_TEXTURE(OPENIGI_HISTORY_SLOT(slot)) Texture3D<float4> name##History

#define OPENIGI_HISTORY_LOAD_2D(name, at) name[(at)]
#define OPENIGI_HISTORY_LOAD_3D(name, at) name[(at)]
#define OPENIGI_HISTORY_STORE(name, at, value) name[(at)] = (value)

#endif

#endif
