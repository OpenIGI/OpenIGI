using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.SurfaceCache;

/// <summary>
/// The four images the world's surfaces are captured into, and the pass that draws into them.
/// </summary>
/// <remarks>
/// <para>
/// Lighting a room from bounced light means knowing how bright every surface in it is, including the ones
/// off screen and behind the camera. Re-shading those surfaces per ray is far too expensive, so instead
/// every model is photographed once, flat, from up to six directions — those photographs are cards, and
/// they live here. A ray that hits a wall does not shade the wall; it looks up the wall's page.
/// </para>
/// <para>
/// Four targets, because a photograph of the material needs four things: what colour it is, which way it
/// faces, how it responds to light, and how far behind the card's window it sits. The last is what lets a
/// reader tell whether the surface it hit is the one the card recorded, or something else that happens to
/// project to the same place.
/// </para>
/// <para>
/// The images are storage images rather than plain render targets, and are left in the general layout for
/// their whole life. That is deliberate: they are drawn into by a raster pass, then sampled by compute
/// every frame after, and the general layout is legal for both. The alternative is a transition between
/// every use, to buy back a little bandwidth on an image that is written once at load and read forever.
/// </para>
/// <para>
/// Capture happens at load, not in a frame, so it records into its own command buffer and waits. That
/// blocks — for as long as it takes to draw every card once — which is the right trade for something that
/// happens once per level and that everything afterwards depends on.
/// </para>
/// </remarks>
internal sealed unsafe class CardAtlas : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanImage _depthBuffer;
    private readonly Framebuffer _framebuffer;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="CardAtlas"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the atlas lives on.</param>
    /// <param name="allocator">The allocator backing it.</param>
    /// <param name="commands">The pool the initial layout transitions come from.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    /// <param name="albedo">The colour target, from <see cref="CreateTarget"/>.</param>
    /// <param name="normal">The normal target.</param>
    /// <param name="material">The material target.</param>
    /// <param name="depth">The card-depth target.</param>
    /// <remarks>
    /// The four targets are made by the caller and owned by it, not by this. They are registered with the
    /// device so that compute can bind them by handle for the rest of the level, and the device disposes
    /// everything it has registered; disposing them here as well would free them twice.
    /// </remarks>
    public CardAtlas(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int width,
        int height,
        VulkanStorageImage albedo,
        VulkanStorageImage normal,
        VulkanStorageImage material,
        VulkanStorageImage depth)
    {
        _vk = vk;
        _device = device.Device;
        Width = width;
        Height = height;
        Albedo = albedo;
        Normal = normal;
        Material = material;
        Depth = depth;

        // Depth testing only; nothing reads this afterwards, since the linear depth a card wants was
        // written to its own target by the fragment stage.
        _depthBuffer = new VulkanImage(
            vk, device, allocator, commands, Format.D32Sfloat, (uint)width, (uint)height, 1,
            ImageUsageFlags.DepthStencilAttachmentBit, ImageLayout.DepthStencilAttachmentOptimal,
            ImageAspectFlags.DepthBit);

        RenderPass = CreateRenderPass();
        _framebuffer = CreateFramebuffer();
    }

    /// <summary>Gets the atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the atlas height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the surface colour, unlit, with coverage in the fourth channel.</summary>
    public VulkanStorageImage Albedo { get; }

    /// <summary>Gets the surface normal in the model's own space.</summary>
    public VulkanStorageImage Normal { get; }

    /// <summary>Gets roughness, metalness and emissiveness.</summary>
    public VulkanStorageImage Material { get; }

    /// <summary>Gets how far behind the card's window the surface sits, as a fraction of the card's depth.</summary>
    public VulkanStorageImage Depth { get; }

    /// <summary>Gets the render pass the capture draws through.</summary>
    public RenderPass RenderPass { get; }

    /// <summary>Opens the capture, clearing every target including the gutters between pages.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <remarks>
    /// Cleared to zero, so coverage is zero everywhere no triangle reaches. The depth buffer clears to one
    /// rather than zero: a card's depth runs the ordinary way, near at zero and far at one, so the value
    /// standing for "nothing here yet" is the far plane.
    /// </remarks>
    public void Begin(CommandBuffer cmd)
    {
        ClearValue* clears = stackalloc ClearValue[5];
        for (int i = 0; i < 4; i++)
        {
            clears[i] = new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) };
        }

        clears[4] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1f, 0) };

        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = RenderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)Width, (uint)Height)),
            ClearValueCount = 5,
            PClearValues = clears,
        };

        _vk.CmdBeginRenderPass(cmd, &beginInfo, SubpassContents.Inline);
    }

    /// <summary>Aims the following draws at one page of the atlas.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="x">The page's left edge.</param>
    /// <param name="y">The page's top edge.</param>
    /// <param name="width">The page width.</param>
    /// <param name="height">The page height.</param>
    /// <remarks>
    /// Both the viewport and the scissor, not just the viewport. The viewport decides where the card's
    /// clip space lands; the scissor is what stops a model drawn wider than its own card from writing over
    /// the page next to it.
    /// </remarks>
    public void SetPage(CommandBuffer cmd, int x, int y, int width, int height)
    {
        Silk.NET.Vulkan.Viewport viewport = new()
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MinDepth = 0f,
            MaxDepth = 1f,
        };
        _vk.CmdSetViewport(cmd, 0, 1, &viewport);

        Rect2D scissor = new(new Offset2D(x, y), new Extent2D((uint)width, (uint)height));
        _vk.CmdSetScissor(cmd, 0, 1, &scissor);
    }

    /// <summary>Closes the capture.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    public void End(CommandBuffer cmd) => _vk.CmdEndRenderPass(cmd);

    /// <summary>Gets one of the four targets by the index the engine names it with.</summary>
    /// <param name="attachment">Zero albedo, one normal, two material, three depth.</param>
    /// <returns>The image, or null when the index names no target.</returns>
    public VulkanStorageImage? Target(int attachment) => attachment switch
    {
        0 => Albedo,
        1 => Normal,
        2 => Material,
        3 => Depth,
        _ => null,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyFramebuffer(_device, _framebuffer, null);
        _vk.DestroyRenderPass(_device, RenderPass, null);
        _depthBuffer.Dispose();
    }

    /// <summary>Makes one of the four targets, at the format that target wants.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the image lives on.</param>
    /// <param name="allocator">The allocator backing it.</param>
    /// <param name="commands">The pool its initial transition comes from.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    /// <param name="attachment">Zero albedo, one normal, two material, three depth.</param>
    /// <returns>The image, ready to be both drawn into and sampled.</returns>
    /// <remarks>
    /// Ten bits for the normal, because the bounced light a normal steers is smooth and low-contrast and
    /// eight bits there bands visibly across a flat wall. A full float for depth, because that value is
    /// the whole basis of a reader's "is this the surface I hit" test and it is not worth being clever
    /// about. Every target is four bytes a texel, which is also what a readback assumes.
    /// </remarks>
    public static VulkanStorageImage CreateTarget(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int width,
        int height,
        int attachment)
    {
        Format? exact = attachment switch
        {
            1 => Format.A2B10G10R10UnormPack32,
            3 => Format.R32Sfloat,
            _ => null,
        };

        return new VulkanStorageImage(
            vk, device, allocator, commands, width, height, 1, VolumeFormat.Rgba8, exact,
            ImageUsageFlags.ColorAttachmentBit);
    }

    private RenderPass CreateRenderPass()
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[5];
        AttachmentReference* colours = stackalloc AttachmentReference[4];

        Format[] formats = [Albedo.VkFormat, Normal.VkFormat, Material.VkFormat, Depth.VkFormat];
        for (int i = 0; i < 4; i++)
        {
            attachments[i] = new AttachmentDescription
            {
                Format = formats[i],
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,

                // In and out in the general layout: these images live there so that compute can sample
                // them without a transition every frame for the rest of the level.
                InitialLayout = ImageLayout.General,
                FinalLayout = ImageLayout.General,
            };
            colours[i] = new AttachmentReference((uint)i, ImageLayout.General);
        }

        attachments[4] = new AttachmentDescription
        {
            Format = Format.D32Sfloat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.DepthStencilAttachmentOptimal,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        AttachmentReference depthReference = new(4, ImageLayout.DepthStencilAttachmentOptimal);

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 4,
            PColorAttachments = colours,
            PDepthStencilAttachment = &depthReference,
        };

        // The capture is submitted on its own and waited on, so the only ordering that needs stating is
        // against whatever compute reads the atlas afterwards.
        SubpassDependency* dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TopOfPipeBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = AccessFlags.ShaderReadBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 5,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };

        RenderPass pass;
        VkCheck.Ok(_vk.CreateRenderPass(_device, &createInfo, null, &pass), "vkCreateRenderPass (cards)");
        return pass;
    }

    private Framebuffer CreateFramebuffer()
    {
        ImageView* views = stackalloc ImageView[5];
        views[0] = Albedo.View;
        views[1] = Normal.View;
        views[2] = Material.View;
        views[3] = Depth.View;
        views[4] = _depthBuffer.View;

        FramebufferCreateInfo createInfo = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = RenderPass,
            AttachmentCount = 5,
            PAttachments = views,
            Width = (uint)Width,
            Height = (uint)Height,
            Layers = 1,
        };

        Framebuffer framebuffer;
        VkCheck.Ok(_vk.CreateFramebuffer(_device, &createInfo, null, &framebuffer), "vkCreateFramebuffer (cards)");
        return framebuffer;
    }
}
