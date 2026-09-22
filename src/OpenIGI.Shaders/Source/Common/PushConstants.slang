// How a module declares the block of constants it is handed per draw.
//
// Vulkan has push constants and WebGPU does not, and Slang cannot paper over the difference: asked
// for a push constant on the WGSL target it emits `var<uniform>` with no @group or @binding at all,
// which is not valid WGSL, and an explicit binding on the declaration does not change that. So the
// declaration itself is chosen per target.
//
// The build defines OPENIGI_NO_PUSH_CONSTANTS for the WGSL target only. There the block becomes an
// ordinary uniform buffer in a group reserved for it, which is what the RHI's SetPushData writes to
// on that backend; on SPIR-V it stays a real push constant and costs nothing.
//
// The two targets do not lay the block out identically -- WGSL gets std140, SPIR-V std430. They
// agree for a block of vectors and matrices, which is every block here today, and stop agreeing the
// moment one holds a bare float3 or an array. The generated reflection records the block's size so
// that a mismatch is caught rather than guessed at.

#ifndef OPENIGI_PUSH_CONSTANTS_INCLUDED
#define OPENIGI_PUSH_CONSTANTS_INCLUDED

/// The bind group the WGSL target places a push block in. Chosen high so it cannot collide with a
/// module's own descriptor sets, which start at zero and are few.
#define OPENIGI_PUSH_CONSTANT_GROUP 3

#ifdef OPENIGI_NO_PUSH_CONSTANTS
#define OPENIGI_PUSH_CONSTANT(type, name) \
    [[vk::binding(0, OPENIGI_PUSH_CONSTANT_GROUP)]] ConstantBuffer<type> name
#else
#define OPENIGI_PUSH_CONSTANT(type, name) \
    [[vk::push_constant]] ConstantBuffer<type> name
#endif

#endif
