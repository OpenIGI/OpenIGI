using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Memory;

/// <summary>An image the backend owns outright: the image, its memory and a view over all of it.</summary>
/// <remarks>
/// <para>
/// The three steps a Vulkan image always needs — create, allocate, bind — plus the view almost every use
/// of it wants, kept together so a caller names the format, the size and what it is for. Textures the
/// engine uploads go through <see cref="Textures.VulkanTexture"/> instead; this is for the images the
/// renderer makes for itself, which is most of them: render targets, lookup tables, the intermediate
/// results passes hand to one another.
/// </para>
/// <para>
/// It transitions itself out of the undefined layout at creation, because an image that has never been
/// transitioned cannot legally be read or written, and every one of these is written by a pass that would
/// otherwise have to open with a barrier it did not ask for.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanImage : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly DeviceMemory _memory;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanImage"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the image lives on.</param>
    /// <param name="allocator">The allocator backing the image.</param>
    /// <param name="commands">The pool the initial layout transition comes from.</param>
    /// <param name="format">The image format.</param>
    /// <param name="width">The width in texels.</param>
    /// <param name="height">The height in texels.</param>
    /// <param name="depth">The depth in texels; one for a two-dimensional image.</param>
    /// <param name="usage">What the image is for.</param>
    /// <param name="initialLayout">The layout to leave the image in, ready for its first use.</param>
    /// <exception cref="GraphicsDeviceException">The image could not be created or backed.</exception>
    public VulkanImage(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        Format format,
        uint width,
        uint height,
        uint depth,
        ImageUsageFlags usage,
        ImageLayout initialLayout = ImageLayout.General)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);

        _vk = vk;
        _device = device.Device;
        _allocator = allocator;
        Format = format;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Depth = Math.Max(1, depth);
        Layout = initialLayout;

        bool volume = Depth > 1;
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = volume ? ImageType.Type3D : ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(Width, Height, Depth),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        Image image;
        VkCheck.Ok(_vk.CreateImage(_device, &imageInfo, null, &image), "vkCreateImage");
        Handle = image;

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, MemoryPropertyFlags.DeviceLocalBit);
        VkCheck.Ok(_vk.BindImageMemory(_device, image, _memory, 0), "vkBindImageMemory");

        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = volume ? ImageViewType.Type3D : ImageViewType.Type2D,
            Format = format,
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

        Image local = image;
        commands.SingleTime(cmd => Transition(cmd, local, ImageLayout.Undefined, initialLayout));
    }

    /// <summary>Gets the image handle.</summary>
    public Image Handle { get; }

    /// <summary>Gets a view over the whole image.</summary>
    public ImageView View { get; }

    /// <summary>Gets the image format.</summary>
    public Format Format { get; }

    /// <summary>Gets the width in texels.</summary>
    public uint Width { get; }

    /// <summary>Gets the height in texels.</summary>
    public uint Height { get; }

    /// <summary>Gets the depth in texels.</summary>
    public uint Depth { get; }

    /// <summary>Gets the layout the image was last transitioned to by this type.</summary>
    public ImageLayout Layout { get; private set; }

    /// <summary>Records a transition of the whole image from one layout to another.</summary>
    /// <param name="cmd">The command buffer to record into.</param>
    /// <param name="from">The layout the image is in.</param>
    /// <param name="to">The layout to move it to.</param>
    public void Transition(CommandBuffer cmd, ImageLayout from, ImageLayout to)
    {
        Transition(cmd, Handle, from, to);
        Layout = to;
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
        _vk.DestroyImage(_device, Handle, null);
        _allocator.Free(_memory);
    }

    private void Transition(CommandBuffer cmd, Image image, ImageLayout from, ImageLayout to)
    {
        // Deliberately coarse: these transitions happen at pass boundaries a handful of times a frame, not
        // per resource per draw, so naming every stage costs nothing measurable and removes a class of bug
        // where a barrier is correct for the use it was written for and wrong for the next one added.
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
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
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };

        _vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }
}
