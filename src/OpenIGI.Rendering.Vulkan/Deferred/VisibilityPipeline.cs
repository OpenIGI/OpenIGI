using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The pipeline an occlusion query draws its bounding box with, into the geometry pass against the depth
/// already written there.
/// </summary>
/// <remarks>
/// It shares the geometry pass and subpass so it tests against the same depth the G-buffer fill wrote,
/// with the same reverse-Z <see cref="CompareOp.GreaterOrEqual"/>. It differs from the G-buffer pipeline
/// in three ways that make it a probe rather than a draw: it writes no depth (a box must not disturb the
/// depth the real geometry established), it masks every colour channel of all four targets (it produces
/// no shading), and it takes no vertex input — the box's corners are baked into the shader and selected
/// by <c>gl_VertexIndex</c>, so the draw is a bare <c>vkCmdDraw(36, …)</c> with only a push constant.
/// Culling is off so a box seen from inside still rasterises.
/// </remarks>
internal sealed unsafe class VisibilityPipeline : IDisposable
{
    /// <summary>The push constant size: a 64-byte matrix and two 16-byte bounds vectors.</summary>
    public const uint PushConstantSize = 96;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VisibilityPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="geometryPass">The deferred geometry render pass the box is drawn into.</param>
    /// <exception cref="GraphicsDeviceException">The layout, shaders or pipeline could not be created.</exception>
    public VisibilityPipeline(Vk vk, VulkanDevice device, ShaderCompiler compiler, RenderPass geometryPass)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);
        _vk = vk;
        _device = device.Device;

        _vertex = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(OcclusionShaderSource.Vertex, ShaderStage.Vertex, "occlusion.vert"),
            ShaderStage.Vertex);
        _fragment = new VulkanShaderModule(
            vk,
            _device,
            compiler.Compile(OcclusionShaderSource.Fragment, ShaderStage.Fragment, "occlusion.frag"),
            ShaderStage.Fragment);

        PipelineLayout = CreatePipelineLayout();
        Pipeline = CreatePipeline(geometryPass);
    }

    /// <summary>Gets the pipeline.</summary>
    public Pipeline Pipeline { get; }

    /// <summary>Gets the pipeline layout: no descriptor sets, one vertex-stage push constant.</summary>
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
        _fragment.Dispose();
        _vertex.Dispose();
    }

    private PipelineLayout CreatePipelineLayout()
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = PushConstantSize,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 0,
            PSetLayouts = null,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (visibility)");
        return layout;
    }

    private Pipeline CreatePipeline(RenderPass geometryPass)
    {
        nint entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = Stage(ShaderStageFlags.VertexBit, _vertex.Handle, entryPoint);
            stages[1] = Stage(ShaderStageFlags.FragmentBit, _fragment.Handle, entryPoint);

            // No vertex or index buffer: the box's corners are baked into the vertex shader and chosen by
            // gl_VertexIndex, so the state is empty.
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

            // Test against the geometry depth with the same reverse-Z compare, but never write it: a probe
            // must not change the depth the real geometry established.
            PipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.GreaterOrEqual,
            };

            // The geometry pass has four colour targets; the box writes none of them.
            PipelineColorBlendAttachmentState* blends = stackalloc PipelineColorBlendAttachmentState[4];
            for (int i = 0; i < 4; i++)
            {
                blends[i] = new PipelineColorBlendAttachmentState { ColorWriteMask = 0, BlendEnable = false };
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
                RenderPass = geometryPass,
                Subpass = DeferredRenderPass.GeometrySubpass,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (visibility)");
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
