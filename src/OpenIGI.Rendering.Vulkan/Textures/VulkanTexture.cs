using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>A sampled two-dimensional texture: its image, the memory behind it, a view, and a sampler.</summary>
/// <remarks>
/// <para>
/// The engine hands textures over as <see cref="Bgra32"/> — blue, green, red, alpha in memory — so the
/// image is created in the matching <see cref="Format.B8G8R8A8Unorm"/> and the whole mip chain copies
/// across without a swizzle. Upload goes through a host-visible staging buffer and a one-off command
/// that moves the image from undefined to transfer-destination, copies each level, and leaves it in the
/// shader-read layout the sampler needs.
/// </para>
/// <para>
/// The sampler is baked from the description's filter and address mode. That is the common case the 2D
/// path and most models want; the anisotropic-quality override the seam also exposes rebuilds the
/// sampler and is layered on top later.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanTexture : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly DeviceMemory _memory;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanTexture"/> class and uploads its pixels.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the texture lives on.</param>
    /// <param name="allocator">The allocator backing the image and its staging buffer.</param>
    /// <param name="commands">The pool the upload's one-off command comes from.</param>
    /// <param name="description">The texture's size, mip count and sampling.</param>
    /// <param name="pixels">The whole mip chain, largest level first, each level tightly packed.</param>
    /// <param name="mipLodBias">
    /// How far to bias the level-of-detail choice, negative for sharper. The reference's automatic view
    /// mip bias, which exists because its temporal filter recovers detail the rule assumes is gone.
    /// </param>
    /// <param name="maxAnisotropy">
    /// The sampler's anisotropic ratio; one leaves anisotropy off. Ignored where the device lacks the
    /// feature, and on point-sampled or unmipped textures, which gain nothing from it.
    /// </param>
    /// <exception cref="GraphicsDeviceException">The texture could not be created or uploaded.</exception>
    public VulkanTexture(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        in TextureDescription description,
        ReadOnlySpan<Bgra32> pixels,
        float maxAnisotropy = 1f,
        float mipLodBias = 0f)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);

        _vk = vk;
        _device = device.Device;
        _allocator = allocator;
        Width = description.Width;
        Height = description.Height;
        MipLevels = Math.Max(1, description.MipLevels);
        CutoutFraction = MeasureCutoutFraction(pixels, Width, Height);
        HasCutoutAlpha = CutoutFraction > CutoutTexelFraction;

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.B8G8R8A8Unorm,
            Extent = new Extent3D((uint)Width, (uint)Height, 1),
            MipLevels = (uint)MipLevels,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device, &imageInfo, null, &image), "vkCreateImage");
        Image = image;

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device, image, _memory, 0), "vkBindImageMemory");

        Upload(device, allocator, commands, description, pixels);

        View = CreateView();
        Sampler = CreateSampler(
            description,
            device.SamplerAnisotropy ? maxAnisotropy : 1f,
            device.SamplerMipLodBias ? mipLodBias : 0f);
    }

    /// <summary>Gets the image handle.</summary>
    public Image Image { get; }

    /// <summary>Gets the image view the sampler reads through.</summary>
    public ImageView View { get; }

    /// <summary>Gets the sampler bound alongside the view.</summary>
    public Sampler Sampler { get; }

    /// <summary>Gets the width of mip level 0 in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of mip level 0 in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of mip levels.</summary>
    public int MipLevels { get; }

    /// <summary>
    /// Gets whether the alpha test would punch holes in this texture — whether a meaningful share of its
    /// texels fail the shaders' <c>alpha &lt; 0.5</c> cut.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured from the texels rather than inferred from the file's name. The material library's only
    /// rule for this is a name match on <c>*_argb8888</c>, and a wire fence whose texture is not named
    /// that way was being handed to the ray-traced world as a solid occluder — where it cast the shadow
    /// of a wall across the yard, because a ray query has no per-texel test and every triangle it holds
    /// blocks the sun completely.
    /// </para>
    /// <para>
    /// The test is deliberately the same one the shadow and G-buffer fragments apply, so the two paths
    /// cannot disagree about what a surface is: anything the rasteriser would cut holes in is something
    /// the trace must not treat as solid.
    /// </para>
    /// </remarks>
    public bool HasCutoutAlpha { get; }

    /// <summary>Gets the share of top-level texels that fail the shaders' alpha cut.</summary>
    public float CutoutFraction { get; }

    /// <summary>Replaces the pixels of one mip level through a staging buffer.</summary>
    /// <param name="device">The device the staging buffer is created on.</param>
    /// <param name="allocator">The allocator backing the staging buffer.</param>
    /// <param name="commands">The pool the one-off upload command comes from.</param>
    /// <param name="mipLevel">The zero-based mip level to replace.</param>
    /// <param name="pixels">The replacement pixels for that level, tightly packed.</param>
    /// <exception cref="GraphicsDeviceException">The level is out of range or the pixel count is wrong.</exception>
    public void UpdateLevel(
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int mipLevel,
        ReadOnlySpan<Bgra32> pixels)
    {
        if (mipLevel < 0 || mipLevel >= MipLevels)
        {
            throw new GraphicsDeviceException($"Mip level {mipLevel} is outside a {MipLevels}-level texture.");
        }

        int levelWidth = Math.Max(1, Width >> mipLevel);
        int levelHeight = Math.Max(1, Height >> mipLevel);
        if (pixels.Length < levelWidth * levelHeight)
        {
            throw new GraphicsDeviceException(
                $"Mip level {mipLevel} needs {levelWidth * levelHeight} pixels but got {pixels.Length}.");
        }

        using VulkanBuffer staging = new(
            _vk,
            device,
            allocator,
            (ulong)(levelWidth * levelHeight * Bgra32.Size),
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write(pixels[..(levelWidth * levelHeight)]);

        Image image = Image;
        Buffer stagingHandle = staging.Handle;
        int level = mipLevel;
        commands.SingleTime(cmd =>
        {
            TransitionLayout(
                cmd,
                image,
                ImageLayout.ShaderReadOnlyOptimal,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.FragmentShaderBit,
                PipelineStageFlags.TransferBit,
                AccessFlags.ShaderReadBit,
                AccessFlags.TransferWriteBit);

            BufferImageCopy region = new()
            {
                BufferOffset = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = (uint)level,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D((uint)levelWidth, (uint)levelHeight, 1),
            };
            _vk.CmdCopyBufferToImage(cmd, stagingHandle, image, ImageLayout.TransferDstOptimal, 1, &region);

            TransitionLayout(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit);
        });
    }

    /// <summary>Records a device-side copy of another colour image's top-left region into mip level zero.</summary>
    /// <param name="cmd">The command buffer to record into; the copy is ordered with whatever it already holds.</param>
    /// <param name="source">The image to copy from, of the same texel format as this texture.</param>
    /// <param name="sourceLayout">The layout <paramref name="source"/> is in, which must allow transfer reads.</param>
    /// <param name="width">How many texels across to copy; clamped to this texture's own width.</param>
    /// <param name="height">How many texels down to copy; clamped to this texture's own height.</param>
    /// <remarks>
    /// The texture is moved out of the sampled layout for the copy and back into it afterwards, so a draw
    /// recorded after this in the same buffer can sample the copied frame. The source's own layout is the
    /// caller's to manage: it is a swapchain image here, and only the device knows what that is doing.
    /// </remarks>
    public void CopyFromImage(CommandBuffer cmd, Image source, ImageLayout sourceLayout, int width, int height)
    {
        int copyWidth = Math.Min(width, Width);
        int copyHeight = Math.Min(height, Height);
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            return;
        }

        TransitionLayout(
            cmd,
            Image,
            ImageLayout.ShaderReadOnlyOptimal,
            ImageLayout.TransferDstOptimal,
            PipelineStageFlags.FragmentShaderBit,
            PipelineStageFlags.TransferBit,
            AccessFlags.ShaderReadBit,
            AccessFlags.TransferWriteBit);

        ImageSubresourceLayers layers = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            MipLevel = 0,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        ImageCopy region = new()
        {
            SrcSubresource = layers,
            DstSubresource = layers,
            Extent = new Extent3D((uint)copyWidth, (uint)copyHeight, 1),
        };
        _vk.CmdCopyImage(cmd, source, sourceLayout, Image, ImageLayout.TransferDstOptimal, 1, &region);

        TransitionLayout(
            cmd,
            Image,
            ImageLayout.TransferDstOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            AccessFlags.TransferWriteBit,
            AccessFlags.ShaderReadBit);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroySampler(_device, Sampler, null);
        _vk.DestroyImageView(_device, View, null);
        _vk.DestroyImage(_device, Image, null);
        _allocator.Free(_memory);
    }

    private void Upload(
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        in TextureDescription description,
        ReadOnlySpan<Bgra32> pixels)
    {
        using VulkanBuffer staging = new(
            _vk,
            device,
            allocator,
            (ulong)(pixels.Length * Bgra32.Size),
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write(pixels);

        // The byte offset and clamped size of each level inside the tightly packed chain, worked out
        // the same way the pixels were laid out: largest level first, each level's own width times its
        // height. Captured into arrays here because the upload runs in a lambda, which cannot reach the
        // `in` description directly.
        int mipLevels = MipLevels;
        int[] levelOffsets = new int[mipLevels];
        int[] levelWidths = new int[mipLevels];
        int[] levelHeights = new int[mipLevels];
        int runningPixels = 0;
        for (int level = 0; level < mipLevels; level++)
        {
            (int levelWidth, int levelHeight) = description.GetMipSize(level);
            levelWidths[level] = Math.Max(1, levelWidth);
            levelHeights[level] = Math.Max(1, levelHeight);
            levelOffsets[level] = runningPixels * Bgra32.Size;
            runningPixels += levelWidths[level] * levelHeights[level];
        }

        Image image = Image;
        Buffer stagingHandle = staging.Handle;
        commands.SingleTime(cmd =>
        {
            TransitionLayout(
                cmd,
                image,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                AccessFlags.TransferWriteBit);

            for (int level = 0; level < mipLevels; level++)
            {
                BufferImageCopy region = new()
                {
                    BufferOffset = (ulong)levelOffsets[level],
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = (uint)level,
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                    },
                    ImageExtent = new Extent3D((uint)levelWidths[level], (uint)levelHeights[level], 1),
                };

                _vk.CmdCopyBufferToImage(cmd, stagingHandle, image, ImageLayout.TransferDstOptimal, 1, &region);
            }

            TransitionLayout(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit);
        });
    }

    private void TransitionLayout(
        CommandBuffer cmd,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = (uint)MipLevels,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
        };

        _vk.CmdPipelineBarrier(cmd, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private ImageView CreateView()
    {
        ImageViewCreateInfo createInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = ImageViewType.Type2D,
            Format = Format.B8G8R8A8Unorm,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = (uint)MipLevels,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device, &createInfo, null, &view), "vkCreateImageView");
        return view;
    }

    private Sampler CreateSampler(in TextureDescription description, float maxAnisotropy, float mipLodBias)
    {
        Filter filter = description.Filter == TextureFilter.Point ? Filter.Nearest : Filter.Linear;
        SamplerAddressMode address = description.AddressMode == TextureAddressMode.Repeat
            ? SamplerAddressMode.Repeat
            : SamplerAddressMode.ClampToEdge;

        // Anisotropy is what keeps a texture legible at a grazing angle: without it the sampler falls to
        // a coarse, isotropically-averaged mip, and a cut-out surface like a wire fence averages toward
        // transparent and fades out of the picture. Only on a filtered, mipped texture — a point-sampled
        // UI atlas gains nothing.
        bool anisotropic = maxAnisotropy > 1f && filter == Filter.Linear && MipLevels > 1;

        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            MipmapMode = MipLevels > 1 ? SamplerMipmapMode.Linear : SamplerMipmapMode.Nearest,
            AddressModeU = address,
            AddressModeV = address,
            AddressModeW = address,
            MinLod = 0,
            MaxLod = MipLevels,

            // The reference's automatic view mip bias. A temporal filter sees a different sub-pixel offset
            // every frame and averages them, so it recovers detail the level-of-detail rule assumes is
            // lost -- which means the rule picks a level coarser than the picture can actually resolve,
            // and the world comes out softer than it should. The reference biases the choice by a third of
            // a level to compensate, and without it a renderer that has just GAINED mipmaps goes straight
            // past correct into blurry.
            MipLodBias = MipLevels > 1 ? mipLodBias : 0f,
            BorderColor = BorderColor.IntOpaqueBlack,
            AnisotropyEnable = anisotropic,
            MaxAnisotropy = anisotropic ? Math.Clamp(maxAnisotropy, 1f, 16f) : 1.0f,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler");
        return sampler;
    }

    // What share of the top level fails the shaders' alpha cut. One percent is enough to mean the texture
    // is masked at all -- a wire fence is mostly hole, a railing mostly not, and neither is a solid pane.
    private const float CutoutTexelFraction = 0.01f;

    private static float MeasureCutoutFraction(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        int count = width * height;
        if (count <= 0 || count > pixels.Length)
        {
            return 0f;
        }

        int cut = 0;
        for (int i = 0; i < count; i++)
        {
            if (pixels[i].A < 128)
            {
                cut++;
            }
        }

        return cut / (float)count;
    }
}
