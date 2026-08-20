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
