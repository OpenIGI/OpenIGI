using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// The back-buffer render pass, its depth buffer, and the framebuffers that bind them to the
/// swapchain's images.
/// </summary>
/// <remarks>
/// <para>
/// A render pass describes the attachments a frame draws into and the layouts they move through; a
/// framebuffer binds one to concrete image views. The pass here has two attachments: a colour target
/// that clears on load, stores on end, and finishes in the layout the presentation engine reads, and a
/// depth target that clears on load and is discarded after — so beginning the pass is the frame's
/// colour <em>and</em> depth clear, and the world pass has the depth buffer it needs to occlude
/// correctly.
/// </para>
/// <para>
/// One depth image is kept per swapchain image rather than one shared: two frames in flight can be on
/// the GPU at once against different swapchain images, and a single depth buffer would be written by
/// both. The pass depends only on the formats, which a resize does not change, so it is created once;
/// the depth images and framebuffers depend on the extent and are rebuilt whenever the swapchain is.
/// </para>
/// </remarks>
internal sealed unsafe class PresentTarget : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;

    private Framebuffer[] _framebuffers = [];
    private Image[] _depthImages = [];
    private DeviceMemory[] _depthMemories = [];
    private ImageView[] _depthViews = [];
    private Extent2D _extent;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="PresentTarget"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pass and framebuffers live on.</param>
    /// <param name="allocator">The allocator backing the depth images.</param>
    /// <param name="swapchain">The swapchain to build framebuffers for.</param>
    /// <exception cref="GraphicsDeviceException">The render pass or framebuffers could not be created.</exception>
    public PresentTarget(Vk vk, VulkanDevice device, VulkanAllocator allocator, VulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(swapchain);
        _vk = vk;
        _device = device;
        _allocator = allocator;

        DepthFormat = ChooseDepthFormat();
        RenderPass = CreateRenderPass(swapchain.Format);
        Recreate(swapchain);
    }

    /// <summary>Gets the render pass compatible with the swapchain's colour format and the depth format.</summary>
    public RenderPass RenderPass { get; }

    /// <summary>Gets the depth format the pass and its images use.</summary>
    public Format DepthFormat { get; }

    /// <summary>Gets the framebuffer for one swapchain image.</summary>
    /// <param name="imageIndex">The swapchain image index.</param>
    /// <returns>The framebuffer bound to that image.</returns>
    public Framebuffer Framebuffer(uint imageIndex) => _framebuffers[imageIndex];

    /// <summary>Gets the extent the framebuffers were built at.</summary>
    public Extent2D Extent => _extent;

    /// <summary>Rebuilds the depth images and framebuffers for a swapchain, leaving the render pass in place.</summary>
    /// <param name="swapchain">The swapchain whose images the framebuffers bind to.</param>
    public void Recreate(VulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        DestroyFramebuffersAndDepth();

        _extent = swapchain.Extent;
        ReadOnlySpan<ImageView> views = swapchain.ImageViews;
        int count = views.Length;

        _depthImages = new Image[count];
        _depthMemories = new DeviceMemory[count];
        _depthViews = new ImageView[count];
        _framebuffers = new Framebuffer[count];

        ImageView* attachments = stackalloc ImageView[2];
        for (int i = 0; i < count; i++)
        {
            CreateDepthImage(i);

            attachments[0] = views[i];
            attachments[1] = _depthViews[i];
            FramebufferCreateInfo createInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = RenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = _extent.Width,
                Height = _extent.Height,
                Layers = 1,
            };

            Framebuffer framebuffer;
            VkCheck.Ok(_vk.CreateFramebuffer(_device.Device, &createInfo, null, &framebuffer), "vkCreateFramebuffer");
            _framebuffers[i] = framebuffer;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyFramebuffersAndDepth();
        _vk.DestroyRenderPass(_device.Device, RenderPass, null);
    }

    private Format ChooseDepthFormat()
    {
        // A 32-bit float depth is the widest-supported precise choice; the combined depth-stencil formats
        // are the fallback for the rare adapter that lacks it. Only depth is used — there is no stencil.
        ReadOnlySpan<Format> candidates =
        [
            Format.D32Sfloat,
            Format.D24UnormS8Uint,
            Format.D16Unorm,
        ];

        foreach (Format candidate in candidates)
        {
            _vk.GetPhysicalDeviceFormatProperties(_device.PhysicalDevice, candidate, out FormatProperties properties);
            if (properties.OptimalTilingFeatures.HasFlag(FormatFeatureFlags.DepthStencilAttachmentBit))
            {
                return candidate;
            }
        }

        throw new GraphicsDeviceException("No supported depth-attachment format was found.");
    }

    private void CreateDepthImage(int index)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = DepthFormat,
            Extent = new Extent3D(_extent.Width, _extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device.Device, &imageInfo, null, &image), "vkCreateImage");
        _depthImages[index] = image;

        _vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
        _depthMemories[index] = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device.Device, image, _depthMemories[index], 0), "vkBindImageMemory");

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = DepthFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device.Device, &viewInfo, null, &view), "vkCreateImageView");
        _depthViews[index] = view;
    }

    private RenderPass CreateRenderPass(Format colourFormat)
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = colourFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        AttachmentReference colourRef = new() { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        AttachmentReference depthRef = new() { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colourRef,
            PDepthStencilAttachment = &depthRef,
        };

        // Wait at colour output and at the depth tests before the pass writes either, so the render
        // begins only once the acquired image is free and the depth buffer's clear is ordered.
        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device.Device, &createInfo, null, &renderPass), "vkCreateRenderPass");
        return renderPass;
    }

    private void DestroyFramebuffersAndDepth()
    {
        foreach (Framebuffer framebuffer in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device.Device, framebuffer, null);
        }

        foreach (ImageView view in _depthViews)
        {
            _vk.DestroyImageView(_device.Device, view, null);
        }

        for (int i = 0; i < _depthImages.Length; i++)
        {
            _vk.DestroyImage(_device.Device, _depthImages[i], null);
            _allocator.Free(_depthMemories[i]);
        }

        _framebuffers = [];
        _depthViews = [];
        _depthImages = [];
        _depthMemories = [];
    }
}
