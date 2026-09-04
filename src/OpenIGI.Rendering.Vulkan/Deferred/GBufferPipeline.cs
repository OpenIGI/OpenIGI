using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The geometry pipeline: opaque meshes into the four G-buffer targets, one pipeline per depth-and-cull
/// state, created on demand and cached.
/// </summary>
/// <remarks>
/// Geometry is always opaque — translucency is the forward subpass's job — so blend does not vary here;
/// only depth behaviour and culling do. The texture set is the same layout the forward mesh
/// pipeline uses, so a draw's descriptor set is shared between the two paths.
/// </remarks>
internal sealed unsafe class GBufferPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly RenderPass _renderPass;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private readonly Dictionary<(DepthState Depth, CullMode Cull), Pipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GBufferPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="renderPass">The deferred render pass.</param>
    /// <param name="textureSetLayout">The sampler descriptor layout shared with the forward mesh path.</param>
    /// <param name="surfaceSetLayout">The per-frame surface block's layout, bound as set one.</param>
    /// <exception cref="GraphicsDeviceException">The layout or shaders could not be created.</exception>
    public GBufferPipeline(
        Vk vk,
        VulkanDevice device,
        ShaderCompiler compiler,
        RenderPass renderPass,
        DescriptorSetLayout textureSetLayout,
        DescriptorSetLayout surfaceSetLayout)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);
        _vk = vk;
        _device = device.Device;
        _renderPass = renderPass;

        _vertex = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(DeferredShaderSource.GBufferVertex, ShaderStage.Vertex, "gbuffer.vert"),
            ShaderStage.Vertex);
        _fragment = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(DeferredShaderSource.GBufferFragment, ShaderStage.Fragment, "gbuffer.frag"),
            ShaderStage.Fragment);

        PipelineLayout = CreatePipelineLayout(textureSetLayout, surfaceSetLayout);
    }

    /// <summary>Gets the pipeline layout: the texture set and the 128-byte push constant.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets, building if needed, the geometry pipeline for a depth-and-cull state.</summary>
    /// <param name="depth">The depth behaviour.</param>
    /// <param name="cull">The culling rule.</param>
    /// <returns>The matching pipeline.</returns>
    public Pipeline ForState(DepthState depth, CullMode cull)
    {
        if (_pipelines.TryGetValue((depth, cull), out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreatePipeline(depth, cull);
        _pipelines[(depth, cull)] = pipeline;
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
        _fragment.Dispose();
        _vertex.Dispose();
    }

    private PipelineLayout CreatePipelineLayout(
        DescriptorSetLayout textureSetLayout,
        DescriptorSetLayout surfaceSetLayout)
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = 128,
        };

        // Set zero is the draw's own textures, set one the frame's surface block. The split is what the
        // two are: one changes with every material and is cached per pairing, the other changes once a
        // frame and would have to be rewritten into every cached set if it shared theirs.
        DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[2];
        layouts[0] = textureSetLayout;
        layouts[1] = surfaceSetLayout;

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = layouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (gbuffer)");
        return layout;
    }

    private Pipeline CreatePipeline(DepthState depth, CullMode cull)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = Stage(ShaderStageFlags.VertexBit, _vertex.Handle, entryPoint);
            stages[1] = Stage(ShaderStageFlags.FragmentBit, _fragment.Handle, entryPoint);

            // Two streams of the same shape: the pose the mesh holds now, and the pose it held before.
            // A static mesh reports one buffer as both, so the delta the vertex stage forms is zero.
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

            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[8];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 12 };
            attributes[2] = new VertexInputAttributeDescription { Location = 2, Binding = 0, Format = Format.R32G32Sfloat, Offset = 24 };
            attributes[3] = new VertexInputAttributeDescription { Location = 3, Binding = 0, Format = Format.R32G32Sfloat, Offset = 32 };
            attributes[4] = new VertexInputAttributeDescription { Location = 4, Binding = 0, Format = Format.R8G8B8A8Unorm, Offset = (uint)Vertex3D.ColourOffset };
            attributes[5] = new VertexInputAttributeDescription { Location = 5, Binding = 0, Format = Format.R8G8B8A8Unorm, Offset = (uint)Vertex3D.SurfaceNormalOffset };

            // The previous pose's position and normal, out of the second stream. Only these two: nothing
            // else about a vertex changes when a body is re-skinned.
            attributes[6] = new VertexInputAttributeDescription { Location = 6, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[7] = new VertexInputAttributeDescription { Location = 7, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 12 };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = vertexBindings,
                VertexAttributeDescriptionCount = 8,
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
                    CullMode.Clockwise => CullModeFlags.FrontBit,
                    CullMode.CounterClockwise => CullModeFlags.BackBit,
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

            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = depth != DepthState.Disabled,
                DepthWriteEnable = depth == DepthState.TestAndWrite,
                DepthCompareOp = CompareOp.GreaterOrEqual,
            };

            // Four opaque colour attachments, all channels written.
            const ColorComponentFlags writeAll = ColorComponentFlags.RBit | ColorComponentFlags.GBit
                | ColorComponentFlags.BBit | ColorComponentFlags.ABit;
            PipelineColorBlendAttachmentState* blends = stackalloc PipelineColorBlendAttachmentState[4];
            for (int i = 0; i < 4; i++)
            {
                blends[i] = new PipelineColorBlendAttachmentState { ColorWriteMask = writeAll, BlendEnable = false };
            }

            PipelineColorBlendStateCreateInfo colorBlend = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 4,
                PAttachments = blends,
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
                Subpass = DeferredRenderPass.GeometrySubpass,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (gbuffer)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }

    private static PipelineShaderStageCreateInfo Stage(ShaderStageFlags stage, Silk.NET.Vulkan.ShaderModule module, nint entryPoint) => new()
    {
        SType = StructureType.PipelineShaderStageCreateInfo,
        Stage = stage,
        Module = module,
        PName = (byte*)entryPoint,
    };
}
