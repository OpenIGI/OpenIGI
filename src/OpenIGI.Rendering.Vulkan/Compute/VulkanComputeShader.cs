using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Compute;

/// <summary>
/// A compiled compute program: its pipeline, the descriptor-set layout its resources imply, and the
/// per-dispatch descriptor sets and globals buffers a run of it binds.
/// </summary>
/// <remarks>
/// <para>
/// The source is rewritten by <see cref="ComputeShaderAnalysis"/> so a Vulkan target accepts it, compiled
/// to SPIR-V, and reflected into a descriptor-set layout built from the storage buffers, images and
/// samplers it declares plus the one uniform block its scalar parameters were gathered into. The pipeline
/// is created once and lives for the program's life.
/// </para>
/// <para>
/// <b>A dispatch needs its own descriptor set and globals buffer</b> because the backend records a frame's
/// compute into that frame's command buffer and submits it once — so several dispatches of one program are
/// in flight together and cannot share the one set. Each frame-in-flight keeps its own <em>ring</em> of
/// instances, handed out in order and reset when the frame loop hands that slot back (its fence having been
/// waited on, so the instances it held are free). The ring grows to the peak dispatch count a frame reached
/// and is reused from then on, so steady state allocates nothing. A separate immediate instance serves the
/// load-time path, where a dispatch is submitted and waited on its own and one set suffices.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanComputeShader : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly Pipeline _pipeline;
    private readonly PipelineLayout _pipelineLayout;
    private readonly DescriptorSetLayout _setLayout;
    private readonly List<DescriptorPool> _pools = [];
    private readonly Dictionary<string, ComputeShaderAnalysis.Uniform> _uniforms;
    private readonly HashSet<string> _samplerNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _samplerUnits = new(StringComparer.Ordinal);
    private readonly byte[] _globalsShadow;
    private readonly int _globalsSize;
    private readonly List<Instance>[] _ring = [[], []];
    private readonly int[] _used = new int[2];
    private Instance? _immediate;
    private DescriptorPool _currentPool;
    private int _currentPoolRemaining;
    private bool _disposed;

    /// <summary>One dispatch's descriptor set and the globals buffer it points at.</summary>
    internal sealed record Instance(DescriptorSet Set, VulkanBuffer? Globals);

    /// <summary>Initialises a new instance of the <see cref="VulkanComputeShader"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the program lives on.</param>
    /// <param name="allocator">The allocator backing the globals buffers.</param>
    /// <param name="compiler">The GLSL-to-SPIR-V compiler.</param>
    /// <param name="source">The GL-dialect compute source.</param>
    /// <param name="name">The program's name, for diagnostics.</param>
    /// <exception cref="GraphicsDeviceException">The source did not compile or the pipeline could not be built.</exception>
    public VulkanComputeShader(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ShaderCompiler compiler,
        string source,
        string name)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        Name = name;

        ComputeShaderAnalysis.Analysis analysis = ComputeShaderAnalysis.Analyse(source, name);
        Resources = analysis.Resources;
        UniformBinding = analysis.UniformBlockBinding;
        _uniforms = new Dictionary<string, ComputeShaderAnalysis.Uniform>(analysis.Uniforms, StringComparer.Ordinal);
        foreach (ComputeShaderAnalysis.Resource resource in analysis.Resources)
        {
            if (resource.ByUnit)
            {
                _samplerNames.Add(resource.Name);
            }
        }
        _globalsSize = analysis.UniformBlockSize;
        _globalsShadow = _globalsSize > 0 ? new byte[_globalsSize] : [];

        byte[] spirv = compiler.Compile(analysis.Source, ShaderStage.Compute, name);
        _setLayout = CreateSetLayout(analysis);
        (_pipeline, _pipelineLayout) = CreatePipeline(spirv);
    }

    /// <summary>Gets the program's name.</summary>
    public string Name { get; }

    /// <summary>Gets the resources the program binds, by binding number.</summary>
    public IReadOnlyList<ComputeShaderAnalysis.Resource> Resources { get; }

    /// <summary>Gets the pipeline to bind before dispatching.</summary>
    public Pipeline Pipeline => _pipeline;

    /// <summary>Gets the pipeline layout the descriptor set is bound through.</summary>
    public PipelineLayout PipelineLayout => _pipelineLayout;

    /// <summary>Gets the binding the gathered globals block sits at, or -1 when there are none.</summary>
    public int UniformBinding { get; }

    /// <summary>Sets one gathered scalar parameter from its raw bytes.</summary>
    /// <param name="uniform">The parameter's name.</param>
    /// <param name="bytes">Its value, in the shader's own byte layout.</param>
    public void SetUniform(string uniform, ReadOnlySpan<byte> bytes)
    {
        if (!_uniforms.TryGetValue(uniform, out ComputeShaderAnalysis.Uniform slot))
        {
            return;
        }

        int count = Math.Min(bytes.Length, slot.Size);
        bytes[..count].CopyTo(_globalsShadow.AsSpan(slot.Offset, count));
    }

    /// <summary>Records the texture unit a default-block sampler was set to, if the name is one.</summary>
    /// <param name="name">The sampler's name.</param>
    /// <param name="unit">The texture unit it should sample.</param>
    /// <returns><see langword="true"/> if the name is a sampler and was recorded; otherwise <see langword="false"/>.</returns>
    public bool TrySetSamplerUnit(string name, int unit)
    {
        if (!_samplerNames.Contains(name))
        {
            return false;
        }

        _samplerUnits[name] = unit;
        return true;
    }

    /// <summary>Gets the texture unit a sampler was last set to, or zero if it never was.</summary>
    /// <param name="name">The sampler's name.</param>
    /// <returns>The unit.</returns>
    public int SamplerUnit(string name) => _samplerUnits.GetValueOrDefault(name, 0);

    /// <summary>Resets one frame-in-flight's instance ring, freeing the instances it held for reuse.</summary>
    /// <param name="slot">The frame-in-flight index.</param>
    public void ResetFrame(int slot) => _used[slot] = 0;

    /// <summary>Hands out an instance for the next dispatch, uploading the current globals into it.</summary>
    /// <param name="inFrame">True to draw from the given frame slot's ring; false for the immediate instance.</param>
    /// <param name="slot">The frame-in-flight index, when <paramref name="inFrame"/> is set.</param>
    /// <returns>The instance to write descriptors into and bind.</returns>
    public Instance Acquire(bool inFrame, int slot)
    {
        Instance instance;
        if (inFrame)
        {
            List<Instance> ring = _ring[slot];
            if (_used[slot] == ring.Count)
            {
                ring.Add(CreateInstance());
            }

            instance = ring[_used[slot]++];
        }
        else
        {
            instance = _immediate ??= CreateInstance();
        }

        instance.Globals?.Write<byte>(_globalsShadow);
        return instance;
    }

    /// <summary>Gets the globals buffer's size in bytes, for the uniform-block descriptor's range.</summary>
    public ulong GlobalsSize => (ulong)_globalsSize;

    private Instance CreateInstance()
    {
        DescriptorSet set = AllocateSet();
        VulkanBuffer? globals = _globalsSize > 0
            ? new VulkanBuffer(
                _vk,
                _device,
                _allocator,
                (ulong)_globalsSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
            : null;
        return new Instance(set, globals);
    }

    private DescriptorSetLayout CreateSetLayout(ComputeShaderAnalysis.Analysis analysis)
    {
        int count = analysis.Resources.Count + (analysis.UniformBlockBinding >= 0 ? 1 : 0);
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[Math.Max(1, count)];
        int index = 0;

        if (analysis.UniformBlockBinding >= 0)
        {
            bindings[index++] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)analysis.UniformBlockBinding,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        }

        foreach (ComputeShaderAnalysis.Resource resource in analysis.Resources)
        {
            bindings[index++] = new DescriptorSetLayoutBinding
            {
                Binding = resource.Binding,
                DescriptorType = ToDescriptorType(resource.Kind),
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        }

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)count,
            PBindings = count > 0 ? bindings : null,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device.Device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout");
        return layout;
    }

    private DescriptorSet AllocateSet()
    {
        if (_pools.Count == 0 || _currentPoolRemaining == 0)
        {
            GrowPool();
        }

        DescriptorSetLayout layout = _setLayout;
        DescriptorPool pool = _currentPool;
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };

        DescriptorSet set;
        Result result = _vk.AllocateDescriptorSets(_device.Device, &allocInfo, &set);
        if (result == Result.ErrorOutOfPoolMemory || result == Result.ErrorFragmentedPool)
        {
            GrowPool();
            pool = _currentPool;
            allocInfo.DescriptorPool = pool;
            result = _vk.AllocateDescriptorSets(_device.Device, &allocInfo, &set);
        }

        VkCheck.Ok(result, "vkAllocateDescriptorSets (compute)");
        _currentPoolRemaining--;
        return set;
    }

    private void GrowPool()
    {
        const uint setsPerPool = 64;
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[4];
        int used = 0;
        used += AddSize(sizes, used, DescriptorType.UniformBuffer, UniformBinding >= 0 ? (int)setsPerPool : 0);
        used += AddSize(sizes, used, DescriptorType.StorageBuffer, CountKind(ComputeResourceKind.StorageBuffer) * (int)setsPerPool);
        used += AddSize(sizes, used, DescriptorType.StorageImage, CountKind(ComputeResourceKind.StorageImage) * (int)setsPerPool);
        used += AddSize(sizes, used, DescriptorType.CombinedImageSampler, CountKind(ComputeResourceKind.SampledImage) * (int)setsPerPool);
        if (used == 0)
        {
            sizes[used++] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = setsPerPool };
        }

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = setsPerPool,
            PoolSizeCount = (uint)used,
            PPoolSizes = sizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device.Device, &createInfo, null, &pool), "vkCreateDescriptorPool (compute)");
        _pools.Add(pool);
        _currentPool = pool;
        _currentPoolRemaining = (int)setsPerPool;
    }

    private int CountKind(ComputeResourceKind kind)
    {
        int count = 0;
        foreach (ComputeShaderAnalysis.Resource resource in Resources)
        {
            if (resource.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private static int AddSize(DescriptorPoolSize* sizes, int at, DescriptorType type, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        sizes[at] = new DescriptorPoolSize { Type = type, DescriptorCount = (uint)count };
        return 1;
    }

    private (Pipeline, PipelineLayout) CreatePipeline(byte[] spirv)
    {
        ShaderModule module = CreateModule(spirv);
        try
        {
            DescriptorSetLayout layout = _setLayout;
            PipelineLayoutCreateInfo layoutInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &layout,
            };

            PipelineLayout pipelineLayout;
            VkCheck.Ok(_vk.CreatePipelineLayout(_device.Device, &layoutInfo, null, &pipelineLayout), "vkCreatePipelineLayout");

            byte* entry = stackalloc byte[5];
            entry[0] = (byte)'m';
            entry[1] = (byte)'a';
            entry[2] = (byte)'i';
            entry[3] = (byte)'n';
            entry[4] = 0;

            ComputePipelineCreateInfo createInfo = new()
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = module,
                    PName = entry,
                },
                Layout = pipelineLayout,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateComputePipelines(_device.Device, default, 1, &createInfo, null, &pipeline),
                "vkCreateComputePipelines");
            return (pipeline, pipelineLayout);
        }
        finally
        {
            _vk.DestroyShaderModule(_device.Device, module, null);
        }
    }

    private ShaderModule CreateModule(byte[] spirv)
    {
        fixed (byte* code = spirv)
        {
            ShaderModuleCreateInfo createInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code,
            };

            ShaderModule module;
            VkCheck.Ok(_vk.CreateShaderModule(_device.Device, &createInfo, null, &module), "vkCreateShaderModule");
            return module;
        }
    }

    private static DescriptorType ToDescriptorType(ComputeResourceKind kind) => kind switch
    {
        ComputeResourceKind.StorageBuffer => DescriptorType.StorageBuffer,
        ComputeResourceKind.StorageImage => DescriptorType.StorageImage,
        _ => DescriptorType.CombinedImageSampler,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyPipeline(_device.Device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device.Device, _pipelineLayout, null);

        foreach (List<Instance> ring in _ring)
        {
            foreach (Instance instance in ring)
            {
                instance.Globals?.Dispose();
            }
        }

        _immediate?.Globals?.Dispose();

        foreach (DescriptorPool pool in _pools)
        {
            _vk.DestroyDescriptorPool(_device.Device, pool, null);
        }

        _vk.DestroyDescriptorSetLayout(_device.Device, _setLayout, null);
    }
}
