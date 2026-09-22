using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Meshes;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>One moving draw whose screen motion must be stamped into the velocity image.</summary>
/// <param name="Mesh">The mesh that was drawn.</param>
/// <param name="IndexOffset">The first index of the drawn range.</param>
/// <param name="IndexCount">How many indices the range drew.</param>
/// <param name="Current">This frame's raster model-view-projection, including its jitter.</param>
/// <param name="Previous">
/// The preceding frame's unjittered transform with the current jitter applied, so subtracting the two
/// clip positions removes jitter while raster coverage still matches this frame.
/// </param>
/// <param name="FirstPerson">Whether the draw used the foreground depth-range sliver.</param>
/// <param name="PoseBlend">
/// How much of the previous-pose stream is in the position the G-buffer drew — the same blend, or this
/// pass rasterises a shape the depth test will reject.
/// </param>
/// <param name="PreviousPoseBlend">
/// The blend that reconstructs LAST frame's position from the same two streams. The difference between
/// the two is the whole of the pose motion; equal blends report rigid motion alone.
/// </param>
internal readonly record struct ObjectVelocityDraw(
    VulkanMesh Mesh,
    int IndexOffset,
    int IndexCount,
    Matrix4f Current,
    Matrix4f Previous,
    bool FirstPerson,
    float PoseBlend,
    float PreviousPoseBlend);

/// <summary>
/// A raster pass that overwrites the computed velocity with each MOVING object's own screen motion.
/// </summary>
/// <remarks>
/// <para>
/// The velocity compute derives motion from depth and the two cameras, which is exact for everything
/// that stands still — and silently wrong for everything that does not. A running soldier reprojects as
/// though he were part of the wall behind him, so the temporal accumulation blends his history from
/// where he was not: the reported "enemies are blurry when they move". This pass redraws each animated
/// mesh over the velocity image with this frame's and the previous frame's transforms, writing the true
/// screen delta wherever the mesh actually won the depth test.
/// </para>
/// <para>
/// Every mesh binds its preceding pose as a second vertex stream, so limb and hand motion is measured
/// as well as rigid translation. What each frame reports is not the whole of a tick's motion but the
/// part of it that fell between the two frames: the drawn position is a blend of the two poses and the
/// caller says which blend named last frame's, so a body drawn a third of the way between two ticks
/// reports a third of the step. A mesh that does not animate reports one buffer as both streams and
/// its pose motion is zero by construction.
/// </para>
/// <para>
/// The pass rasterises with the exact transform the G-buffer draw used (jitter and all) so the depths
/// match. The previous clip transform carries the same current jitter, which cancels from the reported
/// vector and keeps motion in the unjittered convention every temporal consumer uses. It tests
/// GREATER-OR-EQUAL with a whisker of bias toward the eye and writes no depth. Occluded parts of the mesh
/// fail the test and leave the background's motion alone.
/// </para>
/// </remarks>
internal sealed unsafe class ObjectVelocityPass : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;

    // The device's own pipeline cache, copied here because this class keeps the raw handle
    // rather than the wrapper. Passing it is what lets the driver return a previous run's
    // compiled pipeline instead of building this one again.
    private readonly PipelineCache _pipelineCache;
    private readonly VulkanShaderModule _vertex;
    private readonly VulkanShaderModule _fragment;
    private readonly RenderPass _renderPass;
    private readonly PipelineLayout _layout;
    private readonly Pipeline _pipeline;
    private readonly Dictionary<(ulong Velocity, ulong Depth), Framebuffer> _framebuffers = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ObjectVelocityPass"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass lives on.</param>
    /// <param name="depthFormat">The scene depth attachment's format.</param>
    public ObjectVelocityPass(Vk vk, VulkanDevice device, Format depthFormat)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device.Device;
        _pipelineCache = device.PipelineCache;

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.ObjectVelocity);
        ReadOnlySpan<byte> spirv = ShaderCatalog.GetSpirv(ShaderModuleId.ObjectVelocity).Span;
        _vertex = new VulkanShaderModule(
            vk,
            _device,
            spirv,
            Vulkan.Shaders.ShaderStage.Vertex,
            reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Vertex).Name);
        _fragment = new VulkanShaderModule(
            vk,
            _device,
            spirv,
            Vulkan.Shaders.ShaderStage.Fragment,
            reflection.EntryPointFor(OpenIGI.Shaders.ShaderStage.Fragment).Name);

        _renderPass = CreateRenderPass(depthFormat);
        _layout = CreatePipelineLayout();
        _pipeline = CreatePipeline();
    }

    /// <summary>Records the pass: one draw per recorded moving mesh, over the velocity image.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="velocityView">The velocity image view, as a colour attachment.</param>
    /// <param name="velocityHandle">The velocity view's raw handle, keying the framebuffer cache.</param>
    /// <param name="depthView">The scene depth view, tested read-only.</param>
    /// <param name="depthHandle">The depth view's raw handle, keying the framebuffer cache.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="draws">The moving draws recorded this frame.</param>
    public void Record(
        CommandBuffer cmd,
        ImageView velocityView,
        ulong velocityHandle,
        ImageView depthView,
        ulong depthHandle,
        uint width,
        uint height,
        IReadOnlyList<ObjectVelocityDraw> draws)
    {
        Framebuffer framebuffer = GetFramebuffer(velocityView, velocityHandle, depthView, depthHandle, width, height);

        RenderPassBeginInfo begin = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height)),
            ClearValueCount = 0,
        };

        _vk.CmdBeginRenderPass(cmd, &begin, SubpassContents.Inline);
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);

        Rect2D scissor = new(new Offset2D(0, 0), new Extent2D(width, height));
        _vk.CmdSetScissor(cmd, 0, 1, &scissor);

        Span<float> push = stackalloc float[32];
        Buffer* vertexBuffers = stackalloc Buffer[2];
        ulong* vertexOffsets = stackalloc ulong[2];
        vertexOffsets[0] = 0;
        vertexOffsets[1] = 0;
        bool? viewportIsFirstPerson = null;
        foreach (ObjectVelocityDraw draw in draws)
        {
            // The view model was originally rasterised into the nearest depth sliver so it could retain
            // self-occlusion without a world wall cutting through it. Reproduce that viewport transform
            // here or its rerasterised depth cannot match the stored depth and the velocity stamp fails.
            if (viewportIsFirstPerson != draw.FirstPerson)
            {
                Silk.NET.Vulkan.Viewport viewport = new(
                    0f,
                    0f,
                    width,
                    height,
                    draw.FirstPerson ? VulkanGraphicsDevice.ForegroundDepthFloor : 0f,
                    1f);
                _vk.CmdSetViewport(cmd, 0, 1, &viewport);
                viewportIsFirstPerson = draw.FirstPerson;
            }

            draw.Current.CopyColumnMajorTo(push[..16]);

            // The previous transform's x, y and w rows; its z row is the one the vector never asks for.
            Matrix4f was = draw.Previous;
            push[16] = was.M11; push[17] = was.M12; push[18] = was.M13; push[19] = was.M14;
            push[20] = was.M21; push[21] = was.M22; push[22] = was.M23; push[23] = was.M24;
            push[24] = was.M41; push[25] = was.M42; push[26] = was.M43; push[27] = was.M44;

            push[28] = draw.PoseBlend;
            push[29] = draw.PreviousPoseBlend;
            push[30] = 0f;
            push[31] = 0f;
            fixed (float* data = push)
            {
                _vk.CmdPushConstants(
                    cmd, _layout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 128, data);
            }

            vertexBuffers[0] = draw.Mesh.VertexBuffer;
            vertexBuffers[1] = draw.Mesh.PreviousVertexBuffer;
            _vk.CmdBindVertexBuffers(cmd, 0, 2, vertexBuffers, vertexOffsets);
            _vk.CmdBindIndexBuffer(cmd, draw.Mesh.IndexBuffer, 0, IndexType.Uint16);
            _vk.CmdDrawIndexed(cmd, (uint)draw.IndexCount, 1, (uint)draw.IndexOffset, 0, 0);
        }

        _vk.CmdEndRenderPass(cmd);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Framebuffer framebuffer in _framebuffers.Values)
        {
            _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        _framebuffers.Clear();
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _layout, null);
        _vk.DestroyRenderPass(_device, _renderPass, null);
        _fragment.Dispose();
        _vertex.Dispose();
    }

    /// <summary>Drops every cached framebuffer, for a resize that recreated the views they point at.</summary>
    public void InvalidateFramebuffers()
    {
        foreach (Framebuffer framebuffer in _framebuffers.Values)
        {
            _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        _framebuffers.Clear();
    }

    private Framebuffer GetFramebuffer(
        ImageView velocityView, ulong velocityHandle, ImageView depthView, ulong depthHandle, uint width, uint height)
    {
        (ulong, ulong) key = (velocityHandle, depthHandle);
        if (_framebuffers.TryGetValue(key, out Framebuffer existing))
        {
            return existing;
        }

        ImageView* attachments = stackalloc ImageView[2] { velocityView, depthView };
        FramebufferCreateInfo createInfo = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = width,
            Height = height,
            Layers = 1,
        };

        Framebuffer framebuffer;
        VkCheck.Ok(_vk.CreateFramebuffer(_device, &createInfo, null, &framebuffer), "vkCreateFramebuffer (object velocity)");
        _framebuffers[key] = framebuffer;
        return framebuffer;
    }

    private RenderPass CreateRenderPass(Format depthFormat)
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];

        // The velocity image lives its whole life in General — written by compute before this pass and
        // read by compute after it — so the pass loads and stores it there rather than transitioning.
        attachments[0] = new AttachmentDescription
        {
            Format = Format.R16G16Sfloat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.General,
            FinalLayout = ImageLayout.General,
        };

        // The depth is read-only here: tested, never written, and handed back in the sampled layout the
        // compute passes around this one expect.
        attachments[1] = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ShaderReadOnlyOptimal,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        AttachmentReference colour = new(0, ImageLayout.General);
        AttachmentReference depth = new(1, ImageLayout.DepthStencilReadOnlyOptimal);

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colour,
            PDepthStencilAttachment = &depth,
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[2];

        // The compute velocity write, and every earlier read of the depth, before this pass's use.
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ShaderWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit
                | PipelineStageFlags.EarlyFragmentTestsBit
                | PipelineStageFlags.LateFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit,
        };

        // This pass's colour write before the dilation's compute read.
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ComputeShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device, &createInfo, null, &renderPass), "vkCreateRenderPass (object velocity)");
        return renderPass;
    }

    private PipelineLayout CreatePipelineLayout()
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
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };

        PipelineLayout layout;
        VkCheck.Ok(_vk.CreatePipelineLayout(_device, &createInfo, null, &layout), "vkCreatePipelineLayout (object velocity)");
        return layout;
    }

    private Pipeline CreatePipeline()
    {
        nint vertexEntry = SilkMarshal.StringToPtr(_vertex.EntryPoint, NativeStringEncoding.UTF8);
        nint fragmentEntry = SilkMarshal.StringToPtr(_fragment.EntryPoint, NativeStringEncoding.UTF8);
        try
        {
            PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertex.Handle,
                PName = (byte*)vertexEntry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragment.Handle,
                PName = (byte*)fragmentEntry,
            };

            // Current and previous positions come from whole-pose streams; the rest of each Vertex3D
            // stride is stepped over. Static meshes simply bind the same buffer twice.
            VertexInputBindingDescription* vertexBindings = stackalloc VertexInputBindingDescription[2];
            vertexBindings[0] = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)Rendering.Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };
            vertexBindings[1] = new VertexInputBindingDescription
            {
                Binding = 1,
                Stride = (uint)Rendering.Vertex3D.Size,
                InputRate = VertexInputRate.Vertex,
            };
            VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[2];
            attributes[0] = new VertexInputAttributeDescription
            {
                Location = 0,
                Binding = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = 0,
            };
            attributes[1] = new VertexInputAttributeDescription
            {
                Location = 1,
                Binding = 1,
                Format = Format.R32G32B32Sfloat,
                Offset = 0,
            };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = vertexBindings,
                VertexAttributeDescriptionCount = 2,
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

            // No culling: the G-buffer's cull state varies per draw, and a face the depth test rejects
            // costs nothing, while a face wrongly culled here leaves a hole in the object's motion.
            // The bias nudges each fragment a whisker toward the eye (reverse-Z: greater is nearer), so
            // the GREATER-OR-EQUAL test cannot lose its own surface to a rounding difference.
            PipelineRasterizationStateCreateInfo rasterization = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1.0f,
                DepthBiasEnable = true,
                DepthBiasConstantFactor = 4f,
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
                DepthWriteEnable = false,
                DepthCompareOp = CompareOp.GreaterOrEqual,
            };

            PipelineColorBlendAttachmentState blendAttachment = new()
            {
                BlendEnable = false,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit,
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
                Layout = _layout,
                RenderPass = _renderPass,
                Subpass = 0,
            };

            Pipeline pipeline;
            VkCheck.Ok(
                _vk.CreateGraphicsPipelines(_device, _pipelineCache, 1, &createInfo, null, &pipeline),
                "vkCreateGraphicsPipelines (object velocity)");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(vertexEntry);
            SilkMarshal.Free(fragmentEntry);
        }
    }
}
