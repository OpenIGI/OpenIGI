using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// The RHI, answered through Vulkan.
/// </summary>
/// <remarks>
/// <para>
/// Where the WebGPU implementation is mostly a translation followed by one call, this one has work to
/// do: the interface promises no barriers and no layouts, and Vulkan has both. What that costs is paid
/// here and in the encoder, and it is the price of an interface a browser can also implement.
/// </para>
/// <para>
/// It owns nothing it did not make. The device, the allocator and the command pool are handed in,
/// because the backend already has them and a second set would be a second set of everything.
/// </para>
/// </remarks>
internal sealed unsafe partial class VulkanRhiDevice : IRhiDevice
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly IOpenIgiLogger _logger;
    private readonly List<DescriptorPoolState> _pools = [];
    private DescriptorPoolState? _pool;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiDevice"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device everything lives on.</param>
    /// <param name="allocator">The allocator resources are made from.</param>
    /// <param name="commands">The command pool encoders are allocated from.</param>
    /// <param name="logger">Where a widened format or a stood-down feature is reported.</param>
    /// <param name="instance">
    /// The instance the device came from, which ray tracing needs for its entry points; without it the
    /// device offers none.
    /// </param>
    public VulkanRhiDevice(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        IOpenIgiLogger logger,
        VulkanInstance? instance = null)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(logger);

        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _logger = logger;

        // A one-off goes to the queue around the frame, so anything it may read that is still queued here
        // -- a texture created or written since the last submission -- has to reach the queue before it.
        _commands.BeforeOneOffSubmit = SubmitPendingUploads;

        Features = ReadFeatures(device);
        Limits = ReadLimits(device);
        AdapterName = device.Selection.Name;

        CreateTimeline();

        // Acceleration structures, where the adapter traces. The timeline is asked for as well: the traced
        // world above paces its structures' lifetimes by the submissions it marks, which needs one.
        if (instance is not null && HasTimeline && device.Capabilities.RayQuery && device.Capabilities.AccelerationStructure)
        {
            _accelerationStructures = new VulkanRhiAccelerationStructures(
                vk, new RayTracing.VulkanRayTracingApi(instance, device), device, allocator);
        }
    }

    private readonly VulkanRhiAccelerationStructures? _accelerationStructures;

    /// <inheritdoc/>
    public string AdapterName { get; }

    /// <inheritdoc/>
    public string ApiName => "Vulkan";

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the adapter rather than assumed. It differs by an order of magnitude between vendors,
    /// and a wrong one reports every stage's cost wrong by that factor while looking entirely plausible.
    /// </remarks>
    public double TimestampPeriodNanoseconds => _device.Selection.Properties.Limits.TimestampPeriod;

    /// <inheritdoc/>
    public RhiFeatures Features { get; }

    /// <inheritdoc/>
    public ShaderTarget ShaderTarget => ShaderTarget.Spirv;

    /// <inheritdoc/>
    public RhiLimits Limits { get; }

    /// <inheritdoc/>
    /// <remarks>Null on an adapter that does not trace.</remarks>
    public IRhiAccelerationStructures? AccelerationStructures => _accelerationStructures;

    /// <summary>Gets the acceleration structures as this backend records them.</summary>
    internal VulkanRhiAccelerationStructures? VulkanAccelerationStructures => _accelerationStructures;

    /// <inheritdoc/>
    public ulong LastSubmittedValue => _signalled;

    /// <inheritdoc/>
    public ulong CompletedValue
    {
        get
        {
            if (!HasTimeline)
            {
                return _signalled;
            }

            ulong value = 0;
            _vk.GetSemaphoreCounterValue(_device.Device, _timeline, &value);
            return value;
        }
    }

    /// <summary>Gets the device everything lives on.</summary>
    public VulkanDevice Device => _device;

    /// <inheritdoc/>
    public IRhiBuffer CreateBuffer(in RhiBufferDescriptor descriptor)
    {
        ThrowIfDisposed();

        // Asking for a build-input buffer on an adapter with no acceleration structures is not merely
        // useless, it is invalid -- the flag names an extension that is not enabled. Dropped here rather
        // than refused, because the caller is a mesh that does not know whether it will ever be traced.
        RhiBufferUsage usage = _device.Capabilities.AccelerationStructure
            ? descriptor.Usage
            : descriptor.Usage & ~(RhiBufferUsage.RayTracingInput | RhiBufferUsage.AccelerationStructureScratch);

        // What the host writes and a build reads -- a scene's placements, the hit records -- goes where both
        // reach fastest: device-local memory the host can map, where the adapter offers it.
        MemoryPropertyFlags memory = !VulkanRhiTranslation.NeedsHostVisibleMemory(usage)
            ? MemoryPropertyFlags.DeviceLocalBit
            : usage.HasFlag(RhiBufferUsage.RayTracingInput) && !usage.HasFlag(RhiBufferUsage.MapRead)
                ? _allocator.UploadMemoryProperties()
                : MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

        VulkanBuffer buffer = new(
            _vk,
            _device,
            _allocator,
            descriptor.SizeInBytes,
            VulkanRhiTranslation.BufferUsage(usage),
            memory,
            persistentlyMapped: VulkanRhiTranslation.NeedsHostVisibleMemory(descriptor.Usage));

        return new VulkanRhiBuffer(buffer, descriptor) { Owner = this, ShaderHandle = usage.HasFlag(RhiBufferUsage.RayTracingInput) ? buffer.DeviceAddress : 0 };
    }

    /// <inheritdoc/>
    public IRhiTexture CreateTexture(in RhiTextureDescriptor descriptor)
    {
        ThrowIfDisposed();

        bool depth = descriptor.Format.IsDepth();
        bool volume = descriptor.Dimension == RhiTextureViewDimension.Texture3D;

        ImageCreateInfo info = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = VulkanRhiTranslation.TextureDimension(descriptor.Dimension),
            Format = VulkanRhiTranslation.TextureFormat(descriptor.Format),
            Extent = new Extent3D(
                (uint)Math.Max(1, descriptor.Width),
                (uint)Math.Max(1, descriptor.Height),
                (uint)(volume ? Math.Max(1, descriptor.DepthOrArrayLayers) : 1)),
            MipLevels = (uint)Math.Max(1, descriptor.MipLevelCount),
            ArrayLayers = (uint)(volume ? 1 : Math.Max(1, descriptor.DepthOrArrayLayers)),
            Samples = (SampleCountFlags)Math.Max(1, descriptor.SampleCount),
            Tiling = ImageTiling.Optimal,
            Usage = VulkanRhiTranslation.TextureUsage(descriptor.Usage, depth),
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        if (descriptor.Dimension == RhiTextureViewDimension.Cube)
        {
            info.Flags = ImageCreateFlags.CreateCubeCompatibleBit;
        }

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device.Device, &info, null, &image), "vkCreateImage");

        _vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
        VulkanAllocation memory = _allocator.AllocateResource(
            requirements, MemoryPropertyFlags.DeviceLocalBit, VulkanResourceKind.Image);
        VkCheck.Ok(_vk.BindImageMemory(_device.Device, image, memory.Memory, memory.Offset), "vkBindImageMemory");

        VulkanRhiTexture texture = new(_vk, _device.Device, _allocator, image, memory, descriptor) { Owner = this };

        // Into its resting layout ahead of anything that uses it, so nothing downstream has to know an
        // image starts undefined. A transition out of Undefined discards whatever was there, which is
        // exactly right for one nobody has written to yet. Queued with the uploads rather than submitted
        // and waited on: see QueueTextureUpload.
        QueueInitialTransition(texture);
        return texture;
    }

    /// <inheritdoc/>
    public IRhiSampler CreateSampler(in RhiSamplerDescriptor descriptor)
    {
        ThrowIfDisposed();

        float anisotropy = Math.Clamp(
            descriptor.MaxAnisotropy,
            1,
            (int)_device.Selection.Properties.Limits.MaxSamplerAnisotropy);

        SamplerCreateInfo info = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = VulkanRhiTranslation.Filter(descriptor.MagFilter),
            MinFilter = VulkanRhiTranslation.Filter(descriptor.MinFilter),
            MipmapMode = VulkanRhiTranslation.MipmapFilter(descriptor.MipFilter),
            AddressModeU = VulkanRhiTranslation.AddressMode(descriptor.AddressU),
            AddressModeV = VulkanRhiTranslation.AddressMode(descriptor.AddressV),
            AddressModeW = VulkanRhiTranslation.AddressMode(descriptor.AddressW),
            MinLod = descriptor.LodMin,
            MaxLod = descriptor.LodMax,
            MipLodBias = _device.Capabilities.SamplerMipLodBias ? descriptor.MipLodBias : 0f,
            AnisotropyEnable = anisotropy > 1f,
            MaxAnisotropy = anisotropy,
            CompareEnable = descriptor.Compare is not null,
            CompareOp = descriptor.Compare is { } compare
                ? VulkanRhiTranslation.Compare(compare)
                : CompareOp.Never,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device.Device, &info, null, &sampler), "vkCreateSampler");
        return new VulkanRhiSampler(_vk, _device.Device, sampler, descriptor.Label);
    }

    /// <inheritdoc/>
    public IRhiShaderModule CreateShaderModule(in RhiShaderModuleDescriptor descriptor)
    {
        ThrowIfDisposed();

        ShaderVariant variant = string.IsNullOrEmpty(descriptor.Variant.Name)
            ? ShaderVariant.Default
            : descriptor.Variant;

        ReadOnlySpan<byte> spirv = ShaderCatalog.GetSpirv(descriptor.Module, variant).Span;

        fixed (byte* code = spirv)
        {
            ShaderModuleCreateInfo info = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code,
            };

            ShaderModule module;
            VkCheck.Ok(_vk.CreateShaderModule(_device.Device, &info, null, &module), "vkCreateShaderModule");

            return new VulkanRhiShaderModule(
                _vk,
                _device.Device,
                module,
                descriptor.Label.Length > 0 ? descriptor.Label : descriptor.Module.ToString());
        }
    }

    /// <inheritdoc/>
    public IRhiBindGroupLayout CreateBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        DescriptorSetLayoutBinding[] bindings = new DescriptorSetLayoutBinding[descriptor.Entries.Count];

        for (int i = 0; i < bindings.Length; i++)
        {
            RhiBindGroupLayoutEntry entry = descriptor.Entries[i];
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)entry.Binding,
                DescriptorType = VulkanRhiTranslation.BindingType(entry.Type, entry.HasDynamicOffset),
                DescriptorCount = 1,
                StageFlags = VulkanRhiTranslation.ShaderStages(entry.Visibility),
            };
        }

        fixed (DescriptorSetLayoutBinding* first = bindings)
        {
            DescriptorSetLayoutCreateInfo info = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = bindings.Length > 0 ? first : null,
            };

            DescriptorSetLayout layout;
            VkCheck.Ok(
                _vk.CreateDescriptorSetLayout(_device.Device, &info, null, &layout),
                "vkCreateDescriptorSetLayout");

            return new VulkanRhiBindGroupLayout(_vk, _device.Device, layout, descriptor);
        }
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout CreatePipelineLayout(RhiPipelineLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        if (descriptor.PushDataBytes > Limits.MaxPushDataBytes)
        {
            throw new GraphicsDeviceException(
                $"The layout '{descriptor.Label}' wants {descriptor.PushDataBytes} bytes of push data and "
                + $"this device offers {Limits.MaxPushDataBytes}.");
        }

        DescriptorSetLayout[] sets = new DescriptorSetLayout[descriptor.BindGroupLayouts.Count];
        for (int i = 0; i < sets.Length; i++)
        {
            sets[i] = ((VulkanRhiBindGroupLayout)descriptor.BindGroupLayouts[i]).Handle;
        }

        PushConstantRange push = new()
        {
            StageFlags = VulkanRhiTranslation.ShaderStages(descriptor.PushDataVisibility),
            Offset = 0,
            Size = (uint)descriptor.PushDataBytes,
        };

        fixed (DescriptorSetLayout* first = sets)
        {
            PipelineLayoutCreateInfo info = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint)sets.Length,
                PSetLayouts = sets.Length > 0 ? first : null,
                PushConstantRangeCount = descriptor.PushDataBytes > 0 ? 1u : 0u,
                PPushConstantRanges = descriptor.PushDataBytes > 0 ? &push : null,
            };

            PipelineLayout layout;
            VkCheck.Ok(_vk.CreatePipelineLayout(_device.Device, &info, null, &layout), "vkCreatePipelineLayout");
            return new VulkanRhiPipelineLayout(_vk, _device.Device, layout, descriptor);
        }
    }

    /// <inheritdoc/>
    public IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        using Utf8String entryPoint = new(descriptor.Compute.EntryPoint);
        using VulkanSpecialization constants = new(descriptor.Compute.Constants);

        SpecializationInfo specialization;

        ComputePipelineCreateInfo info = new()
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Layout = ((VulkanRhiPipelineLayout)descriptor.Layout).Handle,
            Stage = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = ((VulkanRhiShaderModule)descriptor.Compute.Module).Handle,
                PName = entryPoint.Pointer,
                PSpecializationInfo = constants.Describe(&specialization),
            },
        };

        Pipeline pipeline;
        VkCheck.Ok(
            _vk.CreateComputePipelines(_device.Device, _device.PipelineCache, 1, &info, null, &pipeline),
            "vkCreateComputePipelines");

        return new VulkanRhiComputePipeline(_vk, _device.Device, pipeline, descriptor.Layout, descriptor.Label);
    }

    /// <inheritdoc/>
    public IRhiQuerySet CreateQuerySet(in RhiQuerySetDescriptor descriptor)
    {
        ThrowIfDisposed();

        if (descriptor.Type == RhiQueryType.Timestamp && !Features.TimestampQuery)
        {
            throw new GraphicsDeviceException(
                "This device does not offer timestamp queries, so a stage cannot be timed on it.");
        }

        QueryPoolCreateInfo info = new()
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = VulkanRhiTranslation.QueryType(descriptor.Type),
            QueryCount = (uint)descriptor.Count,
        };

        QueryPool pool;
        VkCheck.Ok(_vk.CreateQueryPool(_device.Device, &info, null, &pool), "vkCreateQueryPool");
        return new VulkanRhiQuerySet(_vk, _device.Device, pool, descriptor);
    }

    /// <inheritdoc/>
    public void WriteBuffer<T>(IRhiBuffer buffer, ulong offset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        VulkanRhiBuffer target = (VulkanRhiBuffer)buffer;

        if (target.Buffer.HostVisible)
        {
            target.Buffer.WriteAt(offset, data);
            return;
        }

        // Queued ahead of the next submission rather than sent and waited on: see VulkanRhiDevice.Submission.
        QueueUpload(target.Buffer.Handle, offset, System.Runtime.InteropServices.MemoryMarshal.AsBytes(data));
    }

    /// <inheritdoc/>
    public void WriteTexture<T>(
        in RhiTextureRegion destination,
        ReadOnlySpan<T> data,
        int bytesPerRow,
        in RhiExtent3D size)
        where T : unmanaged
    {
        ThrowIfDisposed();

        VulkanRhiTexture texture = (VulkanRhiTexture)destination.Texture;
        RhiTextureRegion region = destination;

        BufferImageCopy copy = new()
        {
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = texture.Aspects,
                MipLevel = (uint)region.MipLevel,
                BaseArrayLayer = (uint)region.Origin.Z,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D(region.Origin.X, region.Origin.Y, 0),
            ImageExtent = new Extent3D(
                (uint)size.Width,
                (uint)size.Height,
                (uint)(texture.Dimension == RhiTextureViewDimension.Texture3D ? size.DepthOrArrayLayers : 1)),
        };

        // Queued ahead of the next submission rather than sent and waited on, as a buffer write is.
        QueueTextureUpload(texture, System.Runtime.InteropServices.MemoryMarshal.AsBytes(data), copy, bytesPerRow);
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ThrowIfDisposed();
        FlushUploads();
        _vk.DeviceWaitIdle(_device.Device);
        ReleaseFinishedLoose();

        // Everything submitted has finished; only a frame still being recorded can name a set now.
        ReleaseFinishedSets(all: !_inFrame);
    }

    /// <inheritdoc/>
    public string DescribeMemory()
    {
        int liveSets = 0;
        foreach (DescriptorPoolState pool in _pools)
        {
            liveSets += pool.Live;
        }

        return string.Join(
            '\n',
            "Vulkan heaps:",
            _device.DescribeMemoryBudget(),
            "Vulkan allocations:",
            _allocator.Describe(),
            FormattableString.Invariant(
                $"  descriptor pools: {_pools.Count}, {liveSets} live set(s), {_setFrees.Count} waiting to be freed"),
            FormattableString.Invariant(
                $"  upload staging: {_freeStaging.Count} spare chunk(s), {_loose.Count} loose submission(s) in flight"));
    }

    /// <inheritdoc/>
    public void TrimMemory()
    {
        ThrowIfDisposed();

        // The spare staging chunks first, so the allocator's trim sees them as freed blocks.
        foreach (VulkanBuffer chunk in _freeStaging)
        {
            chunk.Dispose();
        }

        _freeStaging.Clear();
        ReleaseFinishedSets();
        _allocator.TrimHostVisible();
        _allocator.TrimDeviceLocal(TrimmedDevicePoolBytes);
    }

    /// <summary>How much freed device memory a trim keeps for the next load to reuse.</summary>
    private const long TrimmedDevicePoolBytes = 64L * 1024 * 1024;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DeviceWaitIdle(_device.Device);

        _accelerationStructures?.Dispose();
        DestroySubmissionState();
        DestroyFrameSync();

        foreach (DescriptorPoolState pool in _pools)
        {
            _vk.DestroyDescriptorPool(_device.Device, pool.Handle, null);
        }

        _pools.Clear();
        _setFrees.Clear();
    }

    /// <summary>Puts a texture into a layout, from whatever it was in.</summary>
    /// <param name="cmd">The command buffer to record the barrier into.</param>
    /// <param name="texture">The texture.</param>
    /// <param name="layout">The layout it must be in.</param>
    /// <remarks>
    /// The whole of what the RHI's "no barriers" promise costs, in one place. Conservative on purpose:
    /// the stages are the widest that could apply, because a narrower pair would have to know what the
    /// caller is about to do and the interface deliberately does not say. Narrowing it is an
    /// optimisation with a correctness risk, so it waits for a measurement that asks for it.
    /// </remarks>
    internal void Transition(CommandBuffer cmd, VulkanRhiTexture texture, ImageLayout layout)
    {
        if (texture.Layout == layout)
        {
            return;
        }

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = texture.Layout,
            NewLayout = layout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = texture.Image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = texture.Aspects,
                BaseMipLevel = 0,
                LevelCount = (uint)texture.MipLevelCount,
                BaseArrayLayer = 0,
                LayerCount = (uint)texture.Layers,
            },
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };

        _vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);

        texture.Layout = layout;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
