namespace OpenIGI.Rendering.Vulkan.Capabilities;

/// <summary>The device extension strings the backend asks for by name.</summary>
/// <remarks>
/// Silk exposes a constant for every extension it wraps with entry points, but several of the ones this
/// backend needs are pure shader or feature extensions — ray query adds no commands at all, it only
/// makes <c>rayQueryEXT</c> legal in SPIR-V — so they have no wrapper class and therefore no constant.
/// Naming them all in one place means the selector, the device and the capability report spell them the
/// same way.
/// </remarks>
internal static class VulkanExtensionNames
{
    /// <summary>Ray query: tracing from an ordinary compute or fragment shader, with no ray-tracing pipeline.</summary>
    /// <remarks>
    /// The path the whole indirect-lighting stack traces on. A ray-tracing pipeline needs its own shader
    /// binding table and a separate dispatch; a ray query is an inline loop inside a compute shader, which
    /// is what lets the gather passes run on the async-compute queue alongside the raster work.
    /// </remarks>
    public const string RayQuery = "VK_KHR_ray_query";

    /// <summary>Descriptor indexing: unbounded descriptor arrays indexed by a value computed in the shader.</summary>
    public const string DescriptorIndexing = "VK_EXT_descriptor_indexing";

    /// <summary>Descriptor buffer: descriptors written into ordinary device memory rather than pool-allocated sets.</summary>
    /// <remarks>
    /// The bindless model the backend is built on. A descriptor pool hands out opaque sets that must be
    /// updated through the API; a descriptor buffer is memory the application writes descriptors into and
    /// binds by address, so a resource becomes a plain integer index the shader can compute.
    /// </remarks>
    public const string DescriptorBuffer = "VK_EXT_descriptor_buffer";

    /// <summary>A descriptor slot whose type is decided per write rather than fixed by the layout.</summary>
    /// <remarks>
    /// What lets one heap hold every kind of resource. Without it the bindless set would need a separate
    /// array per descriptor type — one for sampled images, one for storage buffers, one for acceleration
    /// structures — and an integer handle would have to carry which array it indexed. With it there is a
    /// single array, and a handle is just a number.
    /// </remarks>
    public const string MutableDescriptorType = "VK_EXT_mutable_descriptor_type";

    /// <summary>SPIR-V 1.4, which the ray-tracing shader stages are specified against.</summary>
    public const string SpirV14 = "VK_KHR_spirv_1_4";

    /// <summary>Shader float controls, a dependency of SPIR-V 1.4.</summary>
    public const string ShaderFloatControls = "VK_KHR_shader_float_controls";

    /// <summary>Pipeline libraries: linking ray-tracing shader stages compiled separately.</summary>
    public const string PipelineLibrary = "VK_KHR_pipeline_library";

    /// <summary>Position fetch: reading a hit triangle's vertex positions straight from the acceleration structure.</summary>
    /// <remarks>
    /// Without it a hit shader has to reach back into the mesh's own vertex and index buffers to work out
    /// where it hit, which means every mesh's buffers must be reachable from the hit group's parameters.
    /// With it the driver hands the three positions over directly.
    /// </remarks>
    public const string RayTracingPositionFetch = "VK_KHR_ray_tracing_position_fetch";

    /// <summary>Buffer device address: a buffer as a raw 64-bit pointer a shader can dereference.</summary>
    public const string BufferDeviceAddress = "VK_KHR_buffer_device_address";

    /// <summary>Reports how much of each heap is actually in use, for budgeting large atlases.</summary>
    public const string MemoryBudget = "VK_EXT_memory_budget";

    /// <summary>Lets an allocation declare its importance so the driver evicts the right things first.</summary>
    public const string MemoryPriority = "VK_EXT_memory_priority";

    /// <summary>Resetting timestamp queries from the host rather than through a command buffer.</summary>
    public const string HostQueryReset = "VK_EXT_host_query_reset";

    /// <summary>Robustness 2: out-of-bounds reads across every buffer kind return zero rather than lose the device.</summary>
    public const string Robustness2 = "VK_EXT_robustness2";

    /// <summary>Reading an attachment written earlier in the same dynamic-rendering pass.</summary>
    public const string DynamicRenderingLocalRead = "VK_KHR_dynamic_rendering_local_read";

    /// <summary>Instance-level: makes adapters that implement only a subset of Vulkan visible at all.</summary>
    /// <remarks>
    /// An instance extension rather than a device one, and the only one in this list. The Khronos loader
    /// hides a non-conformant implementation from <c>vkEnumeratePhysicalDevices</c> unless the instance
    /// both enables this and sets <c>VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR</c> — so on macOS,
    /// where Vulkan is MoltenVK translating to Metal, an instance without it reports no adapters at all
    /// rather than reporting one that is merely limited.
    /// </remarks>
    public const string PortabilityEnumeration = "VK_KHR_portability_enumeration";

    /// <summary>Declares which corners of Vulkan a non-conformant adapter does not have.</summary>
    /// <remarks>
    /// The spec <em>requires</em> this to be enabled whenever the adapter advertises it: a device created
    /// without it, on a device that offers it, is invalid usage. It carries no commands — it is a feature
    /// structure listing what the implementation cannot do, each field a thing every conformant adapter
    /// has and this one may not.
    /// </remarks>
    public const string PortabilitySubset = "VK_KHR_portability_subset";
}
