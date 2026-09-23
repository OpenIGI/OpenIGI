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

// A sampler declared apart from its texture, at 64 to 79, sharing that texture's slot. Binding the
// texture binds both, so a module that declares the pair costs the caller nothing extra -- and the pair
// is the only shape WGSL has, so a module that wants a browser form declares one rather than a combined
// type.
#define OPENIGI_COMPUTE_SAMPLER(slot) [[vk::binding((slot) + 64, 0)]]

// A texture read in a compute stage, the way each target allows it.
//
// WGSL forbids an implicit-level sample outside a fragment stage -- it has no derivatives there -- so
// a module sampling that way is refused outright. SPIR-V has them: Slang compiles the sample to quad
// derivatives (VK_KHR_compute_shader_derivatives, which the device enables), and the legacy renderer has
// always sampled so. The answer is NOT the one an explicit level zero gets -- switched over, mission 1's
// lit frame moved by up to three levels across two thousand pixels -- so the SPIR-V form keeps what it
// has always done and WGSL takes the level it can express.

/// A flat texture sampled in a compute stage.
float4 ComputeSample(Texture2D<float4> texture, SamplerState sampler, float2 uv)
{
    __target_switch
    {
    case spirv:
        return texture.Sample(sampler, uv);
    default:
        return texture.SampleLevel(sampler, uv, 0.0);
    }
}

/// A volume sampled in a compute stage.
float4 ComputeSample(Texture3D<float4> texture, SamplerState sampler, float3 uvw)
{
    __target_switch
    {
    case spirv:
        return texture.Sample(sampler, uvw);
    default:
        return texture.SampleLevel(sampler, uvw, 0.0);
    }
}

#endif
