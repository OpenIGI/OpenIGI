using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using ShaderStage = OpenIGI.Rendering.Vulkan.Shaders.ShaderStage;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// A pipeline that draws a single full-screen triangle — the shape both the deferred lighting and the
/// resolve take: one descriptor set, a small push constant, no vertex input, no depth.
/// </summary>
/// <remarks>
/// The triangle is generated from the vertex index, so there is no vertex buffer to bind; the draw is a
/// three-vertex <c>vkCmdDraw</c>. The lighting instance reads the G-buffer as input attachments and the
/// resolve instance samples the lit scene — the only difference is the descriptor layout and the
/// fragment shader handed in.
/// </remarks>
internal sealed unsafe class FullscreenPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;

    // The device's own pipeline cache, copied here because this class keeps the raw handle
    // rather than the wrapper. Passing it is what lets the driver return a previous run's
    // compiled pipeline instead of building this one again.
    private readonly PipelineCache _pipelineCache;
    private readonly VulkanShaderModule _vertex;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    private bool _disposed;

    /// <summary>Initialises a pipeline from one of the shader library's modules.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="module">The shader module, which carries both stages.</param>
    /// <param name="descriptorSetLayout">The single descriptor set the shader reads.</param>
    /// <param name="renderPass">The render pass the pipeline is compatible with.</param>
    /// <param name="subpass">The subpass index within that render pass.</param>
    /// <param name="alphaBlend">Whether the pass blends over the target by its alpha rather than replacing it.</param>
    /// <exception cref="GraphicsDeviceException">The pipeline could not be created.</exception>
    /// <remarks>
    /// One module carries the full-screen triangle and the shading, so a single shader module serves
    /// both stages and the push block's size comes from the module's own reflection.
    /// </remarks>
    public FullscreenPipeline(
        Vk vk,
        VulkanDevice device,
        ShaderModuleId module,
        DescriptorSetLayout descriptorSetLayout,
        RenderPass renderPass,
        uint subpass,
        bool alphaBlend = false)
        : this(vk, device, module, ShaderVariant.Default, descriptorSetLayout, renderPass, subpass, alphaBlend)
    {
    }

    /// <summary>Initialises a pipeline from one variant of a shader library module.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="module">The shader module, which carries both stages.</param>
    /// <param name="variant">
    /// Which compiled form to load. The lighting is built once with traced lamp shadows and once
    /// without, because the ray query needs its own resource bound and that is not a runtime question.
    /// </param>
    /// <param name="descriptorSetLayout">The single descriptor set the shader reads.</param>
    /// <param name="renderPass">The render pass the pipeline is compatible with.</param>
    /// <param name="subpass">The subpass index within that render pass.</param>
    /// <param name="alphaBlend">Whether the pass blends over the target by its alpha rather than replacing it.</param>
    /// <exception cref="GraphicsDeviceException">The pipeline could not be created.</exception>
    public FullscreenPipeline(
        Vk vk,
        VulkanDevice device,
        ShaderModuleId module,
        ShaderVariant variant,
        DescriptorSetLayout descriptorSetLayout,
        RenderPass renderPass,
        uint subpass,
        bool alphaBlend = false)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device.Device;
        _pipelineCache = device.PipelineCache;

        ShaderReflection reflection = ShaderCatalog.GetReflection(module, variant);
        _vertexEntryPoint = reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Vertex).Name;
        _fragmentEntryPoint = reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Fragment).Name;

        _vertex = new VulkanShaderModule(
            vk,
            _device,
            ShaderCatalog.GetSpirv(module, variant).Span,
            ShaderStage.Vertex,
            _vertexEntryPoint);

        PipelineLayout = CreatePipelineLayout(descriptorSetLayout, (uint)(reflection.PushBlock?.Size ?? 0));
        Pipeline = CreatePipeline(renderPass, subpass, alphaBlend);
    }

    /// <summary>Gets the pipeline.</summary>
    public Pipeline Pipeline { get; }

    /// <summary>Gets the pipeline layout.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyPipeline(_device, Pipeline, null);
        _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
        _vertex.Dispose();
    }

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout setLayout, uint pushConstantSize)
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = pushConstantSize,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = pushConstantSize > 0 ? 1u : 0u,
            PPushConstantRanges = pushConstantSize > 0 ? &push : null,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (fullscreen)");
        return layout;
    }

    private Pipeline CreatePipeline(RenderPass renderPass, uint subpass, bool alphaBlend)
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
                Module = _vertex.Handle,
                PName = (byte*)vertexEntryPoint,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _vertex.Handle,
                PName = (byte*)fragmentEntryPoint,
            };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
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

            // A full-screen pass neither tests nor writes depth, but a subpass it draws into can still own
            // a depth attachment — the resolve's present pass does — so the state must be supplied, disabled,
            // rather than left null.
            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = false,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.Always,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };

            const ColorComponentFlags writeAll = ColorComponentFlags.RBit | ColorComponentFlags.GBit
                | ColorComponentFlags.BBit | ColorComponentFlags.ABit;
            // Opaque for a resolve, which owns the whole target; blended for a pass that composes over
            // what the frame already holds, such as the loading backdrop fading off the live level.
            PipelineColorBlendAttachmentState blendAttachment = new()
            {
                ColorWriteMask = writeAll,
                BlendEnable = alphaBlend,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
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
                RenderPass = renderPass,
                Subpass = subpass,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (fullscreen)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(vertexEntryPoint);
            SilkMarshal.Free(fragmentEntryPoint);
        }
    }
}
