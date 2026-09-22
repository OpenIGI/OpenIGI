// Where a compute module's resources sit in its descriptor set.
//
// The engine's compute seam binds a resource by the number the shader declared it at AND by what kind it
// is: BindImage(0, ...) and BindStorageBuffer(0, ...) name two different resources, because the dialect
// these shaders came from gives each kind its own numbering. Vulkan gives a set one numbering for all of
// them, so the two have to be folded together, and the fold has to be reversible -- the backend is handed
// a kind and a slot and must arrive at the binding the module declared.
//
// So each kind gets a block of sixteen. A module declares its resources through the macros below and the
// backend recovers the kind and the slot by division, with no table to keep in step and nothing to record
// in the manifest. Sixteen is far more than any of these shaders uses; the widest binds seven buffers.

#ifndef OPENIGI_COMPUTE_BINDINGS_INCLUDED
#define OPENIGI_COMPUTE_BINDINGS_INCLUDED

// Storage images, at 0 to 15.
#define OPENIGI_COMPUTE_IMAGE(slot) [[vk::binding(slot, 0)]]

// Storage buffers, at 16 to 31.
#define OPENIGI_COMPUTE_BUFFER(slot) [[vk::binding((slot) + 16, 0)]]

// Sampled images, at 32 to 47. A module whose Globals block carries an int of the same name is bound by
// the texture unit that int holds rather than by this slot, which is how the seam's default-block
// samplers work; the slot is then only its place in the set.
#define OPENIGI_COMPUTE_TEXTURE(slot) [[vk::binding((slot) + 32, 0)]]

// The world's acceleration structure, at 48. There is only ever one.
#define OPENIGI_COMPUTE_SCENE [[vk::binding(48, 0)]]

// The gathered parameters, at 49: one block holding every value the host sets by name.
#define OPENIGI_COMPUTE_GLOBALS [[vk::binding(49, 0)]]

#endif
