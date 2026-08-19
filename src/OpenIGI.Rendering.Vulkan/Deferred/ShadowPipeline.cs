using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The pipeline that renders a shadow caster's depth into a cascade — position only, no colour, a depth
/// bias to keep a surface from shadowing itself, and one matrix in a push constant.
/// </summary>
/// <remarks>
/// It reads the same vertex buffer the world draws from, but consumes only the position at offset zero,
/// so a mesh casts a shadow with no extra upload. A slope-scaled depth bias is baked in rather than left
/// to the caller: the acne a shadow map shows on ground raked by the sun is a property of the map's
/// resolution, not of any one draw.
/// </remarks>
internal sealed unsafe class ShadowPipeline : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShadowPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="renderPass">The shadow render pass.</param>
    /// <exception cref="GraphicsDeviceException">The pipeline could not be created.</exception>
    public ShadowPipeline(Vk vk, VulkanDevice device, ShaderCompiler compiler, RenderPass renderPass)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiler);
        _vk = vk;
        _device = device.Device;

        _vertex = new VulkanShaderModule(
            vk, _device, compiler.Compile(DeferredShaderSource.ShadowVertex, ShaderStage.Vertex, "shadow.vert"), ShaderStage.Vertex);
        _fragment = new VulkanShaderModule(
            vk, _device, compiler.Compile(DeferredShaderSource.ShadowFragment, ShaderStage.Fragment, "shadow.frag"), ShaderStage.Fragment);

        PipelineLayout = CreateLayout();
        Pipeline = CreatePipeline(renderPass);
    }

    /// <summary>Gets the pipeline.</summary>
    public Pipeline Pipeline { get; }

    /// <summary>Gets the pipeline layout, which carries the model-view-projection push constant.</summary>
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

    private PipelineLayout CreateLayout()
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = sizeof(float) * 16,
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (shadow)");
        return layout;
    }

    private Pipeline CreatePipeline(RenderPass renderPass)
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

            // The same vertex buffer the world draws from, reading only the position at offset zero.
            VertexInputBindingDescription binding = new()
            {
                Binding = 0,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };
            VertexInputAttributeDescription attribute = new()
            {
                Location = 0,
                Binding = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = 0,
            };
            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 1,
                PVertexAttributeDescriptions = &attribute,
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

            // A slope-scaled depth bias pushes a caster's stored depth away from the light so a lit surface
            // does not read as its own shadow. No culling — the world's meshes are not reliably wound one
            // way, and the bias handles the acne culling would.
            PipelineRasterizationStateCreateInfo rasterization = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = true,
                DepthBiasConstantFactor = 1.25f,
                DepthBiasSlopeFactor = 2.75f,
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
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.LessOrEqual,
                DepthBoundsTestEnable = false,
                StencilTestEnable = false,
            };

            PipelineColorBlendStateCreateInfo colorBlend = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 0,
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
            VkCheck.Ok(_vk.CreateGraphicsPipelines(_device, default, 1, &createInfo, null, &pipeline), "vkCreateGraphicsPipelines (shadow)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entryPoint);
        }
    }
}
