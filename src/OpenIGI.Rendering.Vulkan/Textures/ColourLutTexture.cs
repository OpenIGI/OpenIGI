using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Textures;

/// <summary>
/// An authored colour grade on the device: the cube a <see cref="ColourLookupTable"/> describes, as a
/// 3D texture the resolve reads one fetch from.
/// </summary>
/// <remarks>
/// <para>
/// A grade is a function from colour to colour, and a cube is that function written down. What makes it
/// worth a texture rather than arithmetic is that the hardware already interpolates in three dimensions:
/// the whole of a grade, however it was authored and whatever it does, costs the same single trilinear
/// fetch as the simplest one.
/// </para>
/// <para>
/// Always resident once a table has been set, and rebuilt only when a different table arrives. A cube is
/// at most sixty-four on a side — two megabytes at half float — and the alternative, uploading it per
/// frame, would spend more bandwidth restating a constant than the grade itself costs to apply.
/// </para>
/// <para>
/// Stored as half floats with a padding channel rather than as three: three-channel sampled formats are
/// not something every implementation carries, and a grade authored in a wide gamut can hold values
/// outside zero to one that an eight-bit cube would clip on the way in.
/// </para>
/// </remarks>
internal sealed unsafe class ColourLutTexture : IDisposable
{
    private const Format CubeFormat = Format.R16G16B16A16Sfloat;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly Sampler _sampler;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ColourLutTexture"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the cube lives on.</param>
    /// <param name="allocator">The allocator backing it.</param>
    /// <param name="commands">The pool its upload and layout transitions come from.</param>
    /// <exception cref="GraphicsDeviceException">The sampler could not be created.</exception>
    public ColourLutTexture(Vk vk, VulkanDevice device, VulkanAllocator allocator, VulkanCommands commands)
    {
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _commands = commands;
        _sampler = CreateSampler();
    }

    /// <summary>Gets the cube's view, or a zero handle before a table has been set.</summary>
    public ImageView View => _view;

    /// <summary>Gets the sampler the cube is read through.</summary>
    public Sampler Sampler => _sampler;

    /// <summary>Gets the cube's edge length in samples, or zero when none is loaded.</summary>
    public int Size { get; private set; }

    /// <summary>Gets a value indicating whether a grade is loaded and ready to sample.</summary>
    public bool Ready => _view.Handle != 0 && Size > 0;

    /// <summary>Uploads a table, replacing whatever was loaded.</summary>
    /// <param name="table">The grade, or <see langword="null"/> to drop the one that is loaded.</param>
    /// <param name="retire">
    /// Where the cube being replaced is handed for release once the frames still naming it have
    /// finished. Passing nothing frees it AT ONCE, which is only safe with no frame in flight.
    /// </param>
    /// <remarks>
    /// A blocking upload on its own submit. It happens when a grade is chosen — a level load, or the
    /// console — so the stall costs nothing anybody sees and buys a texture that is simply resident from
    /// then on. The cube it replaces is not destroyed on the spot: the resolve sets of the frames still
    /// in flight name that image, and freeing one the GPU is reading loses the device rather than
    /// leaking it.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">The cube could not be created.</exception>
    public void Set(ColourLookupTable? table, Action<IDisposable>? retire = null)
    {
        if (table is null)
        {
            Release(retire);
            return;
        }

        if (Size != table.Size)
        {
            Release(retire);
            Create(table.Size);
            Size = table.Size;
        }

        Upload(table);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release(retire: null);
        _vk.DestroySampler(_device.Device, _sampler, null);
    }

    /// <summary>A cube that has been replaced, holding its handles until it is safe to free them.</summary>
    /// <remarks>
    /// The three handles travel together because they are one resource in three parts, and the order
    /// they are given up in is the order they depend on each other: the view names the image, and the
    /// image is bound to the memory.
    /// </remarks>
    private sealed class RetiredCube(
        Vk vk, Device device, VulkanAllocator allocator, Image image, ImageView view, DeviceMemory memory)
        : IDisposable
    {
        public void Dispose()
        {
            if (view.Handle != 0)
            {
                vk.DestroyImageView(device, view, null);
            }

            if (image.Handle != 0)
            {
                vk.DestroyImage(device, image, null);
            }

            if (memory.Handle != 0)
            {
                allocator.Free(memory);
            }
        }
    }

    /// <summary>Creates the sampler the lookup depends on being exactly right.</summary>
    /// <remarks>
    /// Linear, because the interpolation between samples IS the grade everywhere between them — a
    /// nearest fetch would quantise the whole picture to the cube's own resolution and posterise every
    /// gradient. Clamped, because a colour at the edge of the cube is the extreme of what the grade was
    /// authored over and wrapping there would fetch the opposite extreme: a highlight would come back
    /// as a shadow.
    /// </remarks>
    private Sampler CreateSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            BorderColor = BorderColor.FloatOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device.Device, &createInfo, null, &sampler), "vkCreateSampler (lut)");
        return sampler;
    }

    private void Create(int size)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type3D,
            Format = CubeFormat,
            Extent = new Extent3D((uint)size, (uint)size, (uint)size),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device.Device, &imageInfo, null, &image), "vkCreateImage (lut)");
        _image = image;

        _vk.GetImageMemoryRequirements(_device.Device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device.Device, image, _memory, 0), "vkBindImageMemory (lut)");

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type3D,
            Format = CubeFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device.Device, &viewInfo, null, &view), "vkCreateImageView (lut)");
        _view = view;
    }

    private void Upload(ColourLookupTable table)
    {
        int size = table.Size;
        int voxels = size * size * size;
        ulong bytes = (ulong)voxels * 4 * sizeof(ushort);

        using VulkanBuffer staging = new(
            _vk,
            _device,
            _allocator,
            bytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            persistentlyMapped: true);

        // The file's triples widened into the padded voxel the format carries. Red varies fastest in
        // both, which is also the order a 3D texture's own memory runs, so this is a straight walk.
        ReadOnlySpan<float> entries = table.Entries;
        Half* mapped = (Half*)staging.MappedPointer;
        for (int i = 0; i < voxels; i++)
        {
            mapped[(i * 4) + 0] = (Half)entries[(i * 3) + 0];
            mapped[(i * 4) + 1] = (Half)entries[(i * 3) + 1];
            mapped[(i * 4) + 2] = (Half)entries[(i * 3) + 2];
            mapped[(i * 4) + 3] = (Half)1f;
        }

        Image image = _image;
        _commands.SingleTime(cmd =>
        {
            Transition(
                cmd, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
                PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, AccessFlags.TransferWriteBit);

            BufferImageCopy region = new()
            {
                BufferOffset = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D((uint)size, (uint)size, (uint)size),
            };
            _vk.CmdCopyBufferToImage(cmd, staging.Handle, image, ImageLayout.TransferDstOptimal, 1, &region);

            Transition(
                cmd, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit,
                AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
        });
    }

    private void Transition(
        CommandBuffer cmd,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags source,
        PipelineStageFlags destination,
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
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
        };

        _vk.CmdPipelineBarrier(cmd, source, destination, 0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>Gives up the loaded cube, either to the retirement queue or on the spot.</summary>
    /// <param name="retire">
    /// Where to hand it so it is freed once the frames that named it have cycled, or
    /// <see langword="null"/> to free it immediately — which shutdown may do, having gone idle first.
    /// </param>
    private void Release(Action<IDisposable>? retire)
    {
        RetiredCube cube = new(_vk, _device.Device, _allocator, _image, _view, _memory);
        _view = default;
        _image = default;
        _memory = default;
        Size = 0;

        if (retire is null)
        {
            cube.Dispose();
        }
        else
        {
            retire(cube);
        }
    }
}
