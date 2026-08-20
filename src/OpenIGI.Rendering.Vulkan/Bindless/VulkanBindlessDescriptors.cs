using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace OpenIGI.Rendering.Vulkan.Bindless;

/// <summary>
/// The two descriptor heaps every shader reaches its resources through, and the one pipeline layout they
/// are bound with.
/// </summary>
/// <remarks>
/// <para>
/// Instead of a descriptor set per draw, there are two arrays that hold everything for the whole frame:
/// one of samplers, one of every other resource. A shader is given integers and indexes those arrays.
/// Nothing is bound per draw, which is what makes a single dispatch able to shade a thousand materials —
/// and, more to the point here, what lets a ray that hits an arbitrary triangle reach that triangle's
/// textures, which is impossible when a resource has to be bound before the work that uses it is
/// recorded.
/// </para>
/// <para>
/// The heaps are ordinary buffers the application writes descriptors into, not pool-allocated sets. That
/// is what a descriptor buffer means: the driver tells us how many bytes a descriptor of each type
/// occupies, and writing one is a call that fills those bytes. A slot can be rewritten while a command
/// buffer that names the heap is already recorded, because the command buffer refers to the buffer's
/// address rather than to the descriptors in it.
/// </para>
/// <para>
/// The resource heap is a single array of a type that is decided per write rather than fixed by the
/// layout. Without that it would need one array per resource kind and a handle would have to say which
/// array it indexed; with it there is one array, and a handle is just a number.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBindlessDescriptors : IDisposable
{
    /// <summary>Which set index the sampler heap is bound at.</summary>
    public const uint SamplerSet = 0;

    /// <summary>Which set index the resource heap is bound at.</summary>
    public const uint ResourceSet = 1;

    /// <summary>How many bytes of push constants every pipeline layout reserves.</summary>
    /// <remarks>
    /// The minimum every Vulkan implementation guarantees. Descriptor indices, a few scalars and the odd
    /// device address are all a pass needs to push; anything larger belongs in a buffer the heap holds.
    /// </remarks>
    public const uint PushConstantSize = 128;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly ExtDescriptorBuffer _api;
    private readonly Heap _samplers;
    private readonly Heap _resources;
    private readonly ulong _offsetAlignment;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanBindlessDescriptors"/> class.</summary>
    /// <param name="instance">The instance the device was created under.</param>
    /// <param name="device">The device the heaps live on.</param>
    /// <param name="allocator">The allocator backing the heap buffers.</param>
    /// <param name="samplerCapacity">How many samplers the sampler heap holds.</param>
    /// <param name="resourceCapacity">How many resources the resource heap holds.</param>
    /// <exception cref="GraphicsDeviceException">The device cannot hold descriptors in a buffer.</exception>
    public VulkanBindlessDescriptors(
        VulkanInstance instance,
        VulkanDevice device,
        VulkanAllocator allocator,
        uint samplerCapacity = 1024,
        uint resourceCapacity = 65536)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);

        if (!device.Capabilities.DescriptorBuffer)
        {
            throw new GraphicsDeviceException("This device cannot hold descriptors in a buffer.");
        }

        _vk = instance.Vk;
        _device = device;

        if (!_vk.TryGetDeviceExtension(instance.Instance, device.Device, out ExtDescriptorBuffer api))
        {
            throw new GraphicsDeviceException("VK_EXT_descriptor_buffer could not be loaded.");
        }

        _api = api;

        PhysicalDeviceDescriptorBufferPropertiesEXT limits = device.Capabilities.DescriptorBufferLimits;
        _offsetAlignment = Math.Max(1, limits.DescriptorBufferOffsetAlignment);

        // A mutable slot has to be as large as the largest descriptor it could ever hold, since the type
        // is not known until it is written.
        nuint resourceStride = Max(
            limits.SampledImageDescriptorSize,
            limits.StorageImageDescriptorSize,
            limits.UniformBufferDescriptorSize,
            limits.StorageBufferDescriptorSize,
            limits.UniformTexelBufferDescriptorSize,
            limits.StorageTexelBufferDescriptorSize,
            limits.AccelerationStructureDescriptorSize);

        _samplers = CreateHeap(
            allocator,
            VulkanDescriptorHeap.Sampler,
            samplerCapacity,
            limits.SamplerDescriptorSize,
            BufferUsageFlags.SamplerDescriptorBufferBitExt);

        _resources = CreateHeap(
            allocator,
            VulkanDescriptorHeap.Resource,
            resourceCapacity,
            resourceStride,
            BufferUsageFlags.ResourceDescriptorBufferBitExt);

        PipelineLayout = CreatePipelineLayout();
    }

    /// <summary>Gets the layout every pipeline in the backend is created against.</summary>
    /// <remarks>
    /// One layout for everything. Because no pipeline declares the resources it uses — they are all
    /// reached through the heaps — there is nothing to vary between them, and a shared layout means the
    /// heaps are bound once per command buffer rather than once per pipeline change.
    /// </remarks>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets the flag every pipeline must be created with to read from the heaps.</summary>
    public static PipelineCreateFlags PipelineCreateFlag => PipelineCreateFlags.CreateDescriptorBufferBitExt;

    /// <summary>Gets how many sampler slots are in use.</summary>
    public uint SamplersInUse => _samplers.Allocator.InUse;

    /// <summary>Gets how many resource slots are in use.</summary>
    public uint ResourcesInUse => _resources.Allocator.InUse;

    /// <summary>Binds both heaps to a command buffer.</summary>
    /// <param name="commandBuffer">The command buffer to bind into.</param>
    /// <param name="bindPoints">Which pipeline bind points the heaps should be reachable from.</param>
    /// <remarks>
    /// Called once when a command buffer is opened, not per draw. The binding names the buffers'
    /// addresses, so a descriptor written afterwards is picked up without rebinding anything.
    /// </remarks>
    public void Bind(CommandBuffer commandBuffer, params PipelineBindPoint[] bindPoints)
    {
        ArgumentNullException.ThrowIfNull(bindPoints);

        DescriptorBufferBindingInfoEXT* bindings = stackalloc DescriptorBufferBindingInfoEXT[2];
        bindings[0] = new DescriptorBufferBindingInfoEXT
        {
            SType = StructureType.DescriptorBufferBindingInfoExt,
            Address = _samplers.Buffer.DeviceAddress,
            Usage = BufferUsageFlags.SamplerDescriptorBufferBitExt | BufferUsageFlags.ShaderDeviceAddressBit,
        };
        bindings[1] = new DescriptorBufferBindingInfoEXT
        {
            SType = StructureType.DescriptorBufferBindingInfoExt,
            Address = _resources.Buffer.DeviceAddress,
            Usage = BufferUsageFlags.ResourceDescriptorBufferBitExt | BufferUsageFlags.ShaderDeviceAddressBit,
        };

        _api.CmdBindDescriptorBuffers(commandBuffer, 2, bindings);

        uint* bufferIndices = stackalloc uint[2] { 0, 1 };
        ulong* offsets = stackalloc ulong[2] { 0, 0 };

        foreach (PipelineBindPoint bindPoint in bindPoints)
        {
            _api.CmdSetDescriptorBufferOffsets(
                commandBuffer,
                bindPoint,
                PipelineLayout,
                0,
                2,
                bufferIndices,
                offsets);
        }
    }

    /// <summary>Writes a sampler into the sampler heap.</summary>
    /// <param name="sampler">The sampler.</param>
    /// <returns>The handle a shader indexes the sampler heap with.</returns>
    public VulkanDescriptorHandle CreateSampler(Sampler sampler)
    {
        Sampler local = sampler;
        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.Sampler,
            Data = new DescriptorDataEXT { PSampler = &local },
        };
        return Write(_samplers, DescriptorType.Sampler, &info);
    }

    /// <summary>Writes a sampled image into the resource heap.</summary>
    /// <param name="view">The image view.</param>
    /// <param name="layout">The layout the image will be in when it is read.</param>
    /// <returns>The handle a shader indexes the resource heap with.</returns>
    public VulkanDescriptorHandle CreateSampledImage(ImageView view, ImageLayout layout)
    {
        DescriptorImageInfo image = new() { ImageView = view, ImageLayout = layout };
        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.SampledImage,
            Data = new DescriptorDataEXT { PSampledImage = &image },
        };
        return Write(_resources, DescriptorType.SampledImage, &info);
    }

    /// <summary>Writes a storage image into the resource heap.</summary>
    /// <param name="view">The image view.</param>
    /// <returns>The handle a shader indexes the resource heap with.</returns>
    /// <remarks>
    /// A storage image is always read in the general layout: no optimal layout permits both the reads and
    /// the writes a storage binding allows.
    /// </remarks>
    public VulkanDescriptorHandle CreateStorageImage(ImageView view)
    {
        DescriptorImageInfo image = new() { ImageView = view, ImageLayout = ImageLayout.General };
        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.StorageImage,
            Data = new DescriptorDataEXT { PStorageImage = &image },
        };
        return Write(_resources, DescriptorType.StorageImage, &info);
    }

    /// <summary>Writes a storage buffer into the resource heap.</summary>
    /// <param name="buffer">The buffer, which must carry a device address.</param>
    /// <param name="offset">Where in the buffer the binding starts.</param>
    /// <param name="range">How long the binding is; the rest of the buffer by default.</param>
    /// <returns>The handle a shader indexes the resource heap with.</returns>
    /// <remarks>
    /// A descriptor buffer describes a buffer binding by device address rather than by handle, which is
    /// why every buffer that reaches a shader this way is created addressable.
    /// </remarks>
    public VulkanDescriptorHandle CreateStorageBuffer(VulkanBuffer buffer, ulong offset = 0, ulong range = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        DescriptorAddressInfoEXT address = new()
        {
            SType = StructureType.DescriptorAddressInfoExt,
            Address = buffer.DeviceAddress + offset,
            Range = range == 0 ? buffer.Size - offset : range,
            Format = Format.Undefined,
        };
        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.StorageBuffer,
            Data = new DescriptorDataEXT { PStorageBuffer = &address },
        };
        return Write(_resources, DescriptorType.StorageBuffer, &info);
    }

    /// <summary>Writes a uniform buffer into the resource heap.</summary>
    /// <param name="buffer">The buffer, which must carry a device address.</param>
    /// <param name="offset">Where in the buffer the binding starts.</param>
    /// <param name="range">How long the binding is; the rest of the buffer by default.</param>
    /// <returns>The handle a shader indexes the resource heap with.</returns>
    public VulkanDescriptorHandle CreateUniformBuffer(VulkanBuffer buffer, ulong offset = 0, ulong range = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        DescriptorAddressInfoEXT address = new()
        {
            SType = StructureType.DescriptorAddressInfoExt,
            Address = buffer.DeviceAddress + offset,
            Range = range == 0 ? buffer.Size - offset : range,
            Format = Format.Undefined,
        };
        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.UniformBuffer,
            Data = new DescriptorDataEXT { PUniformBuffer = &address },
        };
        return Write(_resources, DescriptorType.UniformBuffer, &info);
    }

    /// <summary>Writes an acceleration structure into the resource heap.</summary>
    /// <param name="deviceAddress">The structure's device address.</param>
    /// <returns>The handle a shader indexes the resource heap with.</returns>
    /// <remarks>
    /// The scene structure is rebuilt every frame, and this is how a traversal reaches it without the
    /// tracing passes having to be re-recorded: the slot is rewritten and every shader that reads it
    /// picks up the new structure.
    /// </remarks>
    public VulkanDescriptorHandle CreateAccelerationStructure(ulong deviceAddress)
    {
        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.AccelerationStructureKhr,
            Data = new DescriptorDataEXT { AccelerationStructure = deviceAddress },
        };
        return Write(_resources, DescriptorType.AccelerationStructureKhr, &info);
    }

    /// <summary>Rewrites a slot that is already held with a different acceleration structure.</summary>
    /// <param name="handle">The slot to rewrite.</param>
    /// <param name="deviceAddress">The structure's device address.</param>
    public void UpdateAccelerationStructure(VulkanDescriptorHandle handle, ulong deviceAddress)
    {
        if (!handle.IsValid)
        {
            return;
        }

        DescriptorGetInfoEXT info = new()
        {
            SType = StructureType.DescriptorGetInfoExt,
            Type = DescriptorType.AccelerationStructureKhr,
            Data = new DescriptorDataEXT { AccelerationStructure = deviceAddress },
        };
        WriteAt(_resources, DescriptorType.AccelerationStructureKhr, handle.Index, &info);
    }

    /// <summary>Gives a slot back to its heap.</summary>
    /// <param name="handle">The slot to release.</param>
    public void Free(VulkanDescriptorHandle handle)
    {
        if (!handle.IsValid)
        {
            return;
        }

        Heap heap = handle.Heap == VulkanDescriptorHeap.Sampler ? _samplers : _resources;
        heap.Allocator.Free(handle.Index);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyPipelineLayout(_device.Device, PipelineLayout, null);
        _resources.Dispose(_vk, _device.Device);
        _samplers.Dispose(_vk, _device.Device);
        _api.Dispose();
    }

    private static nuint Max(params nuint[] values)
    {
        nuint largest = 0;
        foreach (nuint value in values)
        {
            if (value > largest)
            {
                largest = value;
            }
        }

        return largest;
    }

    private VulkanDescriptorHandle Write(Heap heap, DescriptorType type, DescriptorGetInfoEXT* info)
    {
        uint index = heap.Allocator.Allocate();
        if (index == VulkanDescriptorHandle.InvalidIndex)
        {
            throw new GraphicsDeviceException(
                $"The {heap.Kind} descriptor heap is full at {heap.Allocator.Capacity} slots.");
        }

        WriteAt(heap, type, index, info);
        return new VulkanDescriptorHandle(heap.Kind, index);
    }

    private void WriteAt(Heap heap, DescriptorType type, uint index, DescriptorGetInfoEXT* info)
    {
        // The driver is asked for exactly the bytes this descriptor type occupies, which may be fewer
        // than the mutable slot's stride; the rest of the slot is simply left alone.
        nuint size = DescriptorSize(type);
        byte* destination = (byte*)heap.Buffer.MappedPointer + (index * heap.Stride);
        _api.GetDescriptor(_device.Device, info, size, destination);
    }

    private nuint DescriptorSize(DescriptorType type)
    {
        PhysicalDeviceDescriptorBufferPropertiesEXT limits = _device.Capabilities.DescriptorBufferLimits;
        return type switch
        {
            DescriptorType.Sampler => limits.SamplerDescriptorSize,
            DescriptorType.SampledImage => limits.SampledImageDescriptorSize,
            DescriptorType.StorageImage => limits.StorageImageDescriptorSize,
            DescriptorType.UniformBuffer => limits.UniformBufferDescriptorSize,
            DescriptorType.StorageBuffer => limits.StorageBufferDescriptorSize,
            DescriptorType.UniformTexelBuffer => limits.UniformTexelBufferDescriptorSize,
            DescriptorType.StorageTexelBuffer => limits.StorageTexelBufferDescriptorSize,
            DescriptorType.AccelerationStructureKhr => limits.AccelerationStructureDescriptorSize,
            _ => throw new GraphicsDeviceException($"Descriptor type {type} cannot live in a bindless heap."),
        };
    }

    private Heap CreateHeap(
        VulkanAllocator allocator,
        VulkanDescriptorHeap kind,
        uint capacity,
        nuint descriptorSize,
        BufferUsageFlags heapUsage)
    {
        DescriptorSetLayout layout = kind == VulkanDescriptorHeap.Sampler
            ? CreateSamplerLayout(capacity)
            : CreateResourceLayout(capacity);

        // The driver is asked how large the set actually is rather than the size being computed from the
        // stride, because a layout may carry padding the stride does not describe.
        ulong size = 0;
        _api.GetDescriptorSetLayoutSize(_device.Device, layout, &size);
        size = Math.Max(size, (ulong)descriptorSize * capacity);

        VulkanBuffer buffer = new(
            _vk,
            _device,
            allocator,
            VulkanAlign.Up(Math.Max(size, 1), _offsetAlignment),
            heapUsage | BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.TransferDstBit,
            allocator.UploadMemoryProperties(),
            persistentlyMapped: true);

        return new Heap(kind, layout, buffer, (ulong)descriptorSize, new VulkanDescriptorAllocator(capacity));
    }

    private DescriptorSetLayout CreateSamplerLayout(uint capacity)
    {
        DescriptorBindingFlags bindingFlags = DescriptorBindingFlags.PartiallyBoundBit;
        DescriptorSetLayoutBindingFlagsCreateInfo flagsInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &bindingFlags,
        };

        DescriptorSetLayoutBinding binding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.Sampler,
            DescriptorCount = capacity,
            StageFlags = ShaderStageFlags.All,
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            PNext = &flagsInfo,
            Flags = DescriptorSetLayoutCreateFlags.DescriptorBufferBitExt,
            BindingCount = 1,
            PBindings = &binding,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(
            _vk.CreateDescriptorSetLayout(_device.Device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout (samplers)");
        return layout;
    }

    private DescriptorSetLayout CreateResourceLayout(uint capacity)
    {
        // Every type one slot may turn out to hold. The driver sizes the mutable slot from this list, so
        // anything left out cannot later be written into the heap.
        DescriptorType* types = stackalloc DescriptorType[7]
        {
            DescriptorType.SampledImage,
            DescriptorType.StorageImage,
            DescriptorType.UniformTexelBuffer,
            DescriptorType.StorageTexelBuffer,
            DescriptorType.StorageBuffer,
            DescriptorType.UniformBuffer,
            DescriptorType.AccelerationStructureKhr,
        };

        MutableDescriptorTypeListEXT typeList = new()
        {
            DescriptorTypeCount = 7,
            PDescriptorTypes = types,
        };

        MutableDescriptorTypeCreateInfoEXT mutableInfo = new()
        {
            SType = StructureType.MutableDescriptorTypeCreateInfoExt,
            MutableDescriptorTypeListCount = 1,
            PMutableDescriptorTypeLists = &typeList,
        };

        DescriptorBindingFlags bindingFlags = DescriptorBindingFlags.PartiallyBoundBit;
        DescriptorSetLayoutBindingFlagsCreateInfo flagsInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            PNext = &mutableInfo,
            BindingCount = 1,
            PBindingFlags = &bindingFlags,
        };

        DescriptorSetLayoutBinding binding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.MutableExt,
            DescriptorCount = capacity,
            StageFlags = ShaderStageFlags.All,
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            PNext = &flagsInfo,
            Flags = DescriptorSetLayoutCreateFlags.DescriptorBufferBitExt,
            BindingCount = 1,
            PBindings = &binding,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(
            _vk.CreateDescriptorSetLayout(_device.Device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout (resources)");
        return layout;
    }

    private PipelineLayout CreatePipelineLayout()
    {
        DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[2]
        {
            _samplers.Layout,
            _resources.Layout,
        };

        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.All,
            Offset = 0,
            Size = PushConstantSize,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = layouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(
            _vk.CreatePipelineLayout(_device.Device, &createInfo, null, &layout),
            "vkCreatePipelineLayout (bindless)");
        return layout;
    }

    private sealed record Heap(
        VulkanDescriptorHeap Kind,
        DescriptorSetLayout Layout,
        VulkanBuffer Buffer,
        ulong Stride,
        VulkanDescriptorAllocator Allocator)
    {
        public void Dispose(Vk vk, Device device)
        {
            Buffer.Dispose();
            vk.DestroyDescriptorSetLayout(device, Layout, null);
        }
    }
}
