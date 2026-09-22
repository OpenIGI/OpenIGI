using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using ShaderStage = OpenIGI.Rendering.Vulkan.Shaders.ShaderStage;
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

    // The device's own pipeline cache, copied here because this class keeps the raw handle
    // rather than the wrapper. Passing it is what lets the driver return a previous run's
    // compiled pipeline instead of building this one again.
    private readonly PipelineCache _pipelineCache;
    private readonly Dictionary<byte, Pipeline> _pipelines = [];
    private RenderPass _renderPass;
    private readonly VulkanShaderModule _shader;
    private readonly string _vertexEntryPoint;
    private readonly string _fragmentEntryPoint;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShadowPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="renderPass">The shadow render pass.</param>
    /// <param name="textureSetLayout">The shared texture set, for the cut-out test's alpha.</param>
    /// <exception cref="GraphicsDeviceException">The pipeline could not be created.</exception>
    public ShadowPipeline(
        Vk vk,
        VulkanDevice device,
        RenderPass renderPass,
        DescriptorSetLayout textureSetLayout)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device.Device;
        _pipelineCache = device.PipelineCache;

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Shadow);
        _vertexEntryPoint = reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Vertex).Name;
        _fragmentEntryPoint = reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Fragment).Name;
        _shader = new VulkanShaderModule(
            vk,
            _device,
            ShaderCatalog.GetSpirv(ShaderModuleId.Shadow).Span,
            ShaderStage.Vertex,
            _vertexEntryPoint);

        _renderPass = renderPass;
        PipelineLayout = CreateLayout(textureSetLayout);
        Pipeline = CreatePipeline(renderPass, IGraphicsDevice.DefaultAlphaReference);
        _pipelines[IGraphicsDevice.DefaultAlphaReference] = Pipeline;
    }

    /// <summary>Gets the pipeline that cuts out at the default threshold.</summary>
    public Pipeline Pipeline { get; }

    /// <summary>Gets, building if needed, the pipeline that cuts out at one threshold.</summary>
    /// <param name="alphaReference">The threshold out of 255.</param>
    /// <returns>The matching pipeline.</returns>
    /// <remarks>
    /// <b>Must be asked for the same number the G-buffer was.</b> The shadow fragment shader says so
    /// itself: a pass that cuts on less than the G-buffer keeps casters the frame never draws, and one
    /// that cuts on more drops shadows from things you can see. Either way the two disagree and the
    /// ground goes patchy along the edge.
    /// </remarks>
    public Pipeline ForAlphaReference(byte alphaReference)
    {
        if (_pipelines.TryGetValue(alphaReference, out Pipeline existing))
        {
            return existing;
        }

        Pipeline pipeline = CreatePipeline(_renderPass, alphaReference);
        _pipelines[alphaReference] = pipeline;
        return pipeline;
    }

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
        // Every variant, and the default is one of them -- it is put in the dictionary when it is built,
        // so destroying Pipeline separately here would be a double free.
        foreach (Pipeline pipeline in _pipelines.Values)
        {
            _vk.DestroyPipeline(_device, pipeline, null);
        }

        _pipelines.Clear();
        _vk.DestroyPipelineLayout(_device, PipelineLayout, null);
        _shader.Dispose();
    }

    private PipelineLayout CreateLayout(DescriptorSetLayout textureSetLayout)
    {
        PushConstantRange push = new()
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = sizeof(float) * 24,
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
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (shadow)");
        return layout;
    }

    private Pipeline CreatePipeline(RenderPass renderPass, byte alphaReference)
    {
        nint vertexEntryPoint = SilkMarshal.StringToPtr(_vertexEntryPoint, NativeStringEncoding.UTF8);
        nint fragmentEntryPoint = SilkMarshal.StringToPtr(_fragmentEntryPoint, NativeStringEncoding.UTF8);
        try
        {
            float specializationValue;
            SpecializationMapEntry specializationEntry;
            SpecializationInfo specialization = Pipelines.AlphaReferenceSpecialization.Describe(
                &specializationValue,
                &specializationEntry,
                alphaReference);

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
                PSpecializationInfo = &specialization,
            };

            // The same vertex buffer the world draws from: the position, and the texture coordinate the
            // cut-out test needs.
            VertexInputBindingDescription* bindings = stackalloc VertexInputBindingDescription[2];
            bindings[0] = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };

            // The previous pose, so a body casts from the position it is drawn in rather than from the
            // last pose the simulation happened to have finished.
            bindings[1] = new VertexInputBindingDescription
            {
                Binding = 1,
                Stride = (uint)Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };
            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[6];
            attributes[0] = new VertexInputAttributeDescription
            {
                Location = 0,
                Binding = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = 0,
            };
            attributes[1] = new VertexInputAttributeDescription
            {
                Location = 2,
                Binding = 0,
                Format = Format.R32G32Sfloat,
                Offset = 24,
            };
            attributes[2] = new VertexInputAttributeDescription
            {
                Location = 4,
                Binding = 0,
                Format = Format.R8G8B8A8Unorm,
                Offset = (uint)Vertex3D.ColourOffset,
            };

            // The terrain's morph offset, and the texture offset that travels with it.
            attributes[3] = new VertexInputAttributeDescription
            {
                Location = 1,
                Binding = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = 12,
            };
            attributes[4] = new VertexInputAttributeDescription
            {
                Location = 3,
                Binding = 0,
                Format = Format.R32G32Sfloat,
                Offset = 32,
            };

            // The previous pose's position. A depth-only pass needs no normal from it.
            attributes[5] = new VertexInputAttributeDescription
            {
                Location = 5,
                Binding = 1,
                Format = Format.R32G32B32Sfloat,
                Offset = 0,
            };
            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindings,
                VertexAttributeDescriptionCount = 6,
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
            VkCheck.Ok(_vk.CreateGraphicsPipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline), "vkCreateGraphicsPipelines (shadow)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(vertexEntryPoint);
            SilkMarshal.Free(fragmentEntryPoint);
        }
    }
}
