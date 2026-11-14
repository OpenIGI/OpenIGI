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

// The per-instance form, for a module whose pipeline layout says PushDataPerInstance. Without push
// constants the frame's blocks sit packed in one storage array in that same group, and a draw's
// first instance is its block's index: the backend then binds the array once rather than a slice
// per draw, and folds consecutive draws of one range into one instanced draw. Each entry point
// loads its block first, with OPENIGI_LOAD_INSTANCED_PUSH, from the instance index -- the vertex
// stage's own, and the fragment stage's as a flat varying the vertex stage hands down -- and reads
// it by name exactly as it would the ordinary block. With push constants all of it is the ordinary
// block and the load is nothing.
#ifdef OPENIGI_NO_PUSH_CONSTANTS
#define OPENIGI_INSTANCED_PUSH 1
#ifdef OPENIGI_TARGET_GLSL
// OpenGL reads the blocks as words and builds each one in code. A structured buffer of the block becomes an array
// of structs in GLSL, and a matrix in it is row-major only through a qualifier on that array -- which the GLSL spec
// says reaches the matrices inside and NVIDIA's driver ignores, reading every one transposed. Loaded as words there
// is no qualifier for a driver to get wrong. The stride is the block's size rounded to sixteen, as the backend packs.
#define OPENIGI_INSTANCED_PUSH_CONSTANT(type, name) \
    [[vk::binding(0, OPENIGI_PUSH_CONSTANT_GROUP)]] ByteAddressBuffer name##Blocks; \
    typedef type name##Block; \
    static type name
#define OPENIGI_LOAD_INSTANCED_PUSH(name, index) \
    name = name##Blocks.Load<name##Block>((index) * ((sizeof(name##Block) + 15) / 16 * 16))
#else
#define OPENIGI_INSTANCED_PUSH_CONSTANT(type, name) \
    [[vk::binding(0, OPENIGI_PUSH_CONSTANT_GROUP)]] StructuredBuffer<type> name##Blocks; \
    static type name
#define OPENIGI_LOAD_INSTANCED_PUSH(name, index) name = name##Blocks[index]
#endif

// The semantic the instance index is read through, which must count from the draw's first instance: that is
// where the backend put the block. WGSL's instance_index does. Slang's SV_InstanceID is Direct3D's, which does
// not -- on the SPIR-V the OpenGL target is translated from, it is InstanceIndex less BaseInstance, which is
// always nought for a draw of one instance -- so the OpenGL target reads Vulkan's own index instead.
#ifdef OPENIGI_TARGET_GLSL
#define OPENIGI_INSTANCE_INDEX SV_VulkanInstanceID
#else
#define OPENIGI_INSTANCE_INDEX SV_InstanceID
#endif
#else
#define OPENIGI_INSTANCED_PUSH 0
#define OPENIGI_INSTANCED_PUSH_CONSTANT(type, name) OPENIGI_PUSH_CONSTANT(type, name)
#define OPENIGI_LOAD_INSTANCED_PUSH(name, index)
#endif

#endif
