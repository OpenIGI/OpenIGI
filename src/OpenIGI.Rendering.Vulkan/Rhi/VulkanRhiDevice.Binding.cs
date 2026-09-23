using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>Bind groups, submission, readback, and what this device reports about itself.</summary>
internal sealed unsafe partial class VulkanRhiDevice
{
    /// <summary>How many descriptors of each kind one pool holds.</summary>
    /// <remarks>
    /// Pools are grown by adding another rather than by resizing, because a descriptor set cannot be
    /// moved once written. Sized generously: a pool is a few kilobytes and running out mid-frame would
    /// mean allocating one while recording, which is the one time it must not happen.
    /// </remarks>
    private const int PoolCapacity = 1024;

    /// <inheritdoc/>
    public IRhiBindGroup CreateBindGroup(RhiBindGroupDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        VulkanRhiBindGroupLayout layout = (VulkanRhiBindGroupLayout)descriptor.Layout;
        DescriptorSet set = Allocate(layout.Handle);

        List<(VulkanRhiTexture, bool)> textures = [];
        List<VulkanRhiBuffer> buffers = [];

        WriteDescriptorSet[] writes = new WriteDescriptorSet[descriptor.Entries.Count];
        DescriptorBufferInfo[] bufferInfos = new DescriptorBufferInfo[descriptor.Entries.Count];
        DescriptorImageInfo[] imageInfos = new DescriptorImageInfo[descriptor.Entries.Count];

        fixed (DescriptorBufferInfo* firstBuffer = bufferInfos)
        fixed (DescriptorImageInfo* firstImage = imageInfos)
        {
            for (int i = 0; i < writes.Length; i++)
            {
                RhiBindGroupEntry entry = descriptor.Entries[i];
                RhiBindGroupLayoutEntry declared = Declared(layout, entry.Binding);

                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = (uint)entry.Binding,
                    DescriptorCount = 1,
                    DescriptorType = VulkanRhiTranslation.BindingType(declared.Type, declared.HasDynamicOffset),
                };

                if (entry.Buffer is VulkanRhiBuffer buffer)
                {
                    buffers.Add(buffer);
                    bufferInfos[i] = new DescriptorBufferInfo
                    {
                        Buffer = buffer.Buffer.Handle,

                        // A dynamic binding takes its offset when it is bound, so the descriptor itself
                        // starts at zero and the range is what one binding covers.
                        Offset = declared.HasDynamicOffset ? 0 : entry.Offset,
                        Range = entry.Size > 0 ? entry.Size : buffer.SizeInBytes - entry.Offset,
                    };

                    writes[i].PBufferInfo = firstBuffer + i;
                    continue;
                }

                if (entry.TextureView is VulkanRhiTextureView view)
                {
                    bool storage = declared.Type == RhiBindingType.StorageTexture;
                    textures.Add((view.Owner, storage));

                    imageInfos[i] = new DescriptorImageInfo
                    {
                        ImageView = view.Handle,

                        // General for both, which is the resting layout every texture here is kept in.
                        // A sampled image would prefer ShaderReadOnlyOptimal and cannot have it while
                        // the promise is that nothing has to think about layouts.
                        ImageLayout = ImageLayout.General,
                    };

                    writes[i].PImageInfo = firstImage + i;
                    continue;
                }

                if (entry.Sampler is VulkanRhiSampler sampler)
                {
                    imageInfos[i] = new DescriptorImageInfo { Sampler = sampler.Handle };
                    writes[i].PImageInfo = firstImage + i;
                    continue;
                }

                throw new GraphicsDeviceException(
                    $"The bind group '{descriptor.Label}' has nothing to bind at binding {entry.Binding}.");
            }

            fixed (WriteDescriptorSet* firstWrite = writes)
            {
                _vk.UpdateDescriptorSets(_device.Device, (uint)writes.Length, firstWrite, 0, null);
            }
        }

        return new VulkanRhiBindGroup(set, descriptor.Label, textures, buffers);
    }

    /// <inheritdoc/>
    public IRhiCommandEncoder CreateCommandEncoder(string label = "")
    {
        ThrowIfDisposed();

        CommandBuffer command = _commands.AllocatePrimary(1)[0];

        CommandBufferBeginInfo begin = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        VkCheck.Ok(_vk.BeginCommandBuffer(command, &begin), "vkBeginCommandBuffer");

        return new VulkanRhiCommandEncoder(this, _vk, command, label);
    }

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<IRhiCommandBuffer> commands)
    {
        ThrowIfDisposed();

        if (commands.Length == 0)
        {
            return;
        }

        // Everything these write is done once the submission carrying them is: inside a frame the frame's
        // own, outside one the loose submission this makes next.
        ulong ticket = _signalled + 1;

        CommandBuffer[] handles = new CommandBuffer[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            VulkanRhiCommandBuffer command = (VulkanRhiCommandBuffer)commands[i];
            handles[i] = command.Handle;

            foreach (VulkanRhiBuffer written in command.Written)
            {
                written.WriteTicket = ticket;
            }
        }

        SubmitOrHold(handles);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Vulkan could return this synchronously, and the interface does not let it: a browser cannot, so
    /// nothing above may assume it. What this returns is a future that is already complete once the work
    /// behind it has run, which is what <see cref="IRhiReadback.Wait"/> makes happen.
    /// </remarks>
    public IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        VulkanRhiBuffer source = (VulkanRhiBuffer)buffer;
        ulong size = sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset;

        // Anything written and not yet sent goes now, so the ticket below covers it.
        FlushUploads();
        return new VulkanRhiReadback(this, source, offset, size, source.WriteTicket);
    }

    /// <summary>Waits until everything submitted has finished, then reads a host-visible buffer.</summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="offset">Where the read starts.</param>
    /// <param name="size">How many bytes.</param>
    /// <returns>The bytes.</returns>
    /// <summary>Reads a host-visible buffer whose writes are known to have finished, without waiting.</summary>
    internal static byte[] ReadFinished(VulkanRhiBuffer buffer, ulong offset, ulong size)
    {
        byte[] bytes = new byte[size];
        ReadHostVisible(buffer.Buffer, offset, bytes);
        return bytes;
    }

    internal byte[] ReadBack(VulkanRhiBuffer buffer, ulong offset, ulong size)
    {
        FlushUploads();
        _vk.DeviceWaitIdle(_device.Device);
        ReleaseFinishedLoose();

        byte[] bytes = new byte[size];
        if (buffer.Buffer.HostVisible)
        {
            ReadHostVisible(buffer.Buffer, offset, bytes);
            return bytes;
        }

        // Not host-visible, so it is copied into one that is. The interface says a readback needs
        // MapRead, and this is what happens when a caller asks anyway rather than a silent zero.
        using VulkanBuffer staging = new(
            _vk,
            _device,
            _allocator,
            size,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        Silk.NET.Vulkan.Buffer source = buffer.Buffer.Handle;
        Silk.NET.Vulkan.Buffer destination = staging.Handle;

        _commands.SingleTime(cmd =>
        {
            BufferCopy region = new() { SrcOffset = offset, DstOffset = 0, Size = size };
            _vk.CmdCopyBuffer(cmd, source, destination, 1, &region);
        });

        ReadHostVisible(staging, 0, bytes);
        return bytes;
    }

    private static void ReadHostVisible(VulkanBuffer buffer, ulong offset, Span<byte> destination)
    {
        if (buffer.PersistentlyMapped)
        {
            new ReadOnlySpan<byte>((byte*)buffer.MappedPointer + offset, destination.Length)
                .CopyTo(destination);

            return;
        }

        buffer.Read(destination);
    }

    /// <summary>What the layout said a binding holds, which decides the descriptor written.</summary>
    private static RhiBindGroupLayoutEntry Declared(VulkanRhiBindGroupLayout layout, int binding)
    {
        foreach (RhiBindGroupLayoutEntry entry in layout.Entries)
        {
            if (entry.Binding == binding)
            {
                return entry;
            }
        }

        throw new GraphicsDeviceException(
            $"The layout '{layout.Label}' has no binding {binding}, so nothing can be bound there.");
    }

    private DescriptorSet Allocate(DescriptorSetLayout layout)
    {
        if (_poolRemaining <= 0)
        {
            _pool = CreatePool();
            _pools.Add(_pool);
            _poolRemaining = PoolCapacity;
        }

        DescriptorSetLayout local = layout;
        DescriptorSetAllocateInfo info = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &local,
        };

        DescriptorSet set;
        VkCheck.Ok(_vk.AllocateDescriptorSets(_device.Device, &info, &set), "vkAllocateDescriptorSets");

        _poolRemaining--;
        return set;
    }

    private DescriptorPool CreatePool()
    {
        Span<DescriptorPoolSize> sizes =
        [
            new DescriptorPoolSize(DescriptorType.UniformBuffer, PoolCapacity),
            new DescriptorPoolSize(DescriptorType.UniformBufferDynamic, PoolCapacity),
            new DescriptorPoolSize(DescriptorType.StorageBuffer, PoolCapacity),
            new DescriptorPoolSize(DescriptorType.StorageBufferDynamic, PoolCapacity),
            new DescriptorPoolSize(DescriptorType.SampledImage, PoolCapacity),
            new DescriptorPoolSize(DescriptorType.StorageImage, PoolCapacity),
            new DescriptorPoolSize(DescriptorType.Sampler, PoolCapacity),
        ];

        fixed (DescriptorPoolSize* first = sizes)
        {
            DescriptorPoolCreateInfo info = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)sizes.Length,
                PPoolSizes = first,
                MaxSets = PoolCapacity,
            };

            DescriptorPool pool;
            VkCheck.Ok(_vk.CreateDescriptorPool(_device.Device, &info, null, &pool), "vkCreateDescriptorPool");
            return pool;
        }
    }

    /// <summary>What this device offers, in the interface's terms.</summary>
    private static RhiFeatures ReadFeatures(VulkanDevice device) => new(
        RayQuery: device.Capabilities.RayQuery,
        TimestampQuery: device.Selection.Properties.Limits.TimestampComputeAndGraphics,
        Float32Filterable: true,
        DepthClamp: true,
        IndirectFirstInstance: true,
        TextureCompressionBc: true);

    /// <summary>The sizes this device will not exceed.</summary>
    /// <remarks>
    /// Read from what the driver reports, except the push data: Vulkan guarantees at least 128 bytes and
    /// most drivers offer far more, and the number here is what a pipeline layout is checked against.
    /// </remarks>
    private static RhiLimits ReadLimits(VulkanDevice device)
    {
        PhysicalDeviceLimits limits = device.Selection.Properties.Limits;

        return new RhiLimits(
            MaxTextureDimension2D: (int)limits.MaxImageDimension2D,
            MaxTextureDimension3D: (int)limits.MaxImageDimension3D,
            MaxTextureArrayLayers: (int)limits.MaxImageArrayLayers,
            MaxBindGroups: (int)limits.MaxBoundDescriptorSets,
            MaxSamplersPerStage: (int)limits.MaxPerStageDescriptorSamplers,
            MaxSampledTexturesPerStage: (int)limits.MaxPerStageDescriptorSampledImages,
            MaxStorageBuffersPerStage: (int)limits.MaxPerStageDescriptorStorageBuffers,
            MaxStorageTexturesPerStage: (int)limits.MaxPerStageDescriptorStorageImages,
            MaxUniformBufferBindingSize: limits.MaxUniformBufferRange,
            MaxStorageBufferBindingSize: limits.MaxStorageBufferRange,
            MaxPushDataBytes: (int)limits.MaxPushConstantsSize,
            MaxComputeWorkgroupStorageBytes: (int)limits.MaxComputeSharedMemorySize,
            MaxComputeInvocationsPerWorkgroup: (int)limits.MaxComputeWorkGroupInvocations,

            // One number where Vulkan reports three, and the smallest of them: a dispatch that fits it
            // fits on every axis, which is what the interface promises.
            MaxComputeWorkgroupsPerDimension: (int)Math.Min(
                limits.MaxComputeWorkGroupCount[0],
                Math.Min(limits.MaxComputeWorkGroupCount[1], limits.MaxComputeWorkGroupCount[2])),
            MaxColorAttachments: (int)limits.MaxColorAttachments,
            MaxSampleCount: SampleCount(limits.FramebufferColorSampleCounts));
    }

    private static int SampleCount(SampleCountFlags flags)
    {
        if (flags.HasFlag(SampleCountFlags.Count8Bit))
        {
            return 8;
        }

        if (flags.HasFlag(SampleCountFlags.Count4Bit))
        {
            return 4;
        }

        return flags.HasFlag(SampleCountFlags.Count2Bit) ? 2 : 1;
    }
}

/// <summary>A UTF-8 entry-point name, alive for as long as the create call needs it.</summary>
internal sealed unsafe class Utf8String : IDisposable
{
    private nint _pointer;

    /// <summary>Initialises a new instance of the <see cref="Utf8String"/> class.</summary>
    /// <param name="text">The text.</param>
    public Utf8String(string text) => _pointer = Marshal.StringToHGlobalAnsi(text);

    /// <summary>Gets the bytes, null-terminated.</summary>
    public byte* Pointer => (byte*)_pointer;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_pointer != 0)
        {
            Marshal.FreeHGlobal(_pointer);
            _pointer = 0;
        }
    }
}
