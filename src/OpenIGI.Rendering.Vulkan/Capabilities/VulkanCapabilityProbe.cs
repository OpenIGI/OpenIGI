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

        // The one structure that is read to find out what is *missing* rather than what is available.
        // Every field is something a conformant adapter simply has; a portable one answers for each.
        PhysicalDevicePortabilitySubsetFeaturesKHR* portability = null;
        if (available.Contains(VulkanExtensionNames.PortabilitySubset))
        {
            portability = chain.Add(new PhysicalDevicePortabilitySubsetFeaturesKHR
            {
                SType = StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr,
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
            ShaderInt64 = features.Features.ShaderInt64,
            FragmentStoresAndAtomics = features.Features.FragmentStoresAndAtomics,
            VertexPipelineStoresAndAtomics = features.Features.VertexPipelineStoresAndAtomics,
            StorageImageWriteWithoutFormat = features.Features.ShaderStorageImageWriteWithoutFormat,
            IndependentBlend = features.Features.IndependentBlend,
            ArrayDynamicIndexing = features.Features.ShaderSampledImageArrayDynamicIndexing
                && features.Features.ShaderStorageBufferArrayDynamicIndexing
                && features.Features.ShaderStorageImageArrayDynamicIndexing
                && features.Features.ShaderUniformBufferArrayDynamicIndexing,
            PortabilitySubset = portability is not null,
            PortabilityFeatures = portability is not null ? *portability : default,

            // Absent the extension the adapter is conformant and has it; with the extension it is
            // whatever was answered. Read this way round so every caller can ask the same question
            // without first asking whether the adapter is a portable one.
            SamplerMipLodBias = portability is null || portability->SamplerMipLodBias,
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

        // A structure may be linked only when the extension that DEFINES it is one the device is being
        // created with. The capability flags are not that question: a capability says the adapter can do
        // something, while the extension list says what is being switched on, and the two part company
        // whenever a capability is reported on its own but only ever enabled alongside another. Linking a
        // structure whose extension is not enabled is undefined -- drivers that do not recognise it walk
        // into it anyway, and vkCreateDevice faults inside the driver rather than returning an error.
        // Asking the enabled list, which is the very array handed to vkCreateDevice, is what keeps the
        // chain and the extensions from being able to disagree at all.
        bool Enabled(string extension) => capabilities.EnabledExtensions.Contains(extension);

        if (capabilities.Robustness2 && Enabled(VulkanExtensionNames.Robustness2))
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

        if (Enabled("VK_EXT_device_fault"))
        {
            chain.Add(new PhysicalDeviceFaultFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceFaultFeaturesExt,
                DeviceFault = true,
            });
        }

        if (capabilities.DynamicRenderingLocalRead && Enabled(VulkanExtensionNames.DynamicRenderingLocalRead))
        {
            chain.Add(new PhysicalDeviceDynamicRenderingLocalReadFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceDynamicRenderingLocalReadFeaturesKhr,
                DynamicRenderingLocalRead = true,
            });
        }

        if (capabilities.MutableDescriptorType && Enabled(VulkanExtensionNames.MutableDescriptorType))
        {
            chain.Add(new PhysicalDeviceMutableDescriptorTypeFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceMutableDescriptorTypeFeaturesExt,
                MutableDescriptorType = true,
            });
        }

        if (capabilities.DescriptorBuffer && Enabled(VulkanExtensionNames.DescriptorBuffer))
        {
            chain.Add(new PhysicalDeviceDescriptorBufferFeaturesEXT
            {
                SType = StructureType.PhysicalDeviceDescriptorBufferFeaturesExt,
                DescriptorBuffer = true,
            });
        }

        if (capabilities.RayTracingPositionFetch && Enabled(VulkanExtensionNames.RayTracingPositionFetch))
        {
            chain.Add(new PhysicalDeviceRayTracingPositionFetchFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPositionFetchFeaturesKhr,
                RayTracingPositionFetch = true,
            });
        }

        if (capabilities.RayTracingPipeline && Enabled(KhrRayTracingPipeline.ExtensionName))
        {
            chain.Add(new PhysicalDeviceRayTracingPipelineFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
                RayTracingPipeline = true,
                RayTracingPipelineTraceRaysIndirect = capabilities.RayTracingIndirect,
                RayTraversalPrimitiveCulling = capabilities.RayTraversalPrimitiveCulling,
            });
        }

        if (capabilities.RayQuery && Enabled(VulkanExtensionNames.RayQuery))
        {
            chain.Add(new PhysicalDeviceRayQueryFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayQueryFeaturesKhr,
                RayQuery = true,
            });
        }

        if (capabilities.AccelerationStructure && Enabled(KhrAccelerationStructure.ExtensionName))
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

        if (capabilities.PortabilitySubset && Enabled(VulkanExtensionNames.PortabilitySubset))
        {
            // Handed straight back rather than filtered. Each field is a capability the adapter has
            // already said whether it has, so echoing the answer enables everything it can do and asks
            // for nothing it cannot -- and the structure has to be chained at all, because enabling the
            // extension without it leaves every one of these off.
            PhysicalDevicePortabilitySubsetFeaturesKHR portability = capabilities.PortabilityFeatures;
            portability.SType = StructureType.PhysicalDevicePortabilitySubsetFeaturesKhr;
            portability.PNext = null;
            chain.Add(portability);
        }

        if (capabilities.Storage16Bit)
        {
            chain.Add(new PhysicalDeviceVulkan11Features
            {
                SType = StructureType.PhysicalDeviceVulkan11Features,
                StorageBuffer16BitAccess = true,
                UniformAndStorageBuffer16BitAccess = true,
            });
        }

        // Every field here is what the probe found, never what would be convenient. vkCreateDevice fails
        // the whole call with VK_ERROR_FEATURE_NOT_PRESENT if a single requested feature is absent, so
        // naming one unconditionally turns an adapter that is merely weaker into an adapter the port
        // cannot start on at all -- which is what a Metal adapter behind MoltenVK looks like.
        //
        // None of these is load-bearing today, which is why asking for them where they are offered costs
        // nothing and declining them where they are not costs nothing either. Every store and every
        // atomic in the backend is in a compute shader, where neither stores-and-atomics feature applies;
        // every storage image is declared with an explicit format qualifier; and both multiple-attachment
        // pipelines give all four attachments the same blend state. They are asked for because a pass
        // added later may want them, and because a capability the device reports is free to enable.
        return new PhysicalDeviceFeatures
        {
            SamplerAnisotropy = capabilities.SamplerAnisotropy,
            RobustBufferAccess = capabilities.RobustBufferAccess,
            ShaderInt64 = capabilities.ShaderInt64,
            FragmentStoresAndAtomics = capabilities.FragmentStoresAndAtomics,
            VertexPipelineStoresAndAtomics = capabilities.VertexPipelineStoresAndAtomics,
            ShaderStorageImageWriteWithoutFormat = capabilities.StorageImageWriteWithoutFormat,
            IndependentBlend = capabilities.IndependentBlend,
            ShaderSampledImageArrayDynamicIndexing = capabilities.ArrayDynamicIndexing,
            ShaderStorageBufferArrayDynamicIndexing = capabilities.ArrayDynamicIndexing,
            ShaderStorageImageArrayDynamicIndexing = capabilities.ArrayDynamicIndexing,
            ShaderUniformBufferArrayDynamicIndexing = capabilities.ArrayDynamicIndexing,
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

        // Not optional in the way everything below it is: an adapter that advertises the portability
        // subset must have it enabled, so this is taken first and unconditionally rather than gated on
        // wanting it.
        Take(VulkanExtensionNames.PortabilitySubset);

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
