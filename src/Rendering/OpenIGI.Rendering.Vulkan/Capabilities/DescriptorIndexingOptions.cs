namespace OpenIGI.Rendering.Vulkan.Capabilities;

/// <summary>
/// The descriptor-indexing features an adapter may implement without implementing all of them, each
/// answered as the adapter reported it.
/// </summary>
/// <remarks>
/// <para>
/// Descriptor indexing is one extension but thirteen feature bits, and a conforming adapter is allowed
/// to set any subset. The backend needs seven of them together for a bindless heap to work at all --
/// that decision is <see cref="VulkanDeviceCapabilities.DescriptorIndexing"/> -- and these are the six
/// it does not depend on, which are therefore requested one by one, each only where it was found.
/// </para>
/// <para>
/// The split exists because of NVIDIA Pascal. A GTX 1060 reports every core bit but not
/// <c>descriptorBindingUniformBufferUpdateAfterBind</c>, so a create-info that derived all thirteen
/// from the seven asked that adapter for a feature it does not have, and <c>vkCreateDevice</c> answered
/// <c>VK_ERROR_FEATURE_NOT_PRESENT</c> for the whole device. The RTX 3080 the port was written against
/// has all thirteen, which is why the same code ran there.
/// </para>
/// </remarks>
/// <param name="StorageImageArrayNonUniformIndexing">Whether storage image arrays may be indexed non-uniformly.</param>
/// <param name="UniformBufferArrayNonUniformIndexing">Whether uniform buffer arrays may be indexed non-uniformly.</param>
/// <param name="SampledImageUpdateAfterBind">Whether a sampled image descriptor may be rewritten after binding.</param>
/// <param name="StorageImageUpdateAfterBind">Whether a storage image descriptor may be rewritten after binding.</param>
/// <param name="StorageBufferUpdateAfterBind">Whether a storage buffer descriptor may be rewritten after binding.</param>
/// <param name="UniformBufferUpdateAfterBind">Whether a uniform buffer descriptor may be rewritten after binding.</param>
internal readonly record struct DescriptorIndexingOptions(
    bool StorageImageArrayNonUniformIndexing,
    bool UniformBufferArrayNonUniformIndexing,
    bool SampledImageUpdateAfterBind,
    bool StorageImageUpdateAfterBind,
    bool StorageBufferUpdateAfterBind,
    bool UniformBufferUpdateAfterBind)
{
    /// <summary>Every option present, for an adapter or a test that has them all.</summary>
    public static DescriptorIndexingOptions All { get; } = new(true, true, true, true, true, true);
}
