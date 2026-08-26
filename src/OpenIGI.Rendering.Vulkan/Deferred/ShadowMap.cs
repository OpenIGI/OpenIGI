using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The sun's cascaded shadow map: one depth array image whose layers are the cascades, a depth-only
/// render pass and a framebuffer per layer to render them, and a comparison sampler the lighting reads
/// them through.
/// </summary>
/// <remarks>
/// <para>
/// Each cascade is a slice of the view distance fitted with its own orthographic light view; the near
/// slices get their texels where the player is looking. They share one array image so the lighting binds
/// a single <c>sampler2DArrayShadow</c> and selects the layer per pixel. The comparison sampler does the
/// depth test in hardware and filters four taps at once, which is what makes percentage-closer filtering
/// cheap.
/// </para>
/// <para>
/// The map is rebuilt only when the resolution or cascade count changes; a camera move refits the same
/// images. Depth is stored standard (near 0), not the reverse-Z the main pass uses, because the shadow
/// projection is orthographic — reverse-Z buys nothing without perspective — and it keeps the lighting's
/// depth comparison a plain one.
/// </para>
/// </remarks>
internal sealed unsafe class ShadowMap : IDisposable
{
    /// <summary>The shadow depth format: 32-bit float, matching the scene depth.</summary>
    public const Format DepthFormat = Format.D32Sfloat;

    private const int MaxCascades = 4;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly RenderPass _renderPass;
    private readonly Sampler _sampler;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _arrayView;
    private readonly ImageView[] _layerViews = new ImageView[MaxCascades];
    private readonly Framebuffer[] _framebuffers = new Framebuffer[MaxCascades];
    private int _resolution;
    private int _cascades;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShadowMap"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the map lives on.</param>
    /// <param name="allocator">The allocator backing the depth image.</param>
    /// <param name="commands">The pool the initial layout transition comes from.</param>
    public ShadowMap(Vk vk, VulkanDevice device, VulkanAllocator allocator, VulkanCommands commands)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _renderPass = CreateRenderPass();
        _sampler = CreateSampler();

        // A 1x1 single-cascade placeholder so the array view is always valid to bind, even before shadows
        // are configured; the lighting ignores it while the cascade count it is told is zero.
        Ensure(1, 1);
    }

    /// <summary>Gets a value indicating whether a map has been built and can be rendered or sampled.</summary>
    public bool Ready => _cascades > 0 && _image.Handle != 0;

    /// <summary>Gets the depth-only render pass the cascades render into.</summary>
    public RenderPass RenderPass => _renderPass;

    /// <summary>Gets the array view the lighting samples all cascades through.</summary>
    public ImageView ArrayView => _arrayView;

    /// <summary>Gets the comparison sampler the lighting reads the map with.</summary>
    public Sampler Sampler => _sampler;

    /// <summary>Gets the current resolution in texels.</summary>
    public int Resolution => _resolution;

    /// <summary>Gets the image handle, for the barrier that makes the rendered map visible to the lighting.</summary>
    public Image Image => _image;

    /// <summary>Ensures the map is built for a resolution and cascade count, rebuilding if either changed.</summary>
    /// <param name="resolution">The side length in texels of each cascade.</param>
    /// <param name="cascades">How many cascades.</param>
    public void Ensure(int resolution, int cascades)
    {
        cascades = Math.Clamp(cascades, 1, MaxCascades);
        resolution = Math.Max(1, resolution);
        if (_resolution == resolution && _cascades == cascades && _image.Handle != 0)
        {
            return;
        }

        _device.WaitIdle();
        DestroyImages();
        _resolution = resolution;
        _cascades = cascades;

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = DepthFormat,
            Extent = new Extent3D((uint)resolution, (uint)resolution, 1),
            MipLevels = 1,
            ArrayLayers = (uint)cascades,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device.Device, &imageInfo, null, &image), "vkCreateImage (shadow)");
        _image = image;

        _vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device.Device, image, _memory, 0), "vkBindImageMemory (shadow)");

        _arrayView = CreateView((uint)cascades, 0, ImageViewType.Type2DArray);
        for (int i = 0; i < cascades; i++)
        {
            _layerViews[i] = CreateView(1, (uint)i, ImageViewType.Type2D);
            _framebuffers[i] = CreateFramebuffer(_layerViews[i], (uint)resolution);
        }

        // Move every layer to the shader-read layout the lighting binds it in, so a cascade that has not
        // been rendered yet — the placeholder, or a frame before shadows are configured — is still a valid
        // descriptor rather than an undefined image the layer complains about.
        Image shadowImage = _image;
        int layers = cascades;
        _commands.SingleTime(cmd =>
        {
            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.ShaderReadOnlyOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = shadowImage,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = (uint)layers,
                },
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ShaderReadBit,
            };
            _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier);
        });
    }

    /// <summary>Gets the framebuffer for one cascade layer.</summary>
    /// <param name="cascade">The cascade index.</param>
    /// <returns>The framebuffer.</returns>
    public Framebuffer Framebuffer(int cascade) => _framebuffers[cascade];

    private ImageView CreateView(uint layerCount, uint baseLayer, ImageViewType type)
    {
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = type,
            Format = DepthFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = baseLayer,
                LayerCount = layerCount,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device.Device, &viewInfo, null, &view), "vkCreateImageView (shadow)");
        return view;
    }

    private Framebuffer CreateFramebuffer(ImageView view, uint size)
    {
        FramebufferCreateInfo createInfo = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = size,
            Height = size,
            Layers = 1,
        };

        Framebuffer framebuffer;
        VkCheck.Ok(_vk.CreateFramebuffer(_device.Device, &createInfo, null, &framebuffer), "vkCreateFramebuffer (shadow)");
        return framebuffer;
    }

    private RenderPass CreateRenderPass()
    {
        AttachmentDescription depth = new()
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal,
        };

        AttachmentReference depthRef = new() { Attachment = 0, Layout = ImageLayout.DepthStencilAttachmentOptimal };
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            PDepthStencilAttachment = &depthRef,
        };

        // Order the depth writes before the lighting's later sample, and the previous frame's sample
        // before this frame's writes.
        SubpassDependency* dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.FragmentShaderBit,
            SrcAccessMask = AccessFlags.ShaderReadBit,
            DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.FragmentShaderBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };

        RenderPassCreateInfo createInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &depth,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };

        RenderPass renderPass;
        VkCheck.Ok(_vk.CreateRenderPass(_device.Device, &createInfo, null, &renderPass), "vkCreateRenderPass (shadow)");
        return renderPass;
    }

    private Sampler CreateSampler()
    {
        // A comparison sampler: the fetch returns the filtered result of the depth test, not the depth, so
        // a bilinear fetch is four shadow tests filtered in hardware. Clamped to a white border so a sample
        // off the edge of a cascade reads as lit rather than as shadow.
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToBorder,
            AddressModeV = SamplerAddressMode.ClampToBorder,
            AddressModeW = SamplerAddressMode.ClampToBorder,
            BorderColor = BorderColor.FloatOpaqueWhite,
            CompareEnable = true,
            CompareOp = CompareOp.LessOrEqual,
            MinLod = 0,
            MaxLod = 0,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device.Device, &createInfo, null, &sampler), "vkCreateSampler (shadow)");
        return sampler;
    }

    private void DestroyImages()
    {
        for (int i = 0; i < MaxCascades; i++)
        {
            if (_framebuffers[i].Handle != 0)
            {
                _vk.DestroyFramebuffer(_device.Device, _framebuffers[i], null);
                _framebuffers[i] = default;
            }

            if (_layerViews[i].Handle != 0)
            {
                _vk.DestroyImageView(_device.Device, _layerViews[i], null);
                _layerViews[i] = default;
            }
        }

        if (_arrayView.Handle != 0)
        {
            _vk.DestroyImageView(_device.Device, _arrayView, null);
            _arrayView = default;
        }

        if (_image.Handle != 0)
        {
            _vk.DestroyImage(_device.Device, _image, null);
            _image = default;
        }

        if (_memory.Handle != 0)
        {
            _allocator.Free(_memory);
            _memory = default;
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
        DestroyImages();
        _vk.DestroySampler(_device.Device, _sampler, null);
        _vk.DestroyRenderPass(_device.Device, _renderPass, null);
    }
}
