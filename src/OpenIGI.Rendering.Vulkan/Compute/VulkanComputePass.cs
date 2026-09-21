using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Compute;

/// <summary>What a binding in a compute pass holds.</summary>
internal enum ComputeBindingKind
{
    /// <summary>An image read through a sampler, in the layout optimal for reading.</summary>
    SampledImage,

    /// <summary>
    /// An image read through a sampler that is also written as a storage image elsewhere, and so is kept
    /// in the general layout rather than moved between layouts around every use.
    /// </summary>
    /// <remarks>
    /// The general layout is legal for both and costs a little bandwidth on some hardware. For a lookup
    /// table written once and read a handful of times a frame that is a better trade than a pair of
    /// barriers around every access.
    /// </remarks>
    SampledStorageImage,

    /// <summary>An image written directly, always in the general layout.</summary>
    StorageImage,

    /// <summary>A three-dimensional image written directly.</summary>
    StorageVolume,

    /// <summary>The scene's acceleration structure, for a pass that traces rays against it.</summary>
    AccelerationStructure,

    /// <summary>
    /// A buffer of records read by index, for a pass that walks a structure the engine built.
    /// </summary>
    /// <remarks>
    /// Read-only in practice — nothing here writes one — but bound as a storage buffer all the same,
    /// because the alternative is a uniform buffer whose size a driver may cap at sixteen kilobytes and
    /// these run to megabytes.
    /// </remarks>
    StorageBuffer,
}

/// <summary>One resource bound for a dispatch.</summary>
/// <param name="View">The image view, for an image binding.</param>
/// <param name="Structure">The acceleration structure, for a binding that traces.</param>
/// <param name="Buffer">The buffer, for a storage-buffer binding.</param>
/// <param name="Bytes">How much of that buffer the shader may read. Zero binds the whole of it.</param>
/// <param name="Sampler">
/// A sampler for this binding alone, or nothing to take the pass's own.
/// <para>
/// The pass's sampler clamps, because almost everything it reads is a table whose edges are the extremes
/// of what it encodes — the horizon, the zenith, the top of the atmosphere — and wrapping one of those
/// fetches the opposite extreme. A WORLD TEXTURE is the exception and needs the opposite: its UVs run
/// well past one along a wall or around a tank, and clamping them was leaving a seam at every repeat.
/// </para>
/// </param>
internal readonly record struct ComputeResource(
    ImageView View,
    AccelerationStructureKHR Structure = default,
    Buffer Buffer = default,
    ulong Bytes = 0,
    Sampler Sampler = default)
{
    /// <summary>Wraps an acceleration structure as a bindable resource.</summary>
    /// <param name="structure">The structure to bind.</param>
    /// <returns>The resource.</returns>
    public static ComputeResource Scene(AccelerationStructureKHR structure) => new(default, structure);

    /// <summary>Wraps a buffer of records as a bindable resource.</summary>
    /// <param name="buffer">The buffer to bind.</param>
    /// <param name="bytes">How much of it the shader may read.</param>
    /// <returns>The resource.</returns>
    public static ComputeResource Records(Buffer buffer, ulong bytes) => new(default, default, buffer, bytes);
}

/// <summary>
/// A compute pass the backend owns outright: one shader, a fixed set of image bindings and a block of
/// parameters, with a descriptor set and parameter buffer per frame in flight.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="VulkanComputeShader"/>, which exists to run the <em>engine's</em> compute
/// shaders through the renderer seam and therefore has to cope with shaders written in the OpenGL
/// dialect, bound by unit, and declared without layouts. Nothing of that applies here: these shaders are
/// written for this backend, with explicit bindings, and the pass knows exactly what it binds. Keeping
/// them apart means neither has to carry the other's compromises.
/// </para>
/// <para>
/// The per-frame duplication is what makes a pass safe to record every frame without stalling. A
/// descriptor set written while a frame that uses it is still in flight is a race; one set per frame in
/// flight, cycled by the frame loop, means the set being written was last used by a frame whose fence has
/// already been waited on.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanComputePass : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;

    // The device's own pipeline cache, copied here because this class keeps the raw handle
    // rather than the wrapper. Passing it is what lets the driver return a previous run's
    // compiled pipeline instead of building this one again.
    private readonly PipelineCache _pipelineCache;
    private readonly VulkanShaderModule _module;
    private readonly DescriptorSetLayout _setLayout;
    private readonly PipelineLayout _pipelineLayout;
    private readonly Pipeline _pipeline;
    private readonly DescriptorPool _pool;
    private readonly DescriptorSet[] _sets;
    private readonly VulkanBuffer[] _params;
    private readonly ComputeBindingKind[] _bindings;
    private readonly Sampler _sampler;
    private readonly int _paramSize;
    private readonly int _frames;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanComputePass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="allocator">The allocator backing the parameter buffers.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="name">A name for the shader, used in compiler diagnostics.</param>
    /// <param name="source">The GLSL compute source.</param>
    /// <param name="bindings">
    /// What each binding holds, in binding order starting at zero. The parameter block is bound after
    /// them, so a pass with three image bindings finds its parameters at binding three.
    /// </param>
    /// <param name="parameterBytes">How large the parameter block is. Zero for a pass that takes none.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <exception cref="GraphicsDeviceException">The pass could not be created.</exception>
    public VulkanComputePass(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ShaderCompiler compiler,
        string name,
        string source,
        ReadOnlySpan<ComputeBindingKind> bindings,
        int parameterBytes,
        int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(compiler);

        _vk = vk;
        _device = device.Device;
        _pipelineCache = device.PipelineCache;
        _bindings = bindings.ToArray();
        _paramSize = parameterBytes;
        _frames = Math.Max(1, framesInFlight);

        _module = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(source, ShaderStage.Compute, name),
            ShaderStage.Compute);

        _sampler = CreateSampler();
        _setLayout = CreateSetLayout();
        _pipelineLayout = CreatePipelineLayout();
        _pipeline = CreatePipeline(name);
        _pool = CreatePool();

        _sets = new DescriptorSet[_frames];
        _params = new VulkanBuffer[_frames];
        for (int i = 0; i < _frames; i++)
        {
            _sets[i] = AllocateSet();
            if (_paramSize > 0)
            {
                _params[i] = new VulkanBuffer(
                    vk,
                    device,
                    allocator,
                    (ulong)_paramSize,
                    BufferUsageFlags.UniformBufferBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            }
        }
    }

    /// <summary>Gets the sampler the pass reads its sampled images through.</summary>
    /// <remarks>Linear and clamped, which is what every lookup table this backend samples wants.</remarks>
    public Sampler Sampler => _sampler;

    /// <summary>Records a dispatch, writing the descriptors and parameters for this frame first.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <param name="resources">The resources to bind, in binding order.</param>
    /// <param name="parameters">The parameter block. Must match the size the pass was created with.</param>
    /// <param name="groupsX">Work groups on the first axis.</param>
    /// <param name="groupsY">Work groups on the second axis.</param>
    /// <param name="groupsZ">Work groups on the third axis.</param>
    public void Record(
        CommandBuffer cmd,
        int frame,
        ReadOnlySpan<ComputeResource> resources,
        ReadOnlySpan<float> parameters,
        uint groupsX,
        uint groupsY = 1,
        uint groupsZ = 1)
    {
        if (resources.Length != _bindings.Length)
        {
            throw new GraphicsDeviceException(
                $"The pass binds {_bindings.Length} resources but {resources.Length} were given.");
        }

        if (_paramSize > 0)
        {
            _params[frame].Write(parameters);
        }

        WriteSet(frame, resources);

        DescriptorSet set = _sets[frame];
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _pipeline);
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _pipelineLayout, 0, 1, &set, 0, null);
        _vk.CmdDispatch(cmd, groupsX, groupsY, groupsZ);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (VulkanBuffer? buffer in _params)
        {
            buffer?.Dispose();
        }

        _vk.DestroyDescriptorPool(_device, _pool, null);
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
        _vk.DestroySampler(_device, _sampler, null);
        _module.Dispose();
    }

    private static DescriptorType TypeOf(ComputeBindingKind kind) => kind switch
    {
        ComputeBindingKind.SampledImage or ComputeBindingKind.SampledStorageImage
            => DescriptorType.CombinedImageSampler,
        ComputeBindingKind.AccelerationStructure => DescriptorType.AccelerationStructureKhr,
        ComputeBindingKind.StorageBuffer => DescriptorType.StorageBuffer,
        _ => DescriptorType.StorageImage,
    };

    private void WriteSet(int frame, ReadOnlySpan<ComputeResource> resources)
    {
        int count = _bindings.Length + (_paramSize > 0 ? 1 : 0);
        DescriptorImageInfo* images = stackalloc DescriptorImageInfo[Math.Max(1, _bindings.Length)];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[Math.Max(1, count)];
        AccelerationStructureKHR* structures = stackalloc AccelerationStructureKHR[Math.Max(1, _bindings.Length)];
        WriteDescriptorSetAccelerationStructureKHR* sceneInfos =
            stackalloc WriteDescriptorSetAccelerationStructureKHR[Math.Max(1, _bindings.Length)];
        DescriptorBufferInfo* buffers = stackalloc DescriptorBufferInfo[Math.Max(1, _bindings.Length)];

        for (int i = 0; i < _bindings.Length; i++)
        {
            ComputeBindingKind kind = _bindings[i];
            if (kind == ComputeBindingKind.StorageBuffer)
            {
                buffers[i] = new DescriptorBufferInfo
                {
                    Buffer = resources[i].Buffer,
                    Offset = 0,

                    // Whole-size rather than the byte count when none was given: a descriptor's range may
                    // not be zero, and a pass that does not care how long its records are should not have
                    // to find out.
                    Range = resources[i].Bytes > 0 ? resources[i].Bytes : Vk.WholeSize,
                };
                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _sets[frame],
                    DstBinding = (uint)i,
                    DescriptorType = DescriptorType.StorageBuffer,
                    DescriptorCount = 1,
                    PBufferInfo = &buffers[i],
                };
                continue;
            }

            if (kind == ComputeBindingKind.AccelerationStructure)
            {
                structures[i] = resources[i].Structure;
                sceneInfos[i] = new WriteDescriptorSetAccelerationStructureKHR
                {
                    SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
                    AccelerationStructureCount = 1,
                    PAccelerationStructures = &structures[i],
                };
                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    PNext = &sceneInfos[i],
                    DstSet = _sets[frame],
                    DstBinding = (uint)i,
                    DescriptorType = DescriptorType.AccelerationStructureKhr,
                    DescriptorCount = 1,
                };
                continue;
            }

            bool sampled = kind is ComputeBindingKind.SampledImage or ComputeBindingKind.SampledStorageImage;
            images[i] = new DescriptorImageInfo
            {
                // The resource's own sampler where it brought one, and the pass's otherwise. See
                // ComputeResource.Sampler for why a world texture cannot share the pass's.
                Sampler = sampled
                    ? (resources[i].Sampler.Handle != 0 ? resources[i].Sampler : _sampler)
                    : default,
                ImageView = resources[i].View,
                ImageLayout = kind == ComputeBindingKind.SampledImage
                    ? ImageLayout.ShaderReadOnlyOptimal
                    : ImageLayout.General,
            };

            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _sets[frame],
                DstBinding = (uint)i,
                DescriptorType = TypeOf(_bindings[i]),
                DescriptorCount = 1,
                PImageInfo = &images[i],
            };
        }

        DescriptorBufferInfo paramInfo = default;
        if (_paramSize > 0)
        {
            paramInfo = new DescriptorBufferInfo
            {
                Buffer = _params[frame].Handle,
                Offset = 0,
                Range = (ulong)_paramSize,
            };
            writes[_bindings.Length] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _sets[frame],
                DstBinding = (uint)_bindings.Length,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &paramInfo,
            };
        }

        _vk.UpdateDescriptorSets(_device, (uint)count, writes, 0, null);
    }

    private DescriptorSetLayout CreateSetLayout()
    {
        int count = _bindings.Length + (_paramSize > 0 ? 1 : 0);
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[Math.Max(1, count)];

        for (int i = 0; i < _bindings.Length; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)i,
                DescriptorType = TypeOf(_bindings[i]),
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        }

        if (_paramSize > 0)
        {
            bindings[_bindings.Length] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)_bindings.Length,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        }

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)count,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(
            _vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout (compute pass)");
        return layout;
    }

    private PipelineLayout CreatePipelineLayout()
    {
        DescriptorSetLayout layout = _setLayout;
        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &layout,
        };

        PipelineLayout pipelineLayout;
        VkCheck.Ok(
            _vk.CreatePipelineLayout(_device, &createInfo, null, &pipelineLayout),
            "vkCreatePipelineLayout (compute pass)");
        return pipelineLayout;
    }

    private Pipeline CreatePipeline(string name)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main");
        try
        {
            ComputePipelineCreateInfo createInfo = new()
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = _module.Handle,
                    PName = (byte*)entryPoint,
                },
                Layout = _pipelineLayout,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateComputePipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline),
                $"vkCreateComputePipelines ({name})");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }

    private DescriptorPool CreatePool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[5];
        uint sampled = 0;
        uint storage = 0;
        uint scenes = 0;
        uint records = 0;
        foreach (ComputeBindingKind kind in _bindings)
        {
            if (kind is ComputeBindingKind.SampledImage or ComputeBindingKind.SampledStorageImage)
            {
                sampled++;
            }
            else if (kind == ComputeBindingKind.AccelerationStructure)
            {
                scenes++;
            }
            else if (kind == ComputeBindingKind.StorageBuffer)
            {
                records++;
            }
            else
            {
                storage++;
            }
        }

        uint count = 0;
        if (sampled > 0)
        {
            sizes[count++] = new DescriptorPoolSize
            {
                Type = DescriptorType.CombinedImageSampler,
                DescriptorCount = sampled * (uint)_frames,
            };
        }

        if (storage > 0)
        {
            sizes[count++] = new DescriptorPoolSize
            {
                Type = DescriptorType.StorageImage,
                DescriptorCount = storage * (uint)_frames,
            };
        }

        if (scenes > 0)
        {
            sizes[count++] = new DescriptorPoolSize
            {
                Type = DescriptorType.AccelerationStructureKhr,
                DescriptorCount = scenes * (uint)_frames,
            };
        }

        if (records > 0)
        {
            sizes[count++] = new DescriptorPoolSize
            {
                Type = DescriptorType.StorageBuffer,
                DescriptorCount = records * (uint)_frames,
            };
        }

        if (_paramSize > 0)
        {
            sizes[count++] = new DescriptorPoolSize
            {
                Type = DescriptorType.UniformBuffer,
                DescriptorCount = (uint)_frames,
            };
        }

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = (uint)_frames,
            PoolSizeCount = count,
            PPoolSizes = sizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(
            _vk.CreateDescriptorPool(_device, &createInfo, null, &pool),
            "vkCreateDescriptorPool (compute pass)");
        return pool;
    }

    private DescriptorSet AllocateSet()
    {
        DescriptorSetLayout layout = _setLayout;
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };

        DescriptorSet set;
        VkCheck.Ok(
            _vk.AllocateDescriptorSets(_device, &allocInfo, &set),
            "vkAllocateDescriptorSets (compute pass)");
        return set;
    }

    private Sampler CreateSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,

            // Clamped rather than wrapped. Every table sampled here is parameterised so that its edges are
            // the extremes of the quantity it encodes — the horizon, the zenith, the top of the atmosphere
            // — and wrapping at one of those would fetch the opposite extreme.
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler (compute pass)");
        return sampler;
    }
}
