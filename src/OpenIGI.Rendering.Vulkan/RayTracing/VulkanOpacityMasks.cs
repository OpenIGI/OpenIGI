using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The cut-out shapes a traced ray needs, in one array a ray shader can reach without knowing which
/// texture it hit.
/// </summary>
/// <remarks>
/// <para>
/// A ray query has no per-texel test of its own. Traversal either accepts a triangle or it does not, so
/// a wire fence built as one quad with an alpha channel stops a ray exactly as a board would — which is
/// why cut-out surfaces were kept out of the traceable world entirely and cast no shadow at all. Giving
/// them back their shadow means the shader has to sample the alpha at the point the ray met the
/// triangle, and that means reaching a texture chosen by the hit rather than bound before the dispatch.
/// </para>
/// <para>
/// <b>Why an array of masks rather than the textures themselves.</b> The bindless heaps in
/// <c>Bindless</c> are the general answer to that and are the right home for it eventually. This is the
/// narrow one: a shadow ray does not need colour, or mips, or the source resolution — it needs one bit,
/// is this texel there. So every cut-out texture is reduced once, at load, to a single-channel layer of
/// a fixed-size array, and a hit carries the layer number. One extra sampled image on the pass, no
/// change to how any pass binds its resources, and the whole thing is a few megabytes.
/// </para>
/// <para>
/// The cost of that choice is resolution: a mask is <see cref="Resolution"/> square whatever the source
/// was, so a texture larger than that loses detail its rasterised silhouette keeps. For chain-link at
/// the distances a shadow is read from, that is not visible; for a texture whose cut-out is finer than
/// its own quarter-texel it would be. Point sampling is deliberate — the answer is binary, and filtering
/// it would turn a wire's edge into a half-open texel that is neither there nor not.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanOpacityMasks : IDisposable
{
    /// <summary>How many texels square each mask is stored at.</summary>
    public const int Resolution = 256;

    /// <summary>How many distinct cut-out textures the array can hold.</summary>
    /// <remarks>
    /// Mission one uses far fewer than this. A texture that arrives past the limit keeps the old
    /// behaviour — it stays out of the traceable world rather than being traced with the wrong mask —
    /// and the overflow is logged once, because silently casting somebody else's holes would be worse
    /// than casting none.
    /// </remarks>
    public const int MaxLayers = 128;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanDevice _owner;
    private readonly DeviceMemory _memory;

    // The layout the array rests in between uploads: shader-read for the legacy device, general for the
    // RHI, which keeps every texture in general and transitions on binding.
    private readonly ImageLayout _resting;
    private readonly Dictionary<uint, int> _layers = [];
    private int _next;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanOpacityMasks"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the array lives on.</param>
    /// <param name="allocator">The allocator backing the image.</param>
    /// <param name="commands">The pool the one-off upload commands come from.</param>
    /// <param name="resting">The layout the array rests in between uploads.</param>
    public VulkanOpacityMasks(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        ImageLayout resting = ImageLayout.ShaderReadOnlyOptimal)
    {
        _resting = resting;
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);

        _vk = vk;
        _owner = device;
        _device = device.Device;
        _allocator = allocator;
        _commands = commands;

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8Unorm,
            Extent = new Extent3D(Resolution, Resolution, 1),
            MipLevels = 1,
            ArrayLayers = MaxLayers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device, &imageInfo, null, &image), "vkCreateImage (opacity masks)");
        Image = image;

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device, image, _memory, 0), "vkBindImageMemory (opacity masks)");

        // Every layer starts fully opaque, so a record that names a layer nothing was ever written into
        // behaves like solid geometry rather than like a hole. The array is left in the layout the
        // sampler reads from, and each upload moves one layer out and back.
        ClearToOpaque();

        View = CreateView();
    }

    /// <summary>Gets the array image.</summary>
    public Image Image { get; }

    /// <summary>Gets the view a ray shader reads the array through.</summary>
    /// <remarks>
    /// Read with <c>texelFetch</c> rather than a filtered sample, so whichever sampler the pass that
    /// binds it happens to own is irrelevant: the value is a decision, and interpolating it would turn a
    /// wire's edge into a half-open texel that is neither there nor not.
    /// </remarks>
    public ImageView View { get; }

    /// <summary>Gets how many masks have been registered.</summary>
    public int Count => _next;

    /// <summary>Reduces a texture's alpha to a mask layer, or returns the layer it already has.</summary>
    /// <param name="textureId">The texture's identifier, which is what the mapping is keyed by.</param>
    /// <param name="pixels">The texture's whole mip chain; only the largest level is read.</param>
    /// <param name="width">The width of the largest level.</param>
    /// <param name="height">The height of the largest level.</param>
    /// <returns>The layer, or <c>-1</c> when the array is full.</returns>
    public int Register(uint textureId, ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_layers.TryGetValue(textureId, out int existing))
        {
            return existing;
        }

        if (_next >= MaxLayers || width <= 0 || height <= 0 || width * height > pixels.Length)
        {
            return -1;
        }

        int layer = _next++;
        _layers[textureId] = layer;
        Upload(layer, Reduce(pixels, width, height));
        return layer;
    }

    /// <summary>Finds the layer a texture was reduced to.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <param name="layer">Receives the layer.</param>
    /// <returns><see langword="true"/> when the texture has a mask.</returns>
    public bool TryGetLayer(uint textureId, out int layer) => _layers.TryGetValue(textureId, out layer);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyImageView(_device, View, null);
        _vk.DestroyImage(_device, Image, null);
        _allocator.Free(_memory);
    }

    /// <summary>Samples a texture's alpha down to one mask-sized plane of zeroes and ones.</summary>
    /// <param name="pixels">The source pixels, largest level first.</param>
    /// <param name="width">The source width.</param>
    /// <param name="height">The source height.</param>
    /// <returns>The mask, row-major, 255 where the surface is there and 0 where it is cut away.</returns>
    /// <remarks>
    /// Nearest, and deliberately so. The rasteriser's test is <c>alpha &lt; 0.5</c> against a texture
    /// whose cut-out alpha is strictly zero or one, so the answer at any texel is already binary;
    /// averaging neighbours would invent values on the wrong side of that test along every wire, which
    /// reads as a fence that has thickened or thinned rather than as one that has been resampled. Where
    /// the source is larger than the mask this drops texels, which is the resolution cost named on the
    /// class — not a filter that could have avoided it.
    /// </remarks>
    private static byte[] Reduce(ReadOnlySpan<Bgra32> pixels, int width, int height)
    {
        byte[] mask = new byte[Resolution * Resolution];

        for (int y = 0; y < Resolution; y++)
        {
            // The centre of the mask texel, in source texels, so the sampling matches what the shader
            // asks for rather than being offset half a texel from it.
            int sourceY = Math.Min(height - 1, (int)(((y + 0.5f) / Resolution) * height));
            int sourceRow = sourceY * width;
            int maskRow = y * Resolution;

            for (int x = 0; x < Resolution; x++)
            {
                int sourceX = Math.Min(width - 1, (int)(((x + 0.5f) / Resolution) * width));
                mask[maskRow + x] = pixels[sourceRow + sourceX].A < 128 ? (byte)0 : (byte)255;
            }
        }

        return mask;
    }

    /// <summary>Fills every layer with "solid", before any mask has been written.</summary>
    private void ClearToOpaque()
    {
        Image image = Image;
        _commands.SingleTime(cmd =>
        {
            Transition(
                cmd,
                image,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                AccessFlags.TransferWriteBit,
                0,
                MaxLayers);

            ClearColorValue clear = default;
            clear.Float32_0 = 1f;

            ImageSubresourceRange range = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = MaxLayers,
            };

            _vk.CmdClearColorImage(cmd, image, ImageLayout.TransferDstOptimal, &clear, 1, &range);

            Transition(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                _resting,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit,
                0,
                MaxLayers);
        });
    }

    /// <summary>Writes one reduced mask into its layer.</summary>
    /// <param name="layer">The layer to write.</param>
    /// <param name="mask">The mask, <see cref="Resolution"/> squared bytes.</param>
    private void Upload(int layer, byte[] mask)
    {
        using VulkanBuffer staging = new(
            _vk,
            _owner,
            _allocator,
            (ulong)mask.Length,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write<byte>(mask);

        Image image = Image;
        Buffer stagingHandle = staging.Handle;
        _commands.SingleTime(cmd =>
        {
            Transition(
                cmd,
                image,
                _resting,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.ComputeShaderBit,
                PipelineStageFlags.TransferBit,
                AccessFlags.ShaderReadBit,
                AccessFlags.TransferWriteBit,
                (uint)layer,
                1);

            BufferImageCopy region = new()
            {
                BufferOffset = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = (uint)layer,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D(Resolution, Resolution, 1),
            };

            _vk.CmdCopyBufferToImage(cmd, stagingHandle, image, ImageLayout.TransferDstOptimal, 1, &region);

            Transition(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                _resting,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit,
                (uint)layer,
                1);
        });
    }

    private void Transition(
        CommandBuffer cmd,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess,
        uint baseLayer,
        uint layerCount)
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
                LevelCount = 1,
                BaseArrayLayer = baseLayer,
                LayerCount = layerCount,
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
            ViewType = ImageViewType.Type2DArray,
            Format = Format.R8Unorm,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = MaxLayers,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device, &createInfo, null, &view), "vkCreateImageView (opacity masks)");
        return view;
    }
}
