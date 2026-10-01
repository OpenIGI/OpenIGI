using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Capabilities;

/// <summary>
/// What the chosen adapter can actually do: every optional feature the backend asked for, answered, and
/// the limits that go with the ones it got.
/// </summary>
/// <remarks>
/// <para>
/// An extension being present is not the same as a feature being usable. Vulkan splits the two: the
/// extension string says the driver carries the code, and a feature bit read through
/// <c>vkGetPhysicalDeviceFeatures2</c> says this adapter actually implements it. Enabling an extension
/// whose feature bit is false is legal and then fails at the first call, so every capability here is
/// decided by the pair — string present <em>and</em> bit set — which is the only check that means
/// anything.
/// </para>
/// <para>
/// The limits travel with the flags because they are needed far from where they were read: a shader
/// binding table's record stride is computed from the ray-tracing handle size and alignment, and a
/// descriptor heap's stride from the descriptor buffer's own alignment. Reading them once at selection
/// and carrying them means those decisions never re-query the physical device.
/// </para>
/// </remarks>
internal sealed class VulkanDeviceCapabilities
{
    /// <summary>Gets the device extensions the logical device should enable.</summary>
    public required IReadOnlyList<string> EnabledExtensions { get; init; }

    /// <summary>Gets a value indicating whether synchronisation-2 barriers are available.</summary>
    /// <remarks>
    /// The barrier form the whole backend records: one call carrying per-resource stage and access masks,
    /// rather than the original's single pair of pipeline stage masks for the whole barrier. Core in
    /// Vulkan 1.3, still gated on the feature bit.
    /// </remarks>
    public bool Synchronization2 { get; init; }

    /// <summary>Gets a value indicating whether timeline semaphores are available.</summary>
    /// <remarks>
    /// A monotonically increasing counter a queue signals and anyone can wait on any past value of. It is
    /// what replaces the fence-per-frame ring: completion becomes "has the queue passed value N", which
    /// the CPU can ask without a fence object per submission and which one queue can wait on from another.
    /// </remarks>
    public bool TimelineSemaphore { get; init; }

    /// <summary>Gets a value indicating whether a buffer can be given a device address a shader dereferences.</summary>
    public bool BufferDeviceAddress { get; init; }

    /// <summary>Gets a value indicating whether descriptor arrays can be indexed by a shader-computed value.</summary>
    public bool DescriptorIndexing { get; init; }

    /// <summary>Gets the descriptor-indexing features that are optional on top of <see cref="DescriptorIndexing"/>.</summary>
    /// <remarks>
    /// Each is requested only where the adapter reported it, because an adapter may have the seven the
    /// backend needs and lack one of these -- see <see cref="Capabilities.DescriptorIndexingOptions"/>.
    /// </remarks>
    public DescriptorIndexingOptions DescriptorIndexingOptions { get; init; }

    /// <summary>Gets a value indicating whether an acceleration structure descriptor can be updated after binding.</summary>
    /// <remarks>
    /// Required for a bindless model that holds the scene's acceleration structure: the top-level
    /// structure is rebuilt every frame, so its descriptor is rewritten while a command buffer that
    /// references the heap is already recorded.
    /// </remarks>
    public bool AccelerationStructureUpdateAfterBind { get; init; }

    /// <summary>Gets a value indicating whether storage buffers may use C-style scalar layout.</summary>
    public bool ScalarBlockLayout { get; init; }

    /// <summary>Gets a value indicating whether rendering can begin without a render pass object.</summary>
    public bool DynamicRendering { get; init; }

    /// <summary>Gets a value indicating whether an attachment can be read back within a dynamic-rendering pass.</summary>
    public bool DynamicRenderingLocalRead { get; init; }

    /// <summary>Gets a value indicating whether query pools can be reset from the host.</summary>
    public bool HostQueryReset { get; init; }

    /// <summary>Gets a value indicating whether descriptors can be written into plain device memory.</summary>
    public bool DescriptorBuffer { get; init; }

    /// <summary>Gets a value indicating whether one descriptor slot can hold any resource type.</summary>
    /// <remarks>
    /// What collapses the bindless heap to a single array. Without it each resource kind would need its
    /// own array and an integer handle would have to say which one it indexed; with it a handle is simply
    /// an index.
    /// </remarks>
    public bool MutableDescriptorType { get; init; }

    /// <summary>Gets a value indicating whether acceleration structures can be built and traversed.</summary>
    public bool AccelerationStructure { get; init; }

    /// <summary>Gets a value indicating whether shaders may trace rays inline through a ray query.</summary>
    public bool RayQuery { get; init; }

    /// <summary>Gets a value indicating whether a dedicated ray-tracing pipeline can be created.</summary>
    public bool RayTracingPipeline { get; init; }

    /// <summary>Gets a value indicating whether <c>vkCmdTraceRaysIndirectKHR</c> is available.</summary>
    public bool RayTracingIndirect { get; init; }

    /// <summary>Gets a value indicating whether traversal can cull primitives by facing.</summary>
    public bool RayTraversalPrimitiveCulling { get; init; }

    /// <summary>Gets a value indicating whether a hit can report its triangle's positions directly.</summary>
    public bool RayTracingPositionFetch { get; init; }

    /// <summary>Gets a value indicating whether 16-bit values may be stored in buffers.</summary>
    public bool Storage16Bit { get; init; }

    /// <summary>Gets a value indicating whether a shader may read the draw's own parameters.</summary>
    /// <remarks>
    /// Slang declares the SPIR-V capability for it in any stage that reads <c>SV_VertexID</c>, so every
    /// full-screen pass needs it enabled; a module that declares a capability the device did not enable is
    /// invalid, however the driver treats it.
    /// </remarks>
    public bool ShaderDrawParameters { get; init; }

    /// <summary>Gets a value indicating whether a compute stage may take derivatives across quads.</summary>
    /// <remarks>See <see cref="VulkanExtensionNames.ComputeShaderDerivatives"/>.</remarks>
    public bool ComputeDerivativeGroupQuads { get; init; }

    /// <summary>Gets a value indicating whether shaders may compute in half precision.</summary>
    public bool ShaderFloat16 { get; init; }

    /// <summary>Gets a value indicating whether shaders may perform 64-bit integer atomics.</summary>
    public bool ShaderInt64Atomics { get; init; }

    /// <summary>Gets a value indicating whether anisotropic sampling is available.</summary>
    public bool SamplerAnisotropy { get; init; }

    /// <summary>Gets a value indicating whether out-of-bounds buffer reads are defined to return zero.</summary>
    public bool RobustBufferAccess { get; init; }

    /// <summary>Gets a value indicating whether the extended robustness guarantees are available.</summary>
    public bool Robustness2 { get; init; }

    /// <summary>Gets a value indicating whether shaders may declare 64-bit integers.</summary>
    public bool ShaderInt64 { get; init; }

    /// <summary>Gets a value indicating whether a fragment shader may write buffers and storage images.</summary>
    public bool FragmentStoresAndAtomics { get; init; }

    /// <summary>Gets a value indicating whether a vertex shader may write buffers and storage images.</summary>
    public bool VertexPipelineStoresAndAtomics { get; init; }

    /// <summary>Gets a value indicating whether a storage image may be written without a format qualifier.</summary>
    public bool StorageImageWriteWithoutFormat { get; init; }

    /// <summary>Gets a value indicating whether colour attachments may each carry their own blend state.</summary>
    public bool IndependentBlend { get; init; }

    /// <summary>
    /// Gets a value indicating whether arrays of sampled images, storage images, and uniform and storage
    /// buffers may be indexed by a value that varies at runtime but is uniform across the draw.
    /// </summary>
    /// <remarks>
    /// Reported as one fact because the four Vulkan features behind it are asked for together and no pass
    /// wants a subset: a shading pass that indexes its textures also indexes the buffer that describes
    /// them. The stronger, per-invocation form is <see cref="DescriptorIndexing"/>.
    /// </remarks>
    public bool ArrayDynamicIndexing { get; init; }

    /// <summary>Gets a value indicating whether this adapter implements only a subset of Vulkan.</summary>
    /// <remarks>
    /// True on macOS, where Vulkan is MoltenVK translating to Metal, and false on every conformant
    /// desktop driver. It is not a quality judgement on its own — what the adapter actually lacks is in
    /// <see cref="PortabilityFeatures"/> — but it does mean a capability cannot be assumed from the API
    /// version alone.
    /// </remarks>
    public bool PortabilitySubset { get; init; }

    /// <summary>Gets what a portable adapter answered about itself, valid when <see cref="PortabilitySubset"/> is set.</summary>
    public PhysicalDevicePortabilitySubsetFeaturesKHR PortabilityFeatures { get; init; }

    /// <summary>Gets a value indicating whether a sampler may bias which mip it selects.</summary>
    /// <remarks>
    /// Conformant Vulkan always allows it; a portable adapter need not, because Metal's sampler has no
    /// LOD bias of its own. The texture path reads this and falls back to no bias rather than creating a
    /// sampler the adapter would reject, which costs sharpness at a reduced render scale but is the only
    /// alternative to not creating the texture at all.
    /// </remarks>
    public bool SamplerMipLodBias { get; init; }

    /// <summary>Gets the acceleration-structure limits, valid when <see cref="AccelerationStructure"/> is set.</summary>
    public PhysicalDeviceAccelerationStructurePropertiesKHR AccelerationStructureLimits { get; init; }

    /// <summary>Gets the ray-tracing-pipeline limits, valid when <see cref="RayTracingPipeline"/> is set.</summary>
    public PhysicalDeviceRayTracingPipelinePropertiesKHR RayTracingLimits { get; init; }

    /// <summary>Gets the descriptor-buffer limits, valid when <see cref="DescriptorBuffer"/> is set.</summary>
    public PhysicalDeviceDescriptorBufferPropertiesEXT DescriptorBufferLimits { get; init; }

    /// <summary>Gets the adapter's core limits.</summary>
    public PhysicalDeviceLimits Limits { get; init; }

    /// <summary>
    /// Gets a value indicating whether rays can be traced against a hardware acceleration structure from
    /// an ordinary compute shader — the path every indirect-lighting pass takes.
    /// </summary>
    /// <remarks>
    /// Three things together, because a ray query is useless without a structure to trace and the
    /// structure is unreachable without a device address to build it from. A ray-tracing <em>pipeline</em>
    /// is deliberately not part of this: it is needed only where a hit has to re-shade a full material,
    /// and everything else traces inline.
    /// </remarks>
    public bool HardwareRayTracing => AccelerationStructure && RayQuery && BufferDeviceAddress;
}
