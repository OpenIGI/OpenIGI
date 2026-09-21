using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>A sampled cube map: six square faces in one image, a cube view, and a sampler.</summary>
/// <remarks>
/// <para>
/// A cube in Vulkan is an ordinary two-dimensional image with <b>six array layers</b> and the
/// cube-compatible flag, read through a view of type <see cref="ImageViewType.TypeCube"/>. So most of this
/// is <see cref="VulkanTexture"/>'s shape with a layer index threaded through; what differs is that the
/// faces arrive <em>one at a time</em> — the game stores each as its own file in an archive — so the
/// image is created empty and each face is uploaded on its own.
/// </para>
/// <para>
/// Created empty means created <em>bindable</em>: every layer is moved to the shader-read layout before
/// anything is written, so a cube bound with two of its six faces uploaded samples black on the rest
/// rather than being undefined. An archive that is missing a face is a real case — it is how a level
/// with a partial bake loads — and it should cost a dull reflection, not a validation error.
/// </para>
/// <para>
/// No mip chain and clamped at every edge, which is what the original's own bakes are: I.G.I. 2 creates
/// one single-mip <c>A8R8G8B8</c> cube at face zero's width and loads the six files into it in order
/// (<c>Envmap_LoadFromMemory</c>, <c>igi2ed 0x581670</c>). Clamping is what makes the seam between two
/// faces blend instead of wrapping to the opposite side of the sky.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanCubeTexture : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanDevice _owner;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly DeviceMemory _memory;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanCubeTexture"/> class, with black faces.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the cube lives on.</param>
    /// <param name="allocator">The allocator backing the image and its staging buffers.</param>
    /// <param name="commands">The pool each upload's one-off command comes from.</param>
    /// <param name="edge">The edge length of one face, in texels.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edge"/> is not positive.</exception>
    public VulkanCubeTexture(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int edge)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(edge);

        _vk = vk;
        _device = device.Device;
        _owner = device;
        _allocator = allocator;
        _commands = commands;
        Edge = edge;

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,

            // Type2D with six layers, not Type3D: a cube is an array of faces, and only the view and
            // this flag make it one.
            Flags = ImageCreateFlags.CreateCubeCompatibleBit,
            ImageType = ImageType.Type2D,
            Format = Format.B8G8R8A8Unorm,
            Extent = new Extent3D((uint)edge, (uint)edge, 1),
            MipLevels = 1,
            ArrayLayers = IGraphicsDevice.CubeFaceCount,
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

        ClearToShaderRead();
        View = CreateView();
        Sampler = CreateSampler();
    }

    /// <summary>Gets the image handle.</summary>
    public Image Image { get; }

    /// <summary>Gets the cube view the sampler reads through.</summary>
    public ImageView View { get; }

    /// <summary>Gets the sampler bound alongside the view.</summary>
    public Sampler Sampler { get; }

    /// <summary>Gets the edge length of one face, in texels.</summary>
    public int Edge { get; }

    /// <summary>Gets how many texels one face holds.</summary>
    public int FaceTexels => Edge * Edge;

    /// <summary>Replaces one face.</summary>
    /// <param name="face">Which face, <c>0</c> to <c>5</c>.</param>
    /// <param name="pixels">The face's texels, row by row.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not a face.</exception>
    /// <exception cref="GraphicsDeviceException">The span is not one face's worth of texels.</exception>
    public void UpdateFace(int face, ReadOnlySpan<Bgra32> pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, IGraphicsDevice.CubeFaceCount);

        if (pixels.Length != FaceTexels)
        {
            throw new GraphicsDeviceException(
                $"A face of a {Edge}-texel cube holds {FaceTexels} texels, not {pixels.Length}.");
        }

        ulong stagingBytes = (ulong)(pixels.Length * Bgra32.Size);
        VulkanBuffer staging = new(
            _vk,
            _owner,
            _allocator,
            stagingBytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write(pixels);

        Image image = Image;
        Buffer stagingHandle = staging.Handle;
        uint layer = (uint)face;
        uint edge = (uint)Edge;

        _commands.SingleTimeBatched(
            cmd =>
            {
                // From the shader-read layout the constructor left every layer in, not from undefined:
                // the other five faces are already written and a whole-image discard would lose them.
                TransitionLayer(
                    cmd,
                    layer,
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
                        MipLevel = 0,
                        BaseArrayLayer = layer,
                        LayerCount = 1,
                    },
                    ImageExtent = new Extent3D(edge, edge, 1),
                };

                _vk.CmdCopyBufferToImage(cmd, stagingHandle, image, ImageLayout.TransferDstOptimal, 1, &region);

                TransitionLayer(
                    cmd,
                    layer,
                    ImageLayout.TransferDstOptimal,
                    ImageLayout.ShaderReadOnlyOptimal,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.FragmentShaderBit,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit);
            },
            keepAlive: staging,
            stagingBytes: stagingBytes);
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

    /// <inheritdoc/>
    public override string ToString() => $"cube map, {Edge}x{Edge} per face";

    /// <summary>Clears every face to black and leaves them where a sampler can read them.</summary>
    /// <remarks>
    /// The clear is what makes a partly-written cube well defined. Without it, binding a cube before
    /// all six faces had arrived would sample memory that was never initialised — which on a driver
    /// that happens not to zero it reads as whatever the allocation last held.
    /// </remarks>
    private void ClearToShaderRead()
    {
        Image image = Image;
        _commands.SingleTime(cmd =>
        {
            TransitionAllLayers(
                cmd,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                AccessFlags.TransferWriteBit);

            ClearColorValue black = default;
            ImageSubresourceRange range = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = IGraphicsDevice.CubeFaceCount,
            };
            _vk.CmdClearColorImage(cmd, image, ImageLayout.TransferDstOptimal, &black, 1, &range);

            TransitionAllLayers(
                cmd,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit);
        });
    }

    private void TransitionAllLayers(
        CommandBuffer cmd,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess) =>
        Transition(
            cmd,
            0,
            IGraphicsDevice.CubeFaceCount,
            oldLayout,
            newLayout,
            sourceStage,
            destinationStage,
            sourceAccess,
            destinationAccess);

    private void TransitionLayer(
        CommandBuffer cmd,
        uint layer,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags sourceStage,
        PipelineStageFlags destinationStage,
        AccessFlags sourceAccess,
        AccessFlags destinationAccess) =>
        Transition(
            cmd,
            layer,
            1,
            oldLayout,
            newLayout,
            sourceStage,
            destinationStage,
            sourceAccess,
            destinationAccess);

    private void Transition(
        CommandBuffer cmd,
        uint baseLayer,
        uint layerCount,
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
            Image = Image,
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
            ViewType = ImageViewType.TypeCube,
            Format = Format.B8G8R8A8Unorm,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = IGraphicsDevice.CubeFaceCount,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device, &createInfo, null, &view), "vkCreateImageView");
        return view;
    }

    private Sampler CreateSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,

            // Clamped on all three, which for a cube is what keeps the blend across a face edge from
            // reaching round to the opposite side of the sky.
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0,
            MaxLod = 1,
            MipLodBias = 0f,
            BorderColor = BorderColor.IntOpaqueBlack,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler");
        return sampler;
    }
}
