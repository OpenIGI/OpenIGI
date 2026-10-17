using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// A 2D array image whose layers are handed out one per texture and written whole: what the traced world's opacity masks
/// and albedo both keep their reduced textures in.
/// </summary>
/// <remarks>
/// Every layer starts at one clear colour, so a layer nothing was ever written into reads as that rather than as garbage.
/// The array rests in <see cref="ImageLayout.General"/>, where the RHI keeps every texture it binds, and each upload moves
/// one layer out and back. A destroyed texture's layer is given back and reused before a fresh one is taken; without that
/// the array filled for good, since every menu backdrop swap registers its level's textures again.
/// </remarks>
internal sealed unsafe class VulkanTextureLayers : IDisposable
{
    // The layout the array rests in between uploads: general, where the RHI keeps every texture it binds.
    private const ImageLayout Resting = ImageLayout.General;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly VulkanDevice _owner;
    private readonly DeviceMemory _memory;
    private readonly Dictionary<uint, int> _layers = [];
    private readonly Stack<int> _free = [];
    private readonly uint _resolution;
    private int _next;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanTextureLayers"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the array lives on.</param>
    /// <param name="allocator">The allocator backing the image.</param>
    /// <param name="commands">The pool the one-off upload commands come from.</param>
    /// <param name="format">The layers' format.</param>
    /// <param name="resolution">How many texels square each layer is.</param>
    /// <param name="capacity">How many layers the array has.</param>
    /// <param name="clear">What every layer holds before anything is written into it.</param>
    /// <param name="name">What the array is called in a failed call's message.</param>
    public VulkanTextureLayers(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        Format format,
        int resolution,
        int capacity,
        ClearColorValue clear,
        string name)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);

        _vk = vk;
        _owner = device;
        _device = device.Device;
        _allocator = allocator;
        _commands = commands;
        _resolution = (uint)resolution;
        Capacity = capacity;

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(_resolution, _resolution, 1),
            MipLevels = 1,
            ArrayLayers = (uint)capacity,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device, &imageInfo, null, &image), $"vkCreateImage ({name})");
        Image = image;

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device, image, _memory, 0), $"vkBindImageMemory ({name})");

        Clear(clear);
        View = CreateView(format, name);
    }

    /// <summary>Gets the array image.</summary>
    public Image Image { get; }

    /// <summary>Gets the view a ray shader reads the array through: every layer.</summary>
    public ImageView View { get; }

    /// <summary>Gets how many layers the array was allocated with.</summary>
    public int Capacity { get; }

    /// <summary>Gets how many layers are currently held.</summary>
    public int Count => _layers.Count;

    /// <summary>Finds the layer a texture was given.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <param name="layer">Receives the layer.</param>
    /// <returns><see langword="true"/> when the texture has one.</returns>
    public bool TryGetLayer(uint textureId, out int layer) => _layers.TryGetValue(textureId, out layer);

    /// <summary>Gives a texture a layer of its own: a given-back one first, then a fresh one.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <param name="layer">Receives the layer.</param>
    /// <returns><see langword="false"/> when every layer is taken.</returns>
    public bool TryAllocate(uint textureId, out int layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_free.Count > 0)
        {
            layer = _free.Pop();
        }
        else if (_next < Capacity)
        {
            layer = _next++;
        }
        else
        {
            layer = -1;
            return false;
        }

        _layers[textureId] = layer;
        return true;
    }

    /// <summary>Gives a destroyed texture's layer back to the array.</summary>
    /// <param name="textureId">The texture's identifier.</param>
    /// <remarks>
    /// The layer's contents are left as they are rather than cleared. Nothing reads a layer that no record names, and the
    /// next registration overwrites it outright.
    /// </remarks>
    public void Release(uint textureId)
    {
        if (_disposed || !_layers.Remove(textureId, out int layer))
        {
            return;
        }

        _free.Push(layer);
    }

    /// <summary>Writes one layer whole.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="texels">The layer's bytes, row-major.</param>
    public void Upload(int layer, byte[] texels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(texels);
        VulkanBuffer staging = new(
            _vk,
            _owner,
            _allocator,
            (ulong)texels.Length,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write<byte>(texels);

        // Batched rather than submitted and waited on, one per texture, which made a level's worth of them a round trip
        // each in the frame that created them. The batch runs before anything submitted after it and frees the staging
        // when it has; the barriers below are every stage rather than compute alone, because without the wait they are
        // all that orders this against its readers.
        Image image = Image;
        Buffer stagingHandle = staging.Handle;
        uint resolution = _resolution;
        _commands.SingleTimeBatched(
            cmd =>
            {
                Transition(
                    cmd,
                    image,
                    Resting,
                    ImageLayout.TransferDstOptimal,
                    PipelineStageFlags.AllCommandsBit,
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
                    ImageExtent = new Extent3D(resolution, resolution, 1),
                };

                _vk.CmdCopyBufferToImage(cmd, stagingHandle, image, ImageLayout.TransferDstOptimal, 1, &region);

                Transition(
                    cmd,
                    image,
                    ImageLayout.TransferDstOptimal,
                    Resting,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.AllCommandsBit,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.ShaderReadBit,
                    (uint)layer,
                    1);
            },
            staging,
            (ulong)texels.Length);
    }

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

    // Fills every layer with the clear colour, before anything is written, and leaves the array resting.
    private void Clear(ClearColorValue clear)
    {
        Image image = Image;
        uint layers = (uint)Capacity;
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
                layers);

            ImageSubresourceRange range = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = layers,
            };

            ClearColorValue colour = clear;
            _vk.CmdClearColorImage(cmd, image, ImageLayout.TransferDstOptimal, &colour, 1, &range);

            Transition(
                cmd,
                image,
                ImageLayout.TransferDstOptimal,
                Resting,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit,
                0,
                layers);
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

    private ImageView CreateView(Format format, string name)
    {
        ImageViewCreateInfo createInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = ImageViewType.Type2DArray,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,

                // Every layer the image has. The traced albedo's view once covered the first thousand and twenty-four while
                // its array could hold four times that, so a texture given a layer past it was sampled outside the view.
                LayerCount = (uint)Capacity,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device, &createInfo, null, &view), $"vkCreateImageView ({name})");
        return view;
    }
}
