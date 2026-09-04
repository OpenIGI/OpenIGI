using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>Which of the selection glow's three passes a pipeline is for.</summary>
internal enum OutlinePass
{
    /// <summary>
    /// The silhouette itself, written to depth and to no colour, so the shells behind it are stopped by
    /// the model rather than washing across it.
    /// </summary>
    Mask,

    /// <summary>A shell that culls front faces — for a frame whose own rule keeps them.</summary>
    ShellCullFront,

    /// <summary>A shell that culls back faces — for a frame whose own rule keeps those.</summary>
    ShellCullBack,
}

/// <summary>
/// The selection glow's pipelines: a mask and two shells, per render pass they are asked to draw into.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by the render pass and subpass as well as by the job, because the glow is drawn wherever the
/// caller happens to be — the deferred path's forward subpass over the lit scene, or the present pass on
/// the direct path — and a Vulkan pipeline is built against one of them. Built on demand and kept, which
/// in practice is two or three objects for a session.
/// </para>
/// <para>
/// No descriptor sets: a silhouette carries no texture and no lighting, so everything the two stages
/// need fits in one push block.
/// </para>
/// </remarks>
internal sealed unsafe class OutlinePipeline : IDisposable
{
    /// <summary>The size of the push block both stages share.</summary>
    public const int PushSize = (16 + 4 + 4 + 4) * sizeof(float);

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private readonly Dictionary<(ulong Pass, uint Subpass, OutlinePass Job), Pipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="OutlinePipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <exception cref="GraphicsDeviceException">The layout or shaders could not be created.</exception>
    public OutlinePipeline(Vk vk, VulkanDevice device, ShaderCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);
        _vk = vk;
        _device = device.Device;

        _vertex = new VulkanShaderModule(
            vk, _device, compiler.Compile(OutlineShaderSource.Vertex, ShaderStage.Vertex, "outline.vert"), ShaderStage.Vertex);
        _fragment = new VulkanShaderModule(
            vk, _device, compiler.Compile(OutlineShaderSource.Fragment, ShaderStage.Fragment, "outline.frag"), ShaderStage.Fragment);

        PipelineLayout = CreateLayout();
    }

    /// <summary>Gets the layout, which carries the push block and nothing else.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets, building if needed, the pipeline for one job in one render pass.</summary>
    /// <param name="renderPass">The render pass the caller is inside.</param>
    /// <param name="subpass">The subpass index within it.</param>
    /// <param name="job">Which of the glow's passes this is.</param>
    /// <returns>The matching pipeline.</returns>
    public Pipeline ForPass(RenderPass renderPass, uint subpass, OutlinePass job)
    {
        (ulong, uint, OutlinePass) key = (renderPass.Handle, subpass, job);
        if (_pipelines.TryGetValue(key, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = Create(renderPass, subpass, job);
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
        _fragment.Dispose();
        _vertex.Dispose();
    }

    private PipelineLayout CreateLayout()
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = PushSize,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 0,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (outline)");
        return layout;
    }

    private Pipeline Create(RenderPass renderPass, uint subpass, OutlinePass job)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = Stage(ShaderStageFlags.VertexBit, _vertex.Handle, entryPoint);
            stages[1] = Stage(ShaderStageFlags.FragmentBit, _fragment.Handle, entryPoint);

            // The same two streams every mesh pipeline takes, so a posed body outlines in the pose it was
            // drawn in rather than in the one the simulation last finished.
            VertexInputBindingDescription* bindings = stackalloc VertexInputBindingDescription[2];
            bindings[0] = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };
            bindings[1] = new VertexInputBindingDescription
            {
                Binding = 1,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };

            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[4];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 12 };
            attributes[2] = new VertexInputAttributeDescription { Location = 5, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[3] = new VertexInputAttributeDescription { Location = 6, Binding = 1, Format = Format.R32G32B32Sfloat, Offset = 12 };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindings,
                VertexAttributeDescriptionCount = 4,
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

            // The mask takes both facings: a model with inconsistent winding must still stop the glow
            // getting inside it, and a hole in the mask reads as a bright patch across the middle. A
            // shell takes one facing, so each contributes exactly one layer.
            PipelineRasterizationStateCreateInfo rasterization = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = job switch
                {
                    OutlinePass.ShellCullFront => CullModeFlags.FrontBit,
                    OutlinePass.ShellCullBack => CullModeFlags.BackBit,
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

            // The mask writes depth and always passes, laying the silhouette down at a depth just inside
            // the near plane. A shell tests against it and loses -- which is what stops the glow crossing
            // the model -- while passing against the world behind, in reverse-Z where nearer is greater.
            bool mask = job == OutlinePass.Mask;
            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = mask,
                DepthCompareOp = mask ? CompareOp.Always : CompareOp.Greater,
            };

            const ColorComponentFlags writeAll = ColorComponentFlags.RBit
                | ColorComponentFlags.GBit
                | ColorComponentFlags.BBit
                | ColorComponentFlags.ABit;

            PipelineColorBlendAttachmentState blendAttachment = new()
            {
                BlendEnable = !mask,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                // The lit target's alpha is the frame's own marker for what is world geometry and what
                // is only in front of the eye, and a shell forces it to zero: the glow is a mark ON the
                // picture, not a thing standing in it. Both factors are zero, so whatever the shell
                // covers reads as not-world however faint the shell itself was -- which is what keeps a
                // marker out of the puddle it is standing over.
                SrcAlphaBlendFactor = BlendFactor.Zero,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,

                // The mask writes no colour at all. That is the whole of what makes it a mask.
                ColorWriteMask = mask ? 0 : writeAll,
            };

            PipelineColorBlendStateCreateInfo colorBlend = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };

            DynamicState* dynamicStates = stackalloc DynamicState[2];
            dynamicStates[0] = DynamicState.Viewport;
            dynamicStates[1] = DynamicState.Scissor;
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
                _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (outline)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }

    private static PipelineShaderStageCreateInfo Stage(ShaderStageFlags stage, ShaderModule module, nint entryPoint) => new()
    {
        SType = StructureType.PipelineShaderStageCreateInfo,
        Stage = stage,
        Module = module,
        PName = (byte*)entryPoint,
    };
}
