using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// The world pipeline: graphics pipelines for the 3D forward draw, one per distinct render state,
/// created on demand and cached, sharing a layout that binds a diffuse and a lightmap texture.
/// </summary>
/// <remarks>
/// <para>
/// Depth behaviour, face culling and blending are all baked into a Vulkan pipeline, so each combination
/// the world draws with is its own object. Rather than enumerate the whole product up front — most
/// combinations never occur — a pipeline is built the first time a state is asked for and kept, keyed by
/// that state. In practice the world uses a handful.
/// </para>
/// <para>
/// The front face is counter-clockwise, as OpenGL's is: the vertex shader's Y flip and Vulkan's y-down
/// framebuffer cancel out, so the framebuffer winding matches OpenGL's and the engine's cull rule lands
/// on the same triangles the OpenGL backend keeps.
/// </para>
/// </remarks>
internal sealed unsafe class Mesh3DPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly RenderPass _renderPass;
    private readonly uint _subpass;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private readonly Dictionary<Mesh3DPipelineKey, Pipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="Mesh3DPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="compiler">The shader compiler that turns the GLSL into SPIR-V.</param>
    /// <param name="renderPass">The render pass the pipelines are compatible with.</param>
    /// <param name="subpass">The subpass index within that render pass.</param>
    /// <exception cref="GraphicsDeviceException">The layout or shaders could not be created.</exception>
    public Mesh3DPipeline(Vk vk, VulkanDevice device, ShaderCompiler compiler, RenderPass renderPass, uint subpass = 0)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);
        _vk = vk;
        _device = device.Device;
        _renderPass = renderPass;
        _subpass = subpass;

        _vertex = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(Mesh3DShaderSource.Vertex, ShaderStage.Vertex, "mesh3d.vert"),
            ShaderStage.Vertex);
        _fragment = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(Mesh3DShaderSource.Fragment, ShaderStage.Fragment, "mesh3d.frag"),
            ShaderStage.Fragment);

        DescriptorSetLayout = CreateDescriptorSetLayout();
        PipelineLayout = CreatePipelineLayout(DescriptorSetLayout);
    }

    /// <summary>Gets the layout binding the diffuse and lightmap textures the fragment shader samples.</summary>
    public DescriptorSetLayout DescriptorSetLayout { get; }

    /// <summary>Gets the pipeline layout shared by every variant.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets, building if needed, the pipeline for one render state.</summary>
    /// <param name="depth">The depth behaviour.</param>
    /// <param name="cull">The face-culling rule.</param>
    /// <param name="blend">The blend mode.</param>
    /// <returns>The matching pipeline.</returns>
    public Pipeline ForState(DepthState depth, CullMode cull, BlendMode blend)
    {
        Mesh3DPipelineKey key = new(depth, cull, blend);
        if (_pipelines.TryGetValue(key, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreatePipeline(key);
        _pipelines[key] = pipeline;
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

        _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, DescriptorSetLayout, null);
        _fragment.Dispose();
        _vertex.Dispose();
    }

    private DescriptorSetLayout CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
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
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout");
        return layout;
    }

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout setLayout)
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = sizeof(float) * 32,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout");
        return layout;
    }

    private Pipeline CreatePipeline(Mesh3DPipelineKey key)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertex.Handle,
                PName = (byte*)entryPoint,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragment.Handle,
                PName = (byte*)entryPoint,
            };

            // Two streams: the pose the mesh holds now, and the pose it held before. The first-person rig
            // is skinned like a body and drawn through here, so it blends between the two as they do.
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
                CullMode = CullFlags(key.Cull),
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1.0f,
            };

            PipelineMultisampleStateCreateInfo multisample = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            PipelineDepthStencilStateCreateInfo depthStencil = DepthState(key.Depth);
            PipelineColorBlendAttachmentState blendAttachment = BlendState(key.Blend);
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
                RenderPass = _renderPass,
                Subpass = _subpass,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }

    private static CullModeFlags CullFlags(CullMode mode) => mode switch
    {
        // The shader's Y flip and Vulkan's y-down framebuffer cancel out, so the framebuffer winding is
        // identical to OpenGL's — which means the front face is counter-clockwise, as GL uses, and the
        // cull rule maps exactly as GL's does: cull front for Clockwise, back for CounterClockwise.
        CullMode.Clockwise => CullModeFlags.FrontBit,
        CullMode.CounterClockwise => CullModeFlags.BackBit,
        _ => CullModeFlags.None,
    };

    private static PipelineDepthStencilStateCreateInfo DepthState(DepthState state) => new()
    {
        SType = StructureType.PipelineDepthStencilStateCreateInfo,
        DepthTestEnable = state != Rendering.DepthState.Disabled,
        DepthWriteEnable = state == Rendering.DepthState.TestAndWrite,

        // Reverse-Z: nearer geometry has the greater depth value, so the test keeps the greater one.
        DepthCompareOp = CompareOp.GreaterOrEqual,
    };

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
