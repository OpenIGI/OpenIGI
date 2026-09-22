using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>The raster half: graphics pipelines, built without a render pass object.</summary>
/// <remarks>
/// <para>
/// Through dynamic rendering, which is what makes the interface's render pass implementable at all. The
/// interface states its attachments when the pass begins, the way WebGPU does; classic Vulkan wants a
/// render pass object and a framebuffer built in advance from the same information, and keeping those
/// in step with attachments that change per frame is a cache and an invalidation rule and a source of
/// bugs. Dynamic rendering takes the attachments directly, so there is nothing to keep in step.
/// </para>
/// <para>
/// It is Vulkan 1.3, and a device without it is refused rather than served by a second path. Two ways of
/// beginning a pass would be two ways for the picture to differ.
/// </para>
/// </remarks>
internal sealed unsafe partial class VulkanRhiDevice
{
    /// <inheritdoc/>
    public IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        RequireDynamicRendering();

        using Utf8String vertexEntry = new(descriptor.Vertex.EntryPoint);
        using Utf8String fragmentEntry = new(descriptor.Fragment?.EntryPoint ?? "main");

        int attributeCount = 0;
        foreach (RhiVertexBufferLayout buffer in descriptor.VertexBuffers)
        {
            attributeCount += buffer.Attributes.Count;
        }

        VertexInputAttributeDescription[] attributes =
            new VertexInputAttributeDescription[Math.Max(attributeCount, 1)];

        VertexInputBindingDescription[] bindings =
            new VertexInputBindingDescription[Math.Max(descriptor.VertexBuffers.Count, 1)];

        PipelineColorBlendAttachmentState[] blends =
            new PipelineColorBlendAttachmentState[Math.Max(descriptor.ColorTargets.Count, 1)];

        Format[] colourFormats = new Format[Math.Max(descriptor.ColorTargets.Count, 1)];
        PipelineShaderStageCreateInfo[] stages = new PipelineShaderStageCreateInfo[descriptor.Fragment is null ? 1 : 2];

        int written = 0;
        for (int i = 0; i < descriptor.VertexBuffers.Count; i++)
        {
            RhiVertexBufferLayout buffer = descriptor.VertexBuffers[i];
            bindings[i] = new VertexInputBindingDescription
            {
                Binding = (uint)i,
                Stride = (uint)buffer.Stride,
                InputRate = VulkanRhiTranslation.StepMode(buffer.StepMode),
            };

            foreach (RhiVertexElement element in buffer.Attributes)
            {
                attributes[written++] = new VertexInputAttributeDescription
                {
                    Location = (uint)element.Location,
                    Binding = (uint)i,
                    Format = VulkanRhiTranslation.VertexFormat(element.Format),
                    Offset = (uint)element.Offset,
                };
            }
        }

        for (int i = 0; i < descriptor.ColorTargets.Count; i++)
        {
            RhiColorTargetState target = descriptor.ColorTargets[i];
            colourFormats[i] = VulkanRhiTranslation.TextureFormat(target.Format);

            blends[i] = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = VulkanRhiTranslation.ColorWrite(target.Write),
                BlendEnable = target.Blend is not null,
            };

            if (target.Blend is { } blend)
            {
                blends[i].SrcColorBlendFactor = VulkanRhiTranslation.BlendFactor(blend.Color.Source);
                blends[i].DstColorBlendFactor = VulkanRhiTranslation.BlendFactor(blend.Color.Destination);
                blends[i].ColorBlendOp = VulkanRhiTranslation.BlendOperation(blend.Color.Operation);
                blends[i].SrcAlphaBlendFactor = VulkanRhiTranslation.BlendFactor(blend.Alpha.Source);
                blends[i].DstAlphaBlendFactor = VulkanRhiTranslation.BlendFactor(blend.Alpha.Destination);
                blends[i].AlphaBlendOp = VulkanRhiTranslation.BlendOperation(blend.Alpha.Operation);
            }
        }

        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = ((VulkanRhiShaderModule)descriptor.Vertex.Module).Handle,
            PName = vertexEntry.Pointer,
        };

        if (descriptor.Fragment is { } fragment)
        {
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = ((VulkanRhiShaderModule)fragment.Module).Handle,
                PName = fragmentEntry.Pointer,
            };
        }

        // The viewport and the scissor are set on the pass, so one pipeline survives a resize. Vulkan
        // still wants a count here, and dynamic state is what says the values come later.
        DynamicState* dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };

        fixed (VertexInputAttributeDescription* firstAttribute = attributes)
        fixed (VertexInputBindingDescription* firstBinding = bindings)
        fixed (PipelineColorBlendAttachmentState* firstBlend = blends)
        fixed (Format* firstFormat = colourFormats)
        fixed (PipelineShaderStageCreateInfo* firstStage = stages)
        {
            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = (uint)descriptor.VertexBuffers.Count,
                PVertexBindingDescriptions = descriptor.VertexBuffers.Count > 0 ? firstBinding : null,
                VertexAttributeDescriptionCount = (uint)attributeCount,
                PVertexAttributeDescriptions = attributeCount > 0 ? firstAttribute : null,
            };

            PipelineInputAssemblyStateCreateInfo assembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = VulkanRhiTranslation.Topology(descriptor.Primitive.Topology),
            };

            PipelineViewportStateCreateInfo viewport = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };

            PipelineRasterizationStateCreateInfo rasterisation = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = VulkanRhiTranslation.CullMode(descriptor.Primitive.CullMode),
                FrontFace = VulkanRhiTranslation.FrontFace(descriptor.Primitive.FrontFace),
                LineWidth = 1f,
                DepthBiasEnable = descriptor.DepthStencil is { DepthBiasConstant: not 0f }
                    or { DepthBiasSlopeScale: not 0f },
                DepthBiasConstantFactor = descriptor.DepthStencil?.DepthBiasConstant ?? 0f,
                DepthBiasSlopeFactor = descriptor.DepthStencil?.DepthBiasSlopeScale ?? 0f,
            };

            PipelineMultisampleStateCreateInfo multisample = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = (SampleCountFlags)Math.Max(1, descriptor.SampleCount),
            };

            PipelineDepthStencilStateCreateInfo depth = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = descriptor.DepthStencil is not null,
                DepthWriteEnable = descriptor.DepthStencil?.DepthWrite ?? false,
                DepthCompareOp = descriptor.DepthStencil is { } state
                    ? VulkanRhiTranslation.Compare(state.DepthCompare)
                    : CompareOp.Always,
                MaxDepthBounds = 1f,
            };

            PipelineColorBlendStateCreateInfo blending = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = (uint)descriptor.ColorTargets.Count,
                PAttachments = descriptor.ColorTargets.Count > 0 ? firstBlend : null,
            };

            PipelineDynamicStateCreateInfo dynamic = new()
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };

            // What the pass will attach, stated as formats rather than as a render pass object.
            PipelineRenderingCreateInfo rendering = new()
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = (uint)descriptor.ColorTargets.Count,
                PColorAttachmentFormats = descriptor.ColorTargets.Count > 0 ? firstFormat : null,
                DepthAttachmentFormat = descriptor.DepthStencil is { } target
                    ? VulkanRhiTranslation.TextureFormat(target.Format)
                    : Format.Undefined,
                StencilAttachmentFormat = descriptor.DepthStencil is { } stencil && stencil.Format.HasStencil()
                    ? VulkanRhiTranslation.TextureFormat(stencil.Format)
                    : Format.Undefined,
            };

            GraphicsPipelineCreateInfo info = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &rendering,
                StageCount = (uint)stages.Length,
                PStages = firstStage,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &assembly,
                PViewportState = &viewport,
                PRasterizationState = &rasterisation,
                PMultisampleState = &multisample,
                PDepthStencilState = &depth,
                PColorBlendState = &blending,
                PDynamicState = &dynamic,
                Layout = ((VulkanRhiPipelineLayout)descriptor.Layout).Handle,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device.Device, _device.PipelineCache, 1, &info, null, &pipeline),
                "vkCreateGraphicsPipelines");

            return new VulkanRhiRenderPipeline(_vk, _device.Device, pipeline, descriptor.Layout, descriptor.Label);
        }
    }

    /// <summary>Refuses a device that cannot begin a pass the way the interface describes one.</summary>
    /// <exception cref="GraphicsDeviceException">The device has no dynamic rendering.</exception>
    internal void RequireDynamicRendering()
    {
        if (!_device.Capabilities.DynamicRendering)
        {
            throw new GraphicsDeviceException(
                "This device has no dynamic rendering, which the shared render path is built on. It needs "
                + "Vulkan 1.3 or the extension; a second path through render pass objects would be a "
                + "second way for the picture to differ.");
        }
    }
}

/// <summary>A graphics pipeline.</summary>
internal sealed unsafe class VulkanRhiRenderPipeline : IRhiRenderPipeline
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiRenderPipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The pipeline.</param>
    /// <param name="layout">The layout it was built against.</param>
    /// <param name="label">The name it was created with.</param>
    public VulkanRhiRenderPipeline(
        Vk vk,
        Device device,
        Pipeline handle,
        IRhiPipelineLayout layout,
        string label)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Layout = layout;
        Label = label;
    }

    /// <summary>Gets the pipeline.</summary>
    public Pipeline Handle { get; }

    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyPipeline(_device, Handle, null);
    }
}
