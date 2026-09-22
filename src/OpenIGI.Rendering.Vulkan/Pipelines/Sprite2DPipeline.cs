using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using ShaderStage = OpenIGI.Rendering.Vulkan.Shaders.ShaderStage;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// The pipeline every 2D draw is submitted through: one graphics pipeline per blend mode, sharing a
/// layout that binds a texture and takes the projection size as a push constant.
/// </summary>
/// <remarks>
/// <para>
/// Blend state is baked into a Vulkan pipeline rather than set at draw time, so the three blend modes
/// the 2D path uses are three pipelines built up front from the same shaders and the same layout. A
/// draw selects one by binding it; everything else — the vertex layout, the texture binding, the
/// dynamic viewport and scissor — is identical across them.
/// </para>
/// <para>
/// The projection size travels as a push constant rather than a uniform buffer because it is eight
/// bytes that change with the window: a push constant is the cheapest way to hand a shader a value that
/// small that often, with no descriptor to allocate or update.
/// </para>
/// </remarks>
internal sealed unsafe class Sprite2DPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly uint _fragmentPushOffset;
    private readonly uint _fragmentPushBytes;
    private readonly Device _device;

    // The device's own pipeline cache, copied here because this class keeps the raw handle
    // rather than the wrapper. Passing it is what lets the driver return a previous run's
    // compiled pipeline instead of building this one again.
    private readonly PipelineCache _pipelineCache;
    private readonly VulkanShaderModule _shader;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    private readonly Pipeline[] _pipelines;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="Sprite2DPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="renderPass">The render pass the pipelines are compatible with.</param>
    /// <exception cref="GraphicsDeviceException">A pipeline or its layout could not be created.</exception>
    public Sprite2DPipeline(Vk vk, VulkanDevice device, RenderPass renderPass)
        : this(vk, device, renderPass, ShaderModuleId.Sprite2D, 0)
    {
    }

    /// <summary>Initialises a pipeline for one of the shader library's 2D modules.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="renderPass">The pass the pipeline draws into.</param>
    /// <param name="module">The shader module, which carries both stages.</param>
    /// <param name="fragmentPushOffset">
    /// Where the fragment stage's slice of the push block starts, past the vertex stage's, or zero
    /// when the fragment stage reads nothing from it.
    /// </param>
    /// <exception cref="GraphicsDeviceException">A pipeline or its layout could not be created.</exception>
    /// <remarks>
    /// The two stages share one push block and each reads a disjoint part of it: the vertex stage
    /// takes the projection size at offset zero, the fragment stage everything from
    /// <paramref name="fragmentPushOffset"/> on. The block's total size comes from the module's own
    /// reflection rather than being written down a second time here.
    /// </remarks>
    public Sprite2DPipeline(
        Vk vk,
        VulkanDevice device,
        RenderPass renderPass,
        ShaderModuleId module,
        uint fragmentPushOffset)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device.Device;
        _pipelineCache = device.PipelineCache;
        _fragmentPushOffset = fragmentPushOffset;

        ShaderReflection reflection = ShaderCatalog.GetReflection(module);
        uint pushSize = (uint)(reflection.PushBlock?.Size ?? 0);
        _fragmentPushBytes = fragmentPushOffset > 0 ? pushSize - fragmentPushOffset : 0;
        _vertexEntryPoint = reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Vertex).Name;
        _fragmentEntryPoint = reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Fragment).Name;

        // One module, both stages: the Slang source declares them together and each pipeline stage
        // names the entry point it wants.
        _shader = new VulkanShaderModule(
            vk,
            _device,
            ShaderCatalog.GetSpirv(module).Span,
            ShaderStage.Vertex,
            _vertexEntryPoint);

        DescriptorSetLayout = CreateDescriptorSetLayout();
        PipelineLayout = CreatePipelineLayout(DescriptorSetLayout);

        _pipelines =
        [
            CreatePipeline(renderPass, BlendMode.Opaque),
            CreatePipeline(renderPass, BlendMode.AlphaBlend),
            CreatePipeline(renderPass, BlendMode.Additive),
        ];
    }

    /// <summary>Gets the layout binding the texture the fragment shader samples.</summary>
    public DescriptorSetLayout DescriptorSetLayout { get; }

    /// <summary>Gets the pipeline layout shared by every blend variant.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets the pipeline for one blend mode.</summary>
    /// <param name="blend">The blend mode.</param>
    /// <returns>The matching pipeline.</returns>
    public Pipeline ForBlend(BlendMode blend) => _pipelines[(int)blend];

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Pipeline pipeline in _pipelines)
        {
            _vk.DestroyPipeline(_device, pipeline, null);
        }

        _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, DescriptorSetLayout, null);
        _shader.Dispose();
    }

    private DescriptorSetLayout CreateDescriptorSetLayout()
    {
        // The image and its sampler are bound apart, because the shader library's modules declare
        // them apart -- a combined sampler takes two binding slots on the WGSL target and collides
        // with the next declaration, so neither target can use one.
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
            DescriptorType = DescriptorType.Sampler,
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
            "vkCreateDescriptorSetLayout");
        return layout;
    }

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout setLayout)
    {
        PushConstantRange* push = stackalloc PushConstantRange[2];
        push[0] = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = sizeof(float) * 2,
        };
        push[1] = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = _fragmentPushOffset,
            Size = _fragmentPushBytes,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = _fragmentPushBytes > 0 ? 2u : 1u,
            PPushConstantRanges = push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout");
        return layout;
    }

    private Pipeline CreatePipeline(RenderPass renderPass, BlendMode blend)
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
                Module = _shader.Handle,
                PName = (byte*)vertexEntryPoint,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _shader.Handle,
                PName = (byte*)fragmentEntryPoint,
            };

            VertexInputBindingDescription vertexBinding = new()
            {
                Binding = 0,
                Stride = (uint)Vertex2D.Size,
                InputRate = VertexInputRate.Vertex,
            };

            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[3];
            attributes[0] = new VertexInputAttributeDescription
            {
                Location = 0,
                Binding = 0,
                Format = Format.R32G32Sfloat,
                Offset = 0,
            };
            attributes[1] = new VertexInputAttributeDescription
            {
                Location = 1,
                Binding = 0,
                Format = Format.R32G32Sfloat,
                Offset = sizeof(float) * 2,
            };
            attributes[2] = new VertexInputAttributeDescription
            {
                Location = 2,
                Binding = 0,
                Format = Format.R8G8B8A8Unorm,
                Offset = sizeof(float) * 4,
            };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &vertexBinding,
                VertexAttributeDescriptionCount = 3,
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
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1.0f,
            };

            PipelineMultisampleStateCreateInfo multisample = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            // The render pass carries a depth attachment for the 3D path; the 2D path ignores it, so the
            // state is present but neither tests nor writes depth.
            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = false,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.Always,
            };

            PipelineColorBlendAttachmentState blendAttachment = BlendState(blend);
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
                RenderPass = renderPass,
                Subpass = 0,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(vertexEntryPoint);
            SilkMarshal.Free(fragmentEntryPoint);
        }
    }

    private static PipelineColorBlendAttachmentState BlendState(BlendMode blend)
    {
        const ColorComponentFlags writeAll = ColorComponentFlags.RBit
            | ColorComponentFlags.GBit
            | ColorComponentFlags.BBit
            | ColorComponentFlags.ABit;

        PipelineColorBlendAttachmentState state = new()
        {
            ColorWriteMask = writeAll,
            BlendEnable = blend != BlendMode.Opaque,
            ColorBlendOp = BlendOp.Add,
            AlphaBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
        };

        switch (blend)
        {
            case BlendMode.AlphaBlend:
                state.SrcColorBlendFactor = BlendFactor.SrcAlpha;
                state.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
                break;
            case BlendMode.Additive:
                state.SrcColorBlendFactor = BlendFactor.SrcAlpha;
                state.DstColorBlendFactor = BlendFactor.One;
                state.DstAlphaBlendFactor = BlendFactor.One;
                break;
            default:
                state.SrcColorBlendFactor = BlendFactor.One;
                state.DstColorBlendFactor = BlendFactor.Zero;
                break;
        }

        return state;
    }
}
