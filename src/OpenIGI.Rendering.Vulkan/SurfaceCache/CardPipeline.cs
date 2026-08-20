using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.SurfaceCache;

/// <summary>The pipeline that draws a model into a page of the card atlas.</summary>
/// <remarks>
/// One pipeline, no variants. Every card capture wants the same state: opaque, depth-tested so the nearest
/// surface wins, and no face culling at all — the port's meshes do not wind consistently, and a card that
/// dropped half its triangles would leave holes in the atlas that later read as empty space.
/// </remarks>
internal sealed unsafe class CardPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="CardPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="renderPass">The card atlas's render pass.</param>
    /// <param name="textureSetLayout">The layout of the two-sampler texture set a mesh draw binds.</param>
    public CardPipeline(
        Vk vk,
        VulkanDevice device,
        ShaderCompiler compiler,
        RenderPass renderPass,
        DescriptorSetLayout textureSetLayout)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);

        _vk = vk;
        _device = device.Device;

        _vertex = new VulkanShaderModule(
            vk, _device, compiler.Compile(CardShaderSource.Vertex, ShaderStage.Vertex, "card.vert"), ShaderStage.Vertex);
        _fragment = new VulkanShaderModule(
            vk, _device, compiler.Compile(CardShaderSource.Fragment, ShaderStage.Fragment, "card.frag"), ShaderStage.Fragment);

        PipelineLayout = CreatePipelineLayout(textureSetLayout);
        Pipeline = CreatePipeline(renderPass);
    }

    /// <summary>Gets the pipeline layout: the texture set and the 128-byte push constant.</summary>
    public PipelineLayout PipelineLayout { get; }

    /// <summary>Gets the pipeline.</summary>
    public Pipeline Pipeline { get; }

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
        _fragment.Dispose();
    }

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout textureSetLayout)
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = 128,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &textureSetLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (cards)");
        return layout;
    }

    private Pipeline CreatePipeline(RenderPass renderPass)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = Stage(ShaderStageFlags.VertexBit, _vertex.Handle, entryPoint);
            stages[1] = Stage(ShaderStageFlags.FragmentBit, _fragment.Handle, entryPoint);

            VertexInputBindingDescription vertexBinding = new()
            {
                Binding = 0,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };

            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[5];
            attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
            attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32B32Sfloat, Offset = 12 };
            attributes[2] = new VertexInputAttributeDescription { Location = 2, Binding = 0, Format = Format.R32G32Sfloat, Offset = 24 };
            attributes[3] = new VertexInputAttributeDescription { Location = 3, Binding = 0, Format = Format.R32G32Sfloat, Offset = 32 };
            attributes[4] = new VertexInputAttributeDescription { Location = 4, Binding = 0, Format = Format.R8G8B8A8Unorm, Offset = (uint)Vertex3D.ColourOffset };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &vertexBinding,
                VertexAttributeDescriptionCount = 5,
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

            // The card's depth runs the ordinary way rather than reversed, so the nearest surface is the
            // one with the smaller value.
            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.LessOrEqual,
            };

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
                RenderPass = renderPass,
                Subpass = 0,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (cards)");
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
