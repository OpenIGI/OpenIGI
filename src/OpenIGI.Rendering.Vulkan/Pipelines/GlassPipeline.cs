using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using ShaderStage = OpenIGI.Rendering.Vulkan.Shaders.ShaderStage;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// The glazing pipeline: window panes, drawn in the forward translucency subpass as tinted transmission
/// and recording each pane pixel into the layer the glass reflection pass reads.
/// </summary>
/// <remarks>
/// <para>
/// A second graphics pipeline on the same subpass the world's forward draws use, rather than another
/// state of that one, because it is a different SHAPE and not a different setting: it writes a storage
/// image the world pipeline knows nothing about, and its fragment stage needs a view position the world
/// vertex stage does not pass. Folding either into the shared pipeline would put the cost on every
/// forward draw in the game to serve the handful that are windows.
/// </para>
/// <para>
/// <b>Set 0 is the world's own, unchanged.</b> The diffuse, lightmap and detail trio is built per texture
/// pairing and cached by the device, and this pipeline is handed that same layout so a pairing built for
/// an opaque draw binds here without being rebuilt. Only set 1 is this pipeline's own, and it holds two
/// things: the glazing layer, and the handful of numbers the fragment stage cannot push.
/// </para>
/// <para>
/// <b>There is no acceleration structure here and no surface cache.</b> It carried both once, and they
/// have moved to the compute pass that runs after the render pass closes — see
/// <c>Source/Glazing/Reflect.slang</c> for why a reflection computed inside the render pass can
/// never show anything that moves.
/// </para>
/// </remarks>
internal sealed unsafe class GlassPipeline : IDisposable
{
    /// <summary>How many floats the parameter block holds: one vec4 of fog.</summary>
    public const int ParamFloats = 4;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PipelineCache _pipelineCache;
    private readonly RenderPass _renderPass;
    private readonly uint _subpass;
    private readonly int _frames;
    private readonly VulkanShaderModule _transmission;
    private readonly VulkanShaderModule _layer;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    private readonly DescriptorPool _pool;
    private readonly DescriptorSet[] _sets;
    private readonly VulkanBuffer[] _params;
    // Keyed by the culling rule AND by which pass it is for: the front layer writes depth and does not
    // blend, the transmission blends and does not write depth, and the two target different passes.
    private readonly Dictionary<(CullMode Cull, bool Layer), Pipeline> _pipelines = [];

    private readonly RenderPass _frontLayerPass;
    private readonly Sampler _depthSampler;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GlassPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="allocator">The allocator the parameter buffers come from.</param>
    /// <param name="renderPass">The render pass the pipeline is compatible with.</param>
    /// <param name="subpass">The translucency subpass index within it.</param>
    /// <param name="frontLayerPass">The pass that records which pane is nearest at each pixel.</param>
    /// <param name="depthSampler">The sampler the front-layer stage reads the scene depth through.</param>
    /// <param name="textureSetLayout">The world's set-0 layout, bound here unchanged.</param>
    /// <param name="framesInFlight">How many frames may be recording at once.</param>
    /// <exception cref="GraphicsDeviceException">A layout, shader or pool could not be created.</exception>
    public GlassPipeline(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        RenderPass renderPass,
        uint subpass,
        RenderPass frontLayerPass,
        Sampler depthSampler,
        DescriptorSetLayout textureSetLayout,
        int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _vk = vk;
        _device = device.Device;
        _pipelineCache = device.PipelineCache;
        _renderPass = renderPass;
        _subpass = subpass;
        _frontLayerPass = frontLayerPass;
        _depthSampler = depthSampler;
        _frames = framesInFlight;

        // Two modules rather than three: a module names one entry point per stage, and the two
        // glazing draws are two fragment stages over one vertex stage. Each module carries that same
        // shared vertex stage, which lives in Source/Common/GlassVertex.slang.
        ShaderReflection transmission = ShaderCatalog.GetReflection(ShaderModuleId.Glass);
        _vertexEntryPoint = transmission.EntryPointFor(OpenIGI.Shaders.ShaderStage.Vertex).Name;
        _fragmentEntryPoint = transmission.EntryPointFor(OpenIGI.Shaders.ShaderStage.Fragment).Name;
        _transmission = new VulkanShaderModule(
            vk,
            _device,
            ShaderCatalog.GetSpirv(ShaderModuleId.Glass).Span,
            ShaderStage.Vertex,
            _vertexEntryPoint);
        _layer = new VulkanShaderModule(
            vk,
            _device,
            ShaderCatalog.GetSpirv(ShaderModuleId.GlassLayer).Span,
            ShaderStage.Vertex,
            _vertexEntryPoint);

        GlassSetLayout = CreateGlassSetLayout();
        PipelineLayout = CreatePipelineLayout(textureSetLayout, GlassSetLayout);

        _params = new VulkanBuffer[_frames];
        for (int i = 0; i < _frames; i++)
        {
            _params[i] = new VulkanBuffer(
                vk,
                device,
                allocator,
                (ulong)(ParamFloats * sizeof(float)),
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                persistentlyMapped: true);
        }

        _pool = CreatePool();
        _sets = AllocateSets();
    }

    /// <summary>Gets the pipeline layout: the world's texture set, then this pipeline's own.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets the layout of set 1, the glazing layer and the parameters.</summary>
    public DescriptorSetLayout GlassSetLayout { get; }

    /// <summary>Gets set 1 for one frame in flight.</summary>
    /// <param name="frame">The frame's index.</param>
    /// <returns>The set, as last written by <see cref="Update"/>.</returns>
    public DescriptorSet GlassSet(int frame) => _sets[frame];

    /// <summary>Points set 1 at this frame's scene depth and parameters.</summary>
    /// <param name="frame">The frame in flight being recorded.</param>
    /// <param name="sceneDepth">
    /// The depth the opaque world wrote. Read by the front-layer pass, which tests against a depth buffer
    /// of its own and so has to ask about occlusion in the shader; the transmission pass declares it and
    /// never reads it, because the hardware already tested it against this very buffer.
    /// </param>
    /// <param name="parameters">The parameter block, exactly <see cref="ParamFloats"/> floats.</param>
    /// <exception cref="ArgumentException">The parameter block is the wrong length.</exception>
    public void Update(int frame, ImageView sceneDepth, ReadOnlySpan<float> parameters)
    {
        if (parameters.Length != ParamFloats)
        {
            throw new ArgumentException($"The glass parameters must be {ParamFloats} floats.", nameof(parameters));
        }

        _params[frame].Write(parameters);

        DescriptorImageInfo image = new()
        {
            Sampler = _depthSampler,
            ImageView = sceneDepth,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        DescriptorBufferInfo buffer = new()
        {
            Buffer = _params[frame].Handle,
            Offset = 0,
            Range = (ulong)(ParamFloats * sizeof(float)),
        };

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _sets[frame],
            DstBinding = 0,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            PImageInfo = &image,
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _sets[frame],
            DstBinding = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &buffer,
        };

        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
    }

    /// <summary>Gets, building if needed, the pipeline for one face-culling rule.</summary>
    /// <param name="cull">The culling rule the world is drawing with.</param>
    /// <param name="layer">
    /// Whether this is the front-layer variant: depth written, nothing blended, into the pass that owns
    /// its own depth. False is the transmission draw, which is the reverse of all three.
    /// </param>
    /// <returns>The matching pipeline.</returns>
    /// <remarks>
    /// Culling is the only state that varies. Glazing is always depth-tested without writing — it blends
    /// over a finished picture, so writing its depth would hide whatever is drawn behind it later — and
    /// always alpha-blended, which is the whole point of the pass.
    /// </remarks>
    public Pipeline ForCull(CullMode cull, bool layer = false)
    {
        if (_pipelines.TryGetValue((cull, layer), out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreatePipeline(cull, layer);
        _pipelines[(cull, layer)] = pipeline;
        return pipeline;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Pipeline pipeline in _pipelines.Values)
        {
            _vk.DestroyPipeline(_device, pipeline, null);
        }

        _pipelines.Clear();
        foreach (VulkanBuffer buffer in _params)
        {
            buffer.Dispose();
        }

        _vk.DestroyDescriptorPool(_device, _pool, null);
        _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, GlassSetLayout, null);
        _layer.Dispose();
        _transmission.Dispose();
    }

    private DescriptorSetLayout CreateGlassSetLayout()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(
            _vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout (glass)");
        return layout;
    }

    private DescriptorPool CreatePool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.SampledImage,
            DescriptorCount = (uint)_frames,
        };
        sizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = (uint)_frames,
        };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = (uint)_frames,
            PoolSizeCount = 2,
            PPoolSizes = sizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool (glass)");
        return pool;
    }

    private DescriptorSet[] AllocateSets()
    {
        DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[_frames];
        for (int i = 0; i < _frames; i++)
        {
            layouts[i] = GlassSetLayout;
        }

        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = (uint)_frames,
            PSetLayouts = layouts,
        };

        DescriptorSet[] sets = new DescriptorSet[_frames];
        fixed (DescriptorSet* target = sets)
        {
            VkCheck.Ok(
                _vk.AllocateDescriptorSets(_device, &allocateInfo, target),
                "vkAllocateDescriptorSets (glass)");
        }

        return sets;
    }

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout textures, DescriptorSetLayout glass)
    {
        DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[2] { textures, glass };
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = sizeof(float) * 32,
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
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (glass)");
        return layout;
    }

    private Pipeline CreatePipeline(CullMode cull, bool layer)
    {
        nint vertexEntryPoint = SilkMarshal.StringToPtr(_vertexEntryPoint, NativeStringEncoding.UTF8);
        nint fragmentEntryPoint = SilkMarshal.StringToPtr(_fragmentEntryPoint, NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = (layer ? _layer : _transmission).Handle,
                PName = (byte*)vertexEntryPoint,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = (layer ? _layer : _transmission).Handle,
                PName = (byte*)fragmentEntryPoint,
            };

            // The same two streams the world binds, because the glazing is drawn from the same vertex
            // buffers. The previous pose is declared and never read; a pipeline's attributes have to
            // describe the buffers it is fed whether or not its shader consults them.
            VertexInputBindingDescription* vertexBindings = stackalloc VertexInputBindingDescription[2];
            vertexBindings[0] = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };
            vertexBindings[1] = new VertexInputBindingDescription
            {
                Binding = 1,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };

            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[7];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 12 };
            attributes[2] = new VertexInputAttributeDescription { Location = 2, Binding = 0, Format = Format.R32G32Sfloat, Offset = 24 };
            attributes[3] = new VertexInputAttributeDescription { Location = 3, Binding = 0, Format = Format.R32G32Sfloat, Offset = 32 };
            attributes[4] = new VertexInputAttributeDescription { Location = 4, Binding = 0, Format = Format.R8G8B8A8Unorm, Offset = (uint)Vertex3D.ColourOffset };
            attributes[5] = new VertexInputAttributeDescription { Location = 5, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[6] = new VertexInputAttributeDescription { Location = 6, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 12 };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = vertexBindings,
                VertexAttributeDescriptionCount = 7,
                PVertexAttributeDescriptions = attributes,
            };

            PipelineInputAssemblyStateCreateInfo inputAssembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            PipelineViewportStateCreateInfo viewport = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };

            PipelineRasterizationStateCreateInfo rasterization = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = cull switch
                {
                    Rendering.CullMode.Clockwise => CullModeFlags.FrontBit,
                    Rendering.CullMode.CounterClockwise => CullModeFlags.BackBit,
                    _ => CullModeFlags.None,
                },
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1.0f,
            };

            PipelineMultisampleStateCreateInfo multisample = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            // Tested and not written. A pane blends over a picture that is already finished, so it must
            // not stop anything drawn after it: two windows in a line both have to show, and the second
            // is behind the first.
            //
            // Reverse-Z, as every other pipeline here: nearer geometry holds the GREATER depth, so the
            // test keeps the greater one. Less-or-equal rejects the whole level.
            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,

                // WRITTEN by the front layer and not by the transmission, and the difference is the
                // whole reason the front layer is a pass of its own. There the depth is private, so a
                // write costs nothing and is what makes the nearest pane win deterministically. Here it
                // is shared with the tracers, the muzzle flame and the smoke -- all drawn after the
                // glazing -- and a pane that wrote depth would occlude every one of them.
                DepthWriteEnable = layer,
                DepthCompareOp = CompareOp.GreaterOrEqual,
            };

            // Straight alpha, not premultiplied: the fragment stage writes the pane's own colour and its
            // opacity beside it, and the hardware does the mixing.
            PipelineColorBlendAttachmentState blendAttachment = new()
            {
                // The front layer does not blend: it records one pane's normal and distance, and mixing
                // that with another pane's would describe a surface that is not there.
                BlendEnable = !layer,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,

                // The lit target's alpha is the world/rig marker that later passes read, and a pane must
                // not disturb it: it keeps whatever the opaque draw beneath already wrote there.
                SrcAlphaBlendFactor = BlendFactor.Zero,
                DstAlphaBlendFactor = BlendFactor.One,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit
                    | ColorComponentFlags.GBit
                    | ColorComponentFlags.BBit
                    | ColorComponentFlags.ABit,
            };

            PipelineColorBlendStateCreateInfo colorBlend = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };

            DynamicState* dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            PipelineDynamicStateCreateInfo dynamic = new()
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };

            GraphicsPipelineCreateInfo createInfo = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewport,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamic,
                Layout = PipelineLayout,
                RenderPass = layer ? _frontLayerPass : _renderPass,
                Subpass = layer ? 0u : _subpass,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (glass)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(vertexEntryPoint);
            SilkMarshal.Free(fragmentEntryPoint);
        }
    }
}
