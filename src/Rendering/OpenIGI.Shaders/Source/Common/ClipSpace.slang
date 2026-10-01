// Which way the target's clip space runs vertically, and the one line every vertex stage needs
// because of it.
//
// The engine's projections are OpenGL ones: clip space with Y UP. Vulkan's runs the other way, so a
// mesh stage drawing into a Vulkan target reached through a positive-height viewport has to flip Y on
// its way out -- and that flip is WRONG for a target whose clip space runs the way OpenGL's does,
// which is what WebGPU offers and what the RHI therefore states.
//
// Only one of the two backends can be bent by a viewport: Vulkan takes a negative height and behaves
// like WebGPU, and WebGPU has no such thing. So the RHI states WebGPU's convention, its Vulkan
// implementation flips the viewport, and a stage drawing through it must NOT flip again. This switch
// is what says which.
//
// It is a specialisation constant rather than push data because there is no push room left -- both
// mesh blocks are exactly the 128 bytes the hardware guarantees -- and because it is genuinely
// constant per pipeline. Vulkan takes it as specialisation data and WebGPU as a pipeline-overridable
// constant; either way the branch is gone by the time anything runs.
//
// It DEFAULTS to flipping, the positive-height viewport's convention; a pipeline built for the RHI
// supplies nought (Mesh3DConstants.TargetConstants).
//
// The winding is unaffected either way, which is why nothing about culling changes with it: Y is
// inverted once, in the shader or in the viewport, and the rasteriser sees one inversion in both.

#ifndef OPENIGI_CLIP_SPACE_INCLUDED
#define OPENIGI_CLIP_SPACE_INCLUDED

/// Whether the target's clip space runs with Y downwards, as Vulkan's does. Non-zero to flip.
[vk::constant_id(64)] const int cClipYDown = 1;

/// The clip-space Y a stage should emit, given the one an OpenGL projection produced.
float OpenIgiClipY(float y)
{
    return cClipYDown != 0 ? -y : y;
}

#endif
