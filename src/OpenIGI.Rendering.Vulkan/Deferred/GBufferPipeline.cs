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

    // The device's own pipeline cache, copied here because this class keeps the raw handle
    // rather than the wrapper. Passing it is what lets the driver return a previous run's
    // compiled pipeline instead of building this one again.
    private readonly PipelineCache _pipelineCache;
    private readonly RenderPass _renderPass;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    /// <summary>Where the G-buffer's base colour is attached, in the order the subpass declares.</summary>
    private const int GBaseColourAttachment = 0;

    /// <summary>Where the G-buffer's baked light is attached, in the same order.</summary>
    private const int GBakedAttachment = 3;

    private readonly Dictionary<(DepthState Depth, CullMode Cull, BlendMode Blend, byte AlphaReference), Pipeline> _pipelines = [];
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
        _pipelineCache = device.PipelineCache;
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

    /// <summary>Gets the pipeline for a draw's state, building it on first use.</summary>
    /// <param name="depth">The draw's depth test and write.</param>
    /// <param name="cull">Which facing the draw culls.</param>
    /// <param name="blend">
    /// How the draw blends. The G-buffer is opaque, and every mode but <see cref="BlendMode.Additive"/>
    /// is drawn as such; additive is for the terrain's material passes, which add their weighted share
    /// of the ground into the base colour over the base pass that wrote everything else (see
    /// <c>TerrainChunkMesh</c>): the base colour alone blends, its alpha and the other attachments are
    /// left as the base pass wrote them.
    /// </param>
    /// <param name="alphaReference">
    /// How much texture alpha a cut-out fragment needs to survive, out of 255. Baked into the variant
    /// because there is no push room for it; see <see cref="Pipelines.AlphaReferenceSpecialization"/>.
    /// <b>The shadow pass has to be asked for the same number</b>, or a surface shadows itself along the
    /// edge where the two tests disagree.
    /// </param>
    public Pipeline ForState(
        DepthState depth,
        CullMode cull,
        BlendMode blend = BlendMode.Opaque,
        byte alphaReference = IGraphicsDevice.DefaultAlphaReference)
    {
        if (_pipelines.TryGetValue((depth, cull, blend, alphaReference), out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreatePipeline(depth, cull, blend, alphaReference);
        _pipelines[(depth, cull, blend, alphaReference)] = pipeline;
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

    private Pipeline CreatePipeline(DepthState depth, CullMode cull, BlendMode blend, byte alphaReference)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            float specializationValue;
            SpecializationMapEntry specializationEntry;
            SpecializationInfo specialization = Pipelines.AlphaReferenceSpecialization.Describe(
                &specializationValue,
                &specializationEntry,
                alphaReference);

            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = Stage(ShaderStageFlags.VertexBit, _vertex.Handle, entryPoint);
            stages[1] = Stage(ShaderStageFlags.FragmentBit, _fragment.Handle, entryPoint);
            stages[1].PSpecializationInfo = &specialization;

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

            // Four opaque colour attachments, all channels written -- except for an additive draw, which
            // adds its colour into the base colour's rgb and touches nothing else: not that attachment's
            // alpha, and none of the normal, material or baked attachments, which the opaque pass under
            // it wrote for the same surface.
            const ColorComponentFlags writeAll = ColorComponentFlags.RBit | ColorComponentFlags.GBit
                | ColorComponentFlags.BBit | ColorComponentFlags.ABit;
            const ColorComponentFlags writeColour = ColorComponentFlags.RBit | ColorComponentFlags.GBit
                | ColorComponentFlags.BBit;
            PipelineColorBlendAttachmentState* blends = stackalloc PipelineColorBlendAttachmentState[4];
            for (int i = 0; i < 4; i++)
            {
                blends[i] = new PipelineColorBlendAttachmentState { ColorWriteMask = writeAll, BlendEnable = false };
            }

            // Additive and Modulate2X are the ground's two ways of combining with what a pass before
            // them wrote, and on a deferred path they do NOT land in the same place.
            //
            // Additive is one more material adding its share of the ground's colour, so it belongs on
            // the base colour with the base pass it is finishing. Modulate2X is the tile's BAKE, and a
            // bake is not a colour: the base colour attachment holds an albedo, and a bake multiplied
            // into an albedo is lit a second time by whatever lights the frame. So it goes to the baked
            // attachment, which the base pass left at the neutral one and which the lighting reads as
            // irradiance -- and the doubling MODULATE2X means falls out of the blend for free, because
            // src * dst + dst * src over a neutral destination is twice the bake.
            //
            // Either way one attachment's rgb and nothing else: not its alpha, and none of the three the
            // opaque pass under it wrote for the same surface.
            if (blend is BlendMode.Additive or BlendMode.Modulate2X)
            {
                bool modulate = blend == BlendMode.Modulate2X;
                for (int i = 0; i < 4; i++)
                {
                    blends[i] = new PipelineColorBlendAttachmentState { ColorWriteMask = 0, BlendEnable = false };
                }

                blends[modulate ? GBakedAttachment : GBaseColourAttachment] = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = writeColour,
                    BlendEnable = true,
                    ColorBlendOp = BlendOp.Add,
                    SrcColorBlendFactor = modulate ? BlendFactor.DstColor : BlendFactor.One,
                    DstColorBlendFactor = modulate ? BlendFactor.SrcColor : BlendFactor.One,
                    AlphaBlendOp = BlendOp.Add,
                    SrcAlphaBlendFactor = BlendFactor.Zero,
                    DstAlphaBlendFactor = BlendFactor.One,
                };
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
                _vk.CreateGraphicsPipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline),
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
