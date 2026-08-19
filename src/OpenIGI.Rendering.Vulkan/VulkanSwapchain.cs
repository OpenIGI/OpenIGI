using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The swapchain: the small ring of images the compositor shows, and everything decided to create it —
/// the surface format, the present mode, and the extent.
/// </summary>
/// <remarks>
/// <para>
/// A swapchain is the one part of the backend the window's size reaches into, so it is also the one
/// part rebuilt while the program runs: a resize, a move to another monitor, or a minimise leaves the
/// old chain out of date, and the fix is to wait for the device to go idle and make a new chain against
/// the same surface. Everything that depends on the extent — the image views here, and the framebuffers
/// and per-image targets above — is rebuilt with it.
/// </para>
/// <para>
/// The format is chosen as an unsigned-normalised B8G8R8A8, matching the engine's own
/// <c>Bgra32</c> pixel and the OpenGL backend's default framebuffer: the engine tone maps and encodes
/// its own output, so a swapchain that re-encoded to sRGB a second time would double the gamma. The
/// present mode follows the v-sync request — FIFO when it is on, since every driver has it, and mailbox
/// or immediate when it is off so a frame need not wait for the blank.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanSwapchain : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly KhrSurface _khrSurface;
    private readonly KhrSwapchain _khrSwapchain;
    private readonly SurfaceKHR _surface;

    private SwapchainKHR _handle;
    private Image[] _images = [];
    private ImageView[] _imageViews = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanSwapchain"/> class.</summary>
    /// <param name="instance">The instance owning the surface.</param>
    /// <param name="device">The device the swapchain is created on.</param>
    /// <param name="width">The desired width in pixels.</param>
    /// <param name="height">The desired height in pixels.</param>
    /// <param name="vsync">Whether presentation should wait for the vertical blank.</param>
    /// <exception cref="GraphicsDeviceException">The swapchain could not be created.</exception>
    public VulkanSwapchain(VulkanInstance instance, VulkanDevice device, int width, int height, bool vsync)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        _vk = instance.Vk;
        _device = device;
        _khrSurface = instance.KhrSurface;
        _khrSwapchain = device.Swapchain;
        _surface = instance.Surface;

        Create(width, height, vsync);
    }

    /// <summary>Gets the swapchain handle.</summary>
    public SwapchainKHR Handle => _handle;

    /// <summary>Gets the format the swapchain images are in.</summary>
    public Format Format { get; private set; }

    /// <summary>Gets the pixel dimensions of the swapchain images.</summary>
    public Extent2D Extent { get; private set; }

    /// <summary>Gets the swapchain's images.</summary>
    public ReadOnlySpan<Image> Images => _images;

    /// <summary>Gets one view per swapchain image, in image order.</summary>
    public ReadOnlySpan<ImageView> ImageViews => _imageViews;

    /// <summary>Gets how many images the swapchain holds.</summary>
    public int ImageCount => _images.Length;

    /// <summary>Gets one swapchain image by index, for a read back to the CPU.</summary>
    /// <param name="index">The image index.</param>
    /// <returns>The image handle.</returns>
    public Image ImageAt(uint index) => _images[index];

    /// <summary>Acquires the next image to render into.</summary>
    /// <param name="signal">A semaphore signalled when the image is ready to be rendered to.</param>
    /// <param name="imageIndex">Receives the index of the acquired image.</param>
    /// <returns>
    /// The acquire result. <see cref="Result.Success"/> and <see cref="Result.SuboptimalKhr"/> both
    /// yield a usable image; <see cref="Result.ErrorOutOfDateKhr"/> means the caller must rebuild the
    /// swapchain before rendering.
    /// </returns>
    public Result AcquireNextImage(Semaphore signal, out uint imageIndex)
    {
        uint index = 0;
        Result result = _khrSwapchain.AcquireNextImage(
            _device.Device,
            _handle,
            ulong.MaxValue,
            signal,
            default,
            &index);
        imageIndex = index;
        return VkCheck.Allow(
            result,
            "vkAcquireNextImageKHR",
            Result.SuboptimalKhr,
            Result.ErrorOutOfDateKhr);
    }

    /// <summary>Presents a rendered image once the given semaphore has been signalled.</summary>
    /// <param name="wait">The semaphore that must signal before the image is shown — the render's completion.</param>
    /// <param name="imageIndex">The index of the image to present.</param>
    /// <returns>
    /// The present result, with <see cref="Result.SuboptimalKhr"/> and
    /// <see cref="Result.ErrorOutOfDateKhr"/> tolerated so the caller can rebuild the swapchain.
    /// </returns>
    public Result Present(Semaphore wait, uint imageIndex)
    {
        SwapchainKHR swapchain = _handle;
        uint index = imageIndex;
        Semaphore waitSemaphore = wait;
        PresentInfoKHR info = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &index,
        };

        return VkCheck.Allow(
            _khrSwapchain.QueuePresent(_device.Present, &info),
            "vkQueuePresentKHR",
            Result.SuboptimalKhr,
            Result.ErrorOutOfDateKhr);
    }

    /// <summary>Rebuilds the swapchain at a new size, waiting for the device to go idle first.</summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    /// <param name="vsync">Whether presentation should wait for the vertical blank.</param>
    /// <remarks>A zero dimension — a minimised window — is left for the caller to skip rather than rebuilt.</remarks>
    public void Recreate(int width, int height, bool vsync)
    {
        _device.WaitIdle();
        DestroyViewsAndSwapchain();
        Create(width, height, vsync);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyViewsAndSwapchain();
    }

    private void Create(int width, int height, bool vsync)
    {
        SurfaceCapabilitiesKHR capabilities;
        VkCheck.Ok(
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_device.PhysicalDevice, _surface, &capabilities),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

        SurfaceFormatKHR format = ChooseFormat();
        PresentModeKHR presentMode = ChoosePresentMode(vsync);
        Extent2D extent = ChooseExtent(capabilities, width, height);

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR createInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,

            // Transfer-source too, so a frame can be copied back to the CPU for a screenshot.
            ImageUsage = ImageUsageFlags.ColorAttachmentBit
                | ImageUsageFlags.TransferDstBit
                | ImageUsageFlags.TransferSrcBit,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default,
        };

        QueueFamilyIndices queues = _device.Selection.Queues;
        uint* familyIndices = stackalloc uint[2] { queues.Graphics, queues.Present };
        if (queues.GraphicsAndPresentUnified)
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }
        else
        {
            // The images are touched by both families, so they are shared rather than owned — simpler
            // than an ownership transfer, and the split-family case is rare enough not to optimise.
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;
            createInfo.PQueueFamilyIndices = familyIndices;
        }

        SwapchainKHR swapchain;
        VkCheck.Ok(_khrSwapchain.CreateSwapchain(_device.Device, &createInfo, null, &swapchain), "vkCreateSwapchainKHR");
        _handle = swapchain;
        Format = format.Format;
        Extent = extent;

        AcquireImages();
        CreateImageViews();
    }

    private SurfaceFormatKHR ChooseFormat()
    {
        uint count = 0;
        VkCheck.Ok(
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_device.PhysicalDevice, _surface, &count, null),
            "vkGetPhysicalDeviceSurfaceFormatsKHR");
        if (count == 0)
        {
            throw new GraphicsDeviceException("The surface reports no formats.");
        }

        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* first = formats)
        {
            VkCheck.Ok(
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_device.PhysicalDevice, _surface, &count, first),
                "vkGetPhysicalDeviceSurfaceFormatsKHR");
        }

        foreach (SurfaceFormatKHR candidate in formats)
        {
            if (candidate.Format == Format.B8G8R8A8Unorm
                && candidate.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return candidate;
            }
        }

        // No exact match — the first advertised format is always valid, and the engine encodes its own
        // output, so a different unsigned-normalised format still shows the right picture.
        return formats[0];
    }

    private PresentModeKHR ChoosePresentMode(bool vsync)
    {
        if (vsync)
        {
            // FIFO is the only mode the spec guarantees, and it is exactly the "wait for the blank"
            // v-sync asks for.
            return PresentModeKHR.FifoKhr;
        }

        uint count = 0;
        VkCheck.Ok(
            _khrSurface.GetPhysicalDeviceSurfacePresentModes(_device.PhysicalDevice, _surface, &count, null),
            "vkGetPhysicalDeviceSurfacePresentModesKHR");

        PresentModeKHR[] modes = new PresentModeKHR[count];
        fixed (PresentModeKHR* first = modes)
        {
            VkCheck.Ok(
                _khrSurface.GetPhysicalDeviceSurfacePresentModes(_device.PhysicalDevice, _surface, &count, first),
                "vkGetPhysicalDeviceSurfacePresentModesKHR");
        }

        bool mailbox = false;
        bool immediate = false;
        foreach (PresentModeKHR mode in modes)
        {
            mailbox |= mode == PresentModeKHR.MailboxKhr;
            immediate |= mode == PresentModeKHR.ImmediateKhr;
        }

        // Mailbox shows the freshest frame without tearing and is preferred; immediate tears but never
        // waits; FIFO is the guaranteed fallback.
        if (mailbox)
        {
            return PresentModeKHR.MailboxKhr;
        }

        return immediate ? PresentModeKHR.ImmediateKhr : PresentModeKHR.FifoKhr;
    }

    private static Extent2D ChooseExtent(in SurfaceCapabilitiesKHR capabilities, int width, int height)
    {
        // A driver that fixes the extent reports it in currentExtent; otherwise the window's size is
        // used, clamped to the range the surface allows.
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        uint clampedWidth = Math.Clamp(
            (uint)Math.Max(width, 1),
            capabilities.MinImageExtent.Width,
            capabilities.MaxImageExtent.Width);
        uint clampedHeight = Math.Clamp(
            (uint)Math.Max(height, 1),
            capabilities.MinImageExtent.Height,
            capabilities.MaxImageExtent.Height);
        return new Extent2D(clampedWidth, clampedHeight);
    }

    private void AcquireImages()
    {
        uint count = 0;
        VkCheck.Ok(
            _khrSwapchain.GetSwapchainImages(_device.Device, _handle, &count, null),
            "vkGetSwapchainImagesKHR");

        _images = new Image[count];
        fixed (Image* first = _images)
        {
            VkCheck.Ok(
                _khrSwapchain.GetSwapchainImages(_device.Device, _handle, &count, first),
                "vkGetSwapchainImagesKHR");
        }
    }

    private void CreateImageViews()
    {
        _imageViews = new ImageView[_images.Length];
        for (int i = 0; i < _images.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _images[i],
                ViewType = ImageViewType.Type2D,
                Format = Format,
                Components = default,
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
            VkCheck.Ok(_vk.CreateImageView(_device.Device, &createInfo, null, &view), "vkCreateImageView");
            _imageViews[i] = view;
        }
    }

    private void DestroyViewsAndSwapchain()
    {
        foreach (ImageView view in _imageViews)
        {
            _vk.DestroyImageView(_device.Device, view, null);
        }

        _imageViews = [];

        if (_handle.Handle != 0)
        {
            _khrSwapchain.DestroySwapchain(_device.Device, _handle, null);
            _handle = default;
        }

        _images = [];
    }
}
