using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan.Capabilities;

/// <summary>
/// Asks an adapter what it can do, and later asks the logical device to turn exactly that on.
/// </summary>
/// <remarks>
/// <para>
/// The two halves belong together. Whatever the probe reports as available is what device creation must
/// enable, field for field: enabling a feature the adapter does not have fails device creation outright,
/// and leaving one off that was reported available means a shader compiled against it faults at the
/// first dispatch. Keeping the query and the enable in one type is what stops the two lists drifting.
/// </para>
/// <para>
/// Both halves build the same shape of <c>pNext</c> chain, conditionally: a structure is linked only
/// when the extension that defines it is present, because linking a structure type the driver does not
/// know is undefined rather than politely ignored.
/// </para>
/// </remarks>
internal static unsafe class VulkanCapabilityProbe
{
    /// <summary>Reads everything the backend cares about from a physical device.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The adapter to inspect.</param>
    /// <param name="available">The extension names the adapter reports.</param>
    /// <param name="apiVersion">The adapter's supported API version.</param>
    /// <returns>The capabilities, and the extension list the logical device should enable.</returns>
    public static VulkanDeviceCapabilities Probe(
        Vk vk,
        PhysicalDevice device,
        HashSet<string> available,
        uint apiVersion)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(available);

        bool vulkan13 = apiVersion >= Vk.Version13;

        using VulkanStructChain chain = new();

        PhysicalDeviceVulkan11Features* v11 = chain.Add(new PhysicalDeviceVulkan11Features
        {
            SType = StructureType.PhysicalDeviceVulkan11Features,
        });
        PhysicalDeviceVulkan12Features* v12 = chain.Add(new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
        });

        PhysicalDeviceVulkan13Features* v13 = null;
        if (vulkan13)
        {
            v13 = chain.Add(new PhysicalDeviceVulkan13Features
            {
                SType = StructureType.PhysicalDeviceVulkan13Features,
            });
        }

        PhysicalDeviceAccelerationStructureFeaturesKHR* asFeatures = null;
        if (available.Contains(KhrAccelerationStructure.ExtensionName))
        {
            asFeatures = chain.Add(new PhysicalDeviceAccelerationStructureFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
            });
        }

        PhysicalDeviceRayQueryFeaturesKHR* rayQuery = null;
        if (available.Contains(VulkanExtensionNames.RayQuery))
        {
            rayQuery = chain.Add(new PhysicalDeviceRayQueryFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr,
            });
        }

        PhysicalDeviceRayTracingPipelineFeaturesKHR* rtPipeline = null;
        if (available.Contains(KhrRayTracingPipeline.ExtensionName))
        {
            rtPipeline = chain.Add(new PhysicalDeviceRayTracingPipelineFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
            });
        }

        PhysicalDeviceRayTracingPositionFetchFeaturesKHR* positionFetch = null;
        if (available.Contains(VulkanExtensionNames.RayTracingPositionFetch))
        {
            positionFetch = chain.Add(new PhysicalDeviceRayTracingPositionFetchFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPositionFetchFeaturesKhr,
            });
        }

        PhysicalDeviceDescriptorBufferFeaturesEXT* descriptorBuffer = null;
        if (available.Contains(VulkanExtensionNames.DescriptorBuffer))
        {
            descriptorBuffer = chain.Add(new PhysicalDeviceDescriptorBufferFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceDescriptorBufferFeaturesExt,
            });
        }

        PhysicalDeviceMutableDescriptorTypeFeaturesEXT* mutableType = null;
        if (available.Contains(VulkanExtensionNames.MutableDescriptorType))
        {
            mutableType = chain.Add(new PhysicalDeviceMutableDescriptorTypeFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceMutableDescriptorTypeFeaturesExt,
            });
        }

        PhysicalDeviceDynamicRenderingLocalReadFeaturesKHR* localRead = null;
        if (available.Contains(VulkanExtensionNames.DynamicRenderingLocalRead))
        {
            localRead = chain.Add(new PhysicalDeviceDynamicRenderingLocalReadFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceDynamicRenderingLocalReadFeaturesKhr,
            });
        }

        PhysicalDeviceRobustness2FeaturesEXT* robustness2 = null;
        if (available.Contains(VulkanExtensionNames.Robustness2))
        {
            robustness2 = chain.Add(new PhysicalDeviceRobustness2FeaturesEXT
            {
                SType = StructureType.PhysicalDeviceRobustness2FeaturesExt,
            });
        }

        PhysicalDeviceFeatures2 features = new()
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = chain.Head,
        };
        vk.GetPhysicalDeviceFeatures2(device, &features);

        // Descriptor indexing, buffer device address and timeline semaphores were folded into Vulkan 1.2,
        // so a 1.2-or-later adapter answers for them through the aggregate structure whether or not it
        // still advertises the original extension strings. Asking the aggregate is the reliable question.
        bool descriptorIndexing = v12->DescriptorIndexing
            && v12->RuntimeDescriptorArray
            && v12->DescriptorBindingPartiallyBound
            && v12->DescriptorBindingUpdateUnusedWhilePending
            && v12->DescriptorBindingVariableDescriptorCount
            && v12->ShaderSampledImageArrayNonUniformIndexing
            && v12->ShaderStorageBufferArrayNonUniformIndexing;

        bool bufferDeviceAddress = v12->BufferDeviceAddress;
        bool timelineSemaphore = v12->TimelineSemaphore;
        bool synchronization2 = vulkan13 && v13->Synchronization2;
        bool dynamicRendering = vulkan13 && v13->DynamicRendering;

        bool accelerationStructure = asFeatures is not null
            && asFeatures->AccelerationStructure
            && bufferDeviceAddress
            && available.Contains(KhrDeferredHostOperations.ExtensionName);

        bool rayQueryUsable = rayQuery is not null && rayQuery->RayQuery && accelerationStructure;
        bool rayTracingPipeline = rtPipeline is not null && rtPipeline->RayTracingPipeline && accelerationStructure;
        bool positionFetchUsable =
            positionFetch is not null && positionFetch->RayTracingPositionFetch && accelerationStructure;
        bool localReadUsable = localRead is not null && localRead->DynamicRenderingLocalRead && dynamicRendering;
        bool robustness2Usable = robustness2 is not null && robustness2->RobustBufferAccess2;
        // A single heap holding every resource kind needs a descriptor slot whose type is decided per
        // write, and the per-draw uniform buffers are pushed rather than allocated, which needs the
        // maintenance-5 push-descriptor path. Without either, the bindless model does not hold together,
        // so the whole descriptor-buffer path is declined rather than half-enabled.
        bool mutableDescriptorType = mutableType is not null && mutableType->MutableDescriptorType;
        bool descriptorBufferUsable = descriptorBuffer is not null
            && descriptorBuffer->DescriptorBuffer
            && mutableDescriptorType
            && available.Contains(KhrMaintenance5.ExtensionName)
            && descriptorIndexing
            && bufferDeviceAddress;

        // The limits come from a second query, chained the same way. They are meaningful only for the
        // features that turned out to be present, so each structure is linked on the same condition.
        using VulkanStructChain propertyChain = new();

        PhysicalDeviceAccelerationStructurePropertiesKHR* asLimits = null;
        if (accelerationStructure)
        {
            asLimits = propertyChain.Add(new PhysicalDeviceAccelerationStructurePropertiesKHR
            {
                SType = StructureType.PhysicalDeviceAccelerationStructurePropertiesKhr,
            });
        }

        PhysicalDeviceRayTracingPipelinePropertiesKHR* rtLimits = null;
        if (rayTracingPipeline)
        {
            rtLimits = propertyChain.Add(new PhysicalDeviceRayTracingPipelinePropertiesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelinePropertiesKhr,
            });
        }

        PhysicalDeviceDescriptorBufferPropertiesEXT* dbLimits = null;
        if (descriptorBufferUsable)
        {
            dbLimits = propertyChain.Add(new PhysicalDeviceDescriptorBufferPropertiesEXT
            {
                SType = StructureType.PhysicalDeviceDescriptorBufferPropertiesExt,
            });
        }

        PhysicalDeviceProperties2 properties = new()
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = propertyChain.Head,
        };
        vk.GetPhysicalDeviceProperties2(device, &properties);

        // Only answerable once the limits are in hand: the per-draw uniform buffers are pushed straight
        // into the command buffer, and an adapter that insists a push descriptor still come out of a
        // bound buffer cannot serve them that way.
        if (descriptorBufferUsable && !dbLimits->BufferlessPushDescriptors)
        {
            descriptorBufferUsable = false;
            dbLimits = null;
        }

        return new VulkanDeviceCapabilities
        {
            EnabledExtensions = BuildExtensionList(
                available,
                accelerationStructure,
                rayTracingPipeline,
                rayQueryUsable,
                descriptorBufferUsable,
                positionFetchUsable,
                localReadUsable,
                robustness2Usable),
            Synchronization2 = synchronization2,
            TimelineSemaphore = timelineSemaphore,
            BufferDeviceAddress = bufferDeviceAddress,
            DescriptorIndexing = descriptorIndexing,
            AccelerationStructureUpdateAfterBind =
                asFeatures is not null && asFeatures->DescriptorBindingAccelerationStructureUpdateAfterBind,
            ScalarBlockLayout = v12->ScalarBlockLayout,
            DynamicRendering = dynamicRendering,
            DynamicRenderingLocalRead = localReadUsable,
            HostQueryReset = v12->HostQueryReset,
            DescriptorBuffer = descriptorBufferUsable,
            MutableDescriptorType = mutableDescriptorType,
            AccelerationStructure = accelerationStructure,
            RayQuery = rayQueryUsable,
            RayTracingPipeline = rayTracingPipeline,
            RayTracingIndirect = rtPipeline is not null && rtPipeline->RayTracingPipelineTraceRaysIndirect,
            RayTraversalPrimitiveCulling = rtPipeline is not null && rtPipeline->RayTraversalPrimitiveCulling,
            RayTracingPositionFetch = positionFetchUsable,
            Storage16Bit = v11->StorageBuffer16BitAccess && v11->UniformAndStorageBuffer16BitAccess,
            ShaderFloat16 = v12->ShaderFloat16,
            ShaderInt64Atomics = v12->ShaderBufferInt64Atomics,
            SamplerAnisotropy = features.Features.SamplerAnisotropy,
            RobustBufferAccess = features.Features.RobustBufferAccess,
            Robustness2 = robustness2Usable,
            AccelerationStructureLimits = asLimits is not null ? *asLimits : default,
            RayTracingLimits = rtLimits is not null ? *rtLimits : default,
            DescriptorBufferLimits = dbLimits is not null ? *dbLimits : default,
            Limits = properties.Properties.Limits,
        };
    }

    /// <summary>
    /// Rebuilds the chain of feature structures for device creation, each field set to what the probe
    /// found rather than to what was hoped for.
    /// </summary>
    /// <param name="capabilities">What the probe reported.</param>
    /// <param name="chain">The chain to link the structures into; must outlive the create call.</param>
    /// <param name="apiVersion">The adapter's supported API version.</param>
    /// <returns>The core feature structure to point <c>pEnabledFeatures</c> at.</returns>
    public static PhysicalDeviceFeatures BuildEnabledFeatures(
        VulkanDeviceCapabilities capabilities,
        VulkanStructChain chain,
        uint apiVersion)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(chain);

        if (capabilities.Robustness2)
        {
            chain.Add(new PhysicalDeviceRobustness2FeaturesEXT
            {
                SType = StructureType.PhysicalDeviceRobustness2FeaturesExt,
                RobustBufferAccess2 = true,
                RobustImageAccess2 = true,

                // A null descriptor reads as zero rather than being undefined, which is what lets a
                // bindless heap slot stay empty while a shader that never takes that branch still names
                // it.
                NullDescriptor = true,
            });
        }

        if (capabilities.EnabledExtensions.Contains("VK_EXT_device_fault"))
        {
            chain.Add(new PhysicalDeviceFaultFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceFaultFeaturesExt,
                DeviceFault = true,
            });
        }

        if (capabilities.DynamicRenderingLocalRead)
        {
            chain.Add(new PhysicalDeviceDynamicRenderingLocalReadFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceDynamicRenderingLocalReadFeaturesKhr,
                DynamicRenderingLocalRead = true,
            });
        }

        if (capabilities.MutableDescriptorType)
        {
            chain.Add(new PhysicalDeviceMutableDescriptorTypeFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceMutableDescriptorTypeFeaturesExt,
                MutableDescriptorType = true,
            });
        }

        if (capabilities.DescriptorBuffer)
        {
            chain.Add(new PhysicalDeviceDescriptorBufferFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceDescriptorBufferFeaturesExt,
                DescriptorBuffer = true,
            });
        }

        if (capabilities.RayTracingPositionFetch)
        {
            chain.Add(new PhysicalDeviceRayTracingPositionFetchFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPositionFetchFeaturesKhr,
                RayTracingPositionFetch = true,
            });
        }

        if (capabilities.RayTracingPipeline)
        {
            chain.Add(new PhysicalDeviceRayTracingPipelineFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
                RayTracingPipeline = true,
                RayTracingPipelineTraceRaysIndirect = capabilities.RayTracingIndirect,
                RayTraversalPrimitiveCulling = capabilities.RayTraversalPrimitiveCulling,
            });
        }

        if (capabilities.RayQuery)
        {
            chain.Add(new PhysicalDeviceRayQueryFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr,
                RayQuery = true,
            });
        }

        if (capabilities.AccelerationStructure)
        {
            chain.Add(new PhysicalDeviceAccelerationStructureFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
                AccelerationStructure = true,
                DescriptorBindingAccelerationStructureUpdateAfterBind =
                    capabilities.AccelerationStructureUpdateAfterBind,
            });
        }

        if (apiVersion >= Vk.Version13)
        {
            chain.Add(new PhysicalDeviceVulkan13Features
            {
                SType = StructureType.PhysicalDeviceVulkan13Features,
                Synchronization2 = capabilities.Synchronization2,
                DynamicRendering = capabilities.DynamicRendering,
                Maintenance4 = true,
            });
        }

        chain.Add(new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            BufferDeviceAddress = capabilities.BufferDeviceAddress,
            TimelineSemaphore = capabilities.TimelineSemaphore,
            ScalarBlockLayout = capabilities.ScalarBlockLayout,
            HostQueryReset = capabilities.HostQueryReset,
            ShaderFloat16 = capabilities.ShaderFloat16,
            ShaderBufferInt64Atomics = capabilities.ShaderInt64Atomics,
            DescriptorIndexing = capabilities.DescriptorIndexing,
            RuntimeDescriptorArray = capabilities.DescriptorIndexing,
            DescriptorBindingPartiallyBound = capabilities.DescriptorIndexing,
            DescriptorBindingUpdateUnusedWhilePending = capabilities.DescriptorIndexing,
            DescriptorBindingVariableDescriptorCount = capabilities.DescriptorIndexing,
            ShaderSampledImageArrayNonUniformIndexing = capabilities.DescriptorIndexing,
            ShaderStorageBufferArrayNonUniformIndexing = capabilities.DescriptorIndexing,
            ShaderStorageImageArrayNonUniformIndexing = capabilities.DescriptorIndexing,
            ShaderUniformBufferArrayNonUniformIndexing = capabilities.DescriptorIndexing,
            DescriptorBindingSampledImageUpdateAfterBind = capabilities.DescriptorIndexing,
            DescriptorBindingStorageImageUpdateAfterBind = capabilities.DescriptorIndexing,
            DescriptorBindingStorageBufferUpdateAfterBind = capabilities.DescriptorIndexing,
            DescriptorBindingUniformBufferUpdateAfterBind = capabilities.DescriptorIndexing,
        });

        if (capabilities.Storage16Bit)
        {
            chain.Add(new PhysicalDeviceVulkan11Features
            {
                SType = StructureType.PhysicalDeviceVulkan11Features,
                StorageBuffer16BitAccess = true,
                UniformAndStorageBuffer16BitAccess = true,
            });
        }

        return new PhysicalDeviceFeatures
        {
            SamplerAnisotropy = capabilities.SamplerAnisotropy,
            RobustBufferAccess = capabilities.RobustBufferAccess,

            // The gather passes write storage images from compute and read counts the GPU itself wrote,
            // and every shading pass indexes an array of textures. These are core features an adapter of
            // this class always carries, but they are still asked for by name rather than assumed.
            ShaderInt64 = true,
            FragmentStoresAndAtomics = true,
            VertexPipelineStoresAndAtomics = true,
            ShaderStorageImageWriteWithoutFormat = true,
            IndependentBlend = true,
            ShaderSampledImageArrayDynamicIndexing = true,
            ShaderStorageBufferArrayDynamicIndexing = true,
            ShaderStorageImageArrayDynamicIndexing = true,
            ShaderUniformBufferArrayDynamicIndexing = true,
        };
    }

    private static List<string> BuildExtensionList(
        HashSet<string> available,
        bool accelerationStructure,
        bool rayTracingPipeline,
        bool rayQuery,
        bool descriptorBuffer,
        bool positionFetch,
        bool localRead,
        bool robustness2)
    {
        List<string> enabled = [KhrSwapchain.ExtensionName];

        void Take(string name)
        {
            if (available.Contains(name) && !enabled.Contains(name))
            {
                enabled.Add(name);
            }
        }

        if (robustness2)
        {
            Take(VulkanExtensionNames.Robustness2);
        }

        Take("VK_EXT_device_fault");
        Take(VulkanExtensionNames.MemoryBudget);
        Take(VulkanExtensionNames.MemoryPriority);
        Take(KhrMaintenance4.ExtensionName);
        Take(KhrMaintenance5.ExtensionName);
        Take(KhrMaintenance6.ExtensionName);

        if (localRead)
        {
            Take(VulkanExtensionNames.DynamicRenderingLocalRead);
        }

        if (descriptorBuffer)
        {
            Take(VulkanExtensionNames.DescriptorBuffer);
            Take(VulkanExtensionNames.MutableDescriptorType);
        }

        if (accelerationStructure)
        {
            Take(KhrAccelerationStructure.ExtensionName);
            Take(KhrDeferredHostOperations.ExtensionName);
            Take(VulkanExtensionNames.BufferDeviceAddress);
            Take(VulkanExtensionNames.DescriptorIndexing);
        }

        if (rayQuery)
        {
            Take(VulkanExtensionNames.RayQuery);
        }

        if (rayTracingPipeline)
        {
            Take(KhrRayTracingPipeline.ExtensionName);
            Take(VulkanExtensionNames.PipelineLibrary);
            Take(VulkanExtensionNames.ShaderFloatControls);
            Take(VulkanExtensionNames.SpirV14);
        }

        if (positionFetch)
        {
            Take(VulkanExtensionNames.RayTracingPositionFetch);
        }

        return enabled;
    }
}
