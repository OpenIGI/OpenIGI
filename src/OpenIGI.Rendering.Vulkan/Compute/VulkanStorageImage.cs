using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Compute;

/// <summary>
/// A shader-writable 2D or 3D image — a storage texture or a volume — that a compute pass loads and
/// stores through, and that a later pass can sample.
/// </summary>
/// <remarks>
/// <para>
/// Unlike a sampled <see cref="Textures.VulkanTexture"/>, this is created with the storage usage bit and
/// kept in the <see cref="ImageLayout.General"/> layout for its whole life. General is the one layout an
/// image can be both stored to through an image binding and sampled from, so holding it there means a
/// compute pass and a draw can share the resource without a transition between every use — the trade a
/// transient render target would not make, but exactly right for a field written once and read for the
/// rest of the frame.
/// </para>
/// <para>
/// The format is the volume's own and is fixed at creation. An image binding reinterprets the storage
/// through whatever format the shader declares, so the shader's <c>layout(r8)</c> must match what was
/// allocated here; a mismatch writes a fraction of the texels and leaves the rest, silently, which is
/// why the format is chosen from the same short list the seam exposes rather than inferred.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanStorageImage : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly DeviceMemory _memory;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanStorageImage"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the image lives on.</param>
    /// <param name="allocator">The allocator backing the image.</param>
    /// <param name="commands">The pool the initial layout transition comes from.</param>
    /// <param name="width">The width in texels.</param>
    /// <param name="height">The height in texels.</param>
    /// <param name="depth">The depth in texels; one for a 2D storage texture.</param>
    /// <param name="format">The voxel format.</param>
    /// <exception cref="GraphicsDeviceException">The image could not be created.</exception>
    public VulkanStorageImage(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        int width,
        int height,
        int depth,
        VolumeFormat format)
    {
        _vk = vk;
        _device = device.Device;
        _allocator = allocator;
        Width = width;
        Height = height;
        Depth = depth;
        Format = format;
        Is3D = depth > 1;
        VkFormat = ToVkFormat(format);

        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = Is3D ? ImageType.Type3D : ImageType.Type2D,
            Format = VkFormat,
            Extent = new Extent3D((uint)width, (uint)height, (uint)depth),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit
                | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device, &imageInfo, null, &image), "vkCreateImage");
        Image = image;

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device, image, _memory, 0), "vkBindImageMemory");

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = Is3D ? ImageViewType.Type3D : ImageViewType.Type2D,
            Format = VkFormat,
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
        VkCheck.Ok(_vk.CreateImageView(_device, &viewInfo, null, &view), "vkCreateImageView");
        View = view;

        Sampler = CreateSampler();

        // Moved once to General and kept there. Undefined is not a layout a shader may store through, so
        // the very first dispatch would be undefined without this.
        commands.SingleTime(cmd => Transition(cmd, ImageLayout.Undefined, ImageLayout.General,
            PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ComputeShaderBit, 0, AccessFlags.ShaderWriteBit));
    }

    /// <summary>Gets the image handle.</summary>
    public Image Image { get; }

    /// <summary>Gets the image view, used both as a storage image and for sampling.</summary>
    public ImageView View { get; }

    /// <summary>Gets a sampler, for when the image is read through a combined sampler.</summary>
    public Sampler Sampler { get; }

    /// <summary>Gets the width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the depth in texels.</summary>
    public int Depth { get; }

    /// <summary>Gets the voxel format.</summary>
    public VolumeFormat Format { get; }

    /// <summary>Gets the Vulkan format the storage was allocated with.</summary>
    public Format VkFormat { get; }

    /// <summary>Gets a value indicating whether the image is a volume.</summary>
    public bool Is3D { get; }

    /// <summary>Gets the bytes one voxel occupies.</summary>
    public int BytesPerVoxel => Format switch
    {
        VolumeFormat.R8 => 1,
        VolumeFormat.Rgba16F => 8,
        _ => 4,
    };

    /// <summary>Records a barrier that makes prior stores to this image visible to a following read.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    public void BarrierStoreToRead(CommandBuffer cmd) => Transition(
        cmd,
        ImageLayout.General,
        ImageLayout.General,
        PipelineStageFlags.ComputeShaderBit,
        PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit,
        AccessFlags.ShaderWriteBit,
        AccessFlags.ShaderReadBit);

    private void Transition(
        CommandBuffer cmd,
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
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
        };

        _vk.CmdPipelineBarrier(cmd, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

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
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.IntOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler");
        return sampler;
    }

    private static Format ToVkFormat(VolumeFormat format) => format switch
    {
        VolumeFormat.R8 => Silk.NET.Vulkan.Format.R8Unorm,
        VolumeFormat.Rgba16F => Silk.NET.Vulkan.Format.R16G16B16A16Sfloat,
        _ => Silk.NET.Vulkan.Format.R8G8B8A8Unorm,
    };

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
}
