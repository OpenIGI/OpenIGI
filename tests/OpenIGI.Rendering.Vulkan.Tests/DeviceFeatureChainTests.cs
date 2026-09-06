using OpenIGI.Rendering.Vulkan.Capabilities;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Checks that the <c>pNext</c> chain handed to <c>vkCreateDevice</c> never names an extension the
/// device is not being created with.
/// </summary>
/// <remarks>
/// <para>
/// Linking a feature structure whose extension is absent from <c>ppEnabledExtensionNames</c> is
/// undefined. A driver that does not recognise the structure type walks into it regardless, and the
/// failure is an access violation raised inside <c>vkCreateDevice</c> — not a Vulkan error code the
/// backend could report — so the game dies before it can say anything about why.
/// </para>
/// <para>
/// It is a hardware-shaped bug, which is what makes a test worth having: the chain and the extension
/// list are built from different booleans, so they agree on whatever adapter the author happened to be
/// sitting at and part company on the next one along. These cases are written as the adapters that
/// actually expose them, and need no adapter to run.
/// </para>
/// </remarks>
public sealed unsafe class DeviceFeatureChainTests
{
    /// <summary>Which extension defines each structure the chain may carry.</summary>
    private static readonly (StructureType Type, string Extension)[] ExtensionStructures =
    [
        (StructureType.PhysicalDeviceRobustness2FeaturesExt, VulkanExtensionNames.Robustness2),
        (StructureType.PhysicalDeviceFaultFeaturesExt, "VK_EXT_device_fault"),
        (StructureType.PhysicalDeviceDynamicRenderingLocalReadFeaturesKhr, VulkanExtensionNames.DynamicRenderingLocalRead),
        (StructureType.PhysicalDeviceMutableDescriptorTypeFeaturesExt, VulkanExtensionNames.MutableDescriptorType),
        (StructureType.PhysicalDeviceDescriptorBufferFeaturesExt, VulkanExtensionNames.DescriptorBuffer),
        (StructureType.PhysicalDeviceRayTracingPositionFetchFeaturesKhr, VulkanExtensionNames.RayTracingPositionFetch),
        (StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr, KhrRayTracingPipeline.ExtensionName),
        (StructureType.PhysicalDeviceRayQueryFeaturesKhr, VulkanExtensionNames.RayQuery),
        (StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr, KhrAccelerationStructure.ExtensionName),
        (StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr, VulkanExtensionNames.PortabilitySubset),
    ];

    /// <summary>
    /// A Pascal adapter: it offers mutable descriptor types but no descriptor buffer, and the extension
    /// list only ever takes the first alongside the second. This is the GTX 1080 Ti crash.
    /// </summary>
    [Fact]
    public void MutableDescriptorTypeWithoutDescriptorBuffer_IsNotLinked()
    {
        VulkanDeviceCapabilities capabilities = Capabilities(
            enabled: [KhrSwapchain.ExtensionName],
            mutableDescriptorType: true,
            descriptorBuffer: false);

        Assert.DoesNotContain(
            ChainedTypes(capabilities),
            type => type == StructureType.PhysicalDeviceMutableDescriptorTypeFeaturesExt);
    }

    /// <summary>The same adapter with the extension actually enabled still gets the structure.</summary>
    [Fact]
    public void MutableDescriptorTypeWithTheExtensionEnabled_IsLinked()
    {
        VulkanDeviceCapabilities capabilities = Capabilities(
            enabled: [KhrSwapchain.ExtensionName, VulkanExtensionNames.MutableDescriptorType],
            mutableDescriptorType: true,
            descriptorBuffer: false);

        Assert.Contains(
            ChainedTypes(capabilities),
            type => type == StructureType.PhysicalDeviceMutableDescriptorTypeFeaturesExt);
    }

    /// <summary>
    /// The general invariant, over an adapter that claims every capability while enabling no extension
    /// at all — the worst case the two lists can disagree by.
    /// </summary>
    [Fact]
    public void EveryCapabilityClaimed_LinksNothingWhoseExtensionIsAbsent()
    {
        VulkanDeviceCapabilities capabilities = Capabilities(
            enabled: [KhrSwapchain.ExtensionName],
            mutableDescriptorType: true,
            descriptorBuffer: true,
            everything: true);

        List<StructureType> chained = ChainedTypes(capabilities);

        foreach ((StructureType type, string extension) in ExtensionStructures)
        {
            Assert.False(
                chained.Contains(type),
                $"{type} was linked, but {extension} is not among the enabled extensions.");
        }
    }

    /// <summary>And with every extension enabled, the same capabilities do link their structures.</summary>
    [Fact]
    public void EveryExtensionEnabled_LinksEveryStructure()
    {
        VulkanDeviceCapabilities capabilities = Capabilities(
            enabled: [KhrSwapchain.ExtensionName, .. ExtensionStructures.Select(entry => entry.Extension)],
            mutableDescriptorType: true,
            descriptorBuffer: true,
            everything: true);

        List<StructureType> chained = ChainedTypes(capabilities);

        foreach ((StructureType type, string extension) in ExtensionStructures)
        {
            Assert.True(chained.Contains(type), $"{type} was not linked though {extension} is enabled.");
        }
    }

    /// <summary>Builds the chain for these capabilities and reads back every structure type in it.</summary>
    private static List<StructureType> ChainedTypes(VulkanDeviceCapabilities capabilities)
    {
        using VulkanStructChain chain = new();
        VulkanCapabilityProbe.BuildEnabledFeatures(capabilities, chain, Vk.Version13);

        // Every Vulkan extension structure opens with a 32-bit type and then, a pointer-slot along, its
        // own pNext — which is what lets the chain be walked without knowing what is in it.
        List<StructureType> types = [];
        for (void* link = chain.Head; link is not null; link = ((void**)link)[1])
        {
            types.Add(*(StructureType*)link);
        }

        return types;
    }

    private static VulkanDeviceCapabilities Capabilities(
        string[] enabled,
        bool mutableDescriptorType,
        bool descriptorBuffer,
        bool everything = false) =>
        new()
        {
            EnabledExtensions = enabled,
            MutableDescriptorType = mutableDescriptorType,
            DescriptorBuffer = descriptorBuffer,
            Robustness2 = everything,
            DynamicRenderingLocalRead = everything,
            RayTracingPositionFetch = everything,
            RayTracingPipeline = everything,
            RayQuery = everything,
            AccelerationStructure = everything,
            PortabilitySubset = everything,
        };
}
