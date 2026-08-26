using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>Texture creation, update, binding and readback for <see cref="VulkanGraphicsDevice"/>.</summary>
public sealed partial class VulkanGraphicsDevice
{
    private readonly TextureHandle[] _boundTextures = new TextureHandle[16];

    /// <inheritdoc/>
    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        if (description.Width <= 0 || description.Height <= 0)
        {
            throw new GraphicsDeviceException($"Texture {description.Width}x{description.Height} has a non-positive dimension.");
        }

        int required = 0;
        for (int level = 0; level < Math.Max(1, description.MipLevels); level++)
        {
            (int levelWidth, int levelHeight) = description.GetMipSize(level);
            required += Math.Max(1, levelWidth) * Math.Max(1, levelHeight);
        }

        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"The pixel span holds {pixels.Length} texels but the mip chain needs {required}.",
                nameof(pixels));
        }

        VulkanTexture texture = new(_instance.Vk, _device, _allocator, _commands, description, pixels, _maxAnisotropy);
        uint id = _nextTextureId++;
        _textures[id] = texture;
        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        if (!_textures.TryGetValue(texture.Id, out VulkanTexture? resource))
        {
            throw new ArgumentException($"Unknown texture {texture}.", nameof(texture));
        }

        resource.UpdateLevel(_device, _allocator, _commands, mipLevel, pixels);
    }

    /// <inheritdoc/>
    public void DestroyTexture(TextureHandle texture)
    {
        ThrowIfDisposed();
        if (texture == TextureHandle.None)
        {
            return;
        }

        // A storage texture or volume lives in its own table; free it there when that is what the handle
        // names, so a destroyed volume is not leaked.
        if (_storageImages.Remove(texture.Id, out Compute.VulkanStorageImage? image))
        {
            QueueDeletion(image);
            return;
        }

        if (!_textures.Remove(texture.Id, out VulkanTexture? resource))
        {
            return;
        }

        // The texture and the descriptor sets that name it may still be referenced by a frame in flight;
        // both the descriptor frees and the image dispose are queued and run once those frames cycle,
        // rather than stalling the device to be sure it is idle.
        uint id = texture.Id;
        QueueDeletion(() =>
        {
            _textureDescriptors.Forget(id);
            _meshTextureDescriptors.Forget(id);
            resource.Dispose();
        });
    }

    /// <inheritdoc/>
    public void SetTexture(int unit, TextureHandle texture)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(unit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(unit, _boundTextures.Length);
        _boundTextures[unit] = texture;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The foundation presents straight to the swapchain, so there is no captured colour to copy from
    /// yet; this waits for the offscreen scene target the deferred path adds.
    /// </remarks>
    public void CopyFrameToTexture(TextureHandle texture, int width, int height) => ThrowIfDisposed();

    /// <inheritdoc/>
    /// <remarks>
    /// A CPU pixel blit for splash and movie frames. The foundation does not yet route it through the
    /// 2D path, so it is a no-op reported once rather than a silent drop.
    /// </remarks>
    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        if (pixels.Length < width * height)
        {
            throw new ArgumentException("The pixel span is shorter than the blit rectangle.", nameof(pixels));
        }

        WarnOnce("BlitPixels", "BlitPixels is not yet implemented in the Vulkan backend; the frame is unchanged.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Finalises the current frame with a copy of the back buffer to the CPU: the present pass closes,
    /// the swapchain image is copied to a host buffer, and the frame is submitted and presented before
    /// the buffer is read. The subsequent <see cref="SwapBuffers"/> then finds the frame already closed.
    /// </remarks>
    public void CaptureFrame(int width, int height, Span<Bgra32> destination)
    {
        ThrowIfDisposed();
        if (destination.Length < width * height)
        {
            throw new ArgumentException("The destination is shorter than the capture rectangle.", nameof(destination));
        }

        Span<Bgra32> target = destination[..(width * height)];

        // A consumer is asking for the frame. If the cheap per-SwapBuffers copy was already running — the
        // menu backdrop or a screenshot loop calls this every frame — the staging buffer already holds the
        // last presented frame. If it was not (ordinary gameplay taking a one-off screenshot), grab the last
        // presented frame now with a single synchronous copy. Either way, keep the per-frame copy alive for
        // a few more frames so a caller that keeps asking gets the cheap path.
        bool wasActive = _captureAlways || _captureCountdown > 0;
        _captureCountdown = 3;

        // Only where the per-frame copy was not already running is there nothing in the staging buffer to
        // read. That path reads an image the presentation engine may still be scanning out, which is a
        // race no synchronisation available here can close — so a session that intends to capture says so
        // at construction and never reaches it.
        if (!wasActive && _hasPresented)
        {
            SynchronousCapture();
        }

        if (_captureStaging is null || _captureExtent.Width == 0)
        {
            target.Fill(Bgra32.Black);
            return;
        }

        _device.WaitIdle();

        int captureWidth = (int)_captureExtent.Width;
        int captureHeight = (int)_captureExtent.Height;
        Bgra32[] whole = new Bgra32[captureWidth * captureHeight];
        _captureStaging.Read<Bgra32>(whole);

        int copyWidth = Math.Min(width, captureWidth);
        int copyHeight = Math.Min(height, captureHeight);
        for (int y = 0; y < copyHeight; y++)
        {
            whole.AsSpan(y * captureWidth, copyWidth).CopyTo(target.Slice(y * width, copyWidth));
        }
    }

    /// <summary>Copies the last presented frame into the staging buffer with a single blocking submit.</summary>
    /// <remarks>
    /// For a one-off screenshot from a session that is not otherwise capturing: the frame has already been
    /// presented, so there is no frame recording to disturb, and a synchronous copy of the presented image
    /// is the simplest correct way to read it back on demand.
    /// </remarks>
    private unsafe void SynchronousCapture()
    {
        Extent2D extent = _presentTarget.Extent;
        if (_captureStaging is null || _captureExtent.Width != extent.Width || _captureExtent.Height != extent.Height)
        {
            _device.WaitIdle();
            _captureStaging?.Dispose();
            _captureStaging = new VulkanBuffer(
                _instance.Vk,
                _device,
                _allocator,
                (ulong)(extent.Width * extent.Height * Bgra32.Size),
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            _captureExtent = extent;
        }

        Image image = _swapchain.ImageAt(_frameImageIndex);
        VulkanBuffer staging = _captureStaging;
        _commands.SingleTime(cmd =>
        {
            TransitionSwapchainImage(cmd, image, ImageLayout.PresentSrcKhr, ImageLayout.TransferSrcOptimal, 0, AccessFlags.TransferReadBit);
            BufferImageCopy region = new()
            {
                ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new Extent3D(extent.Width, extent.Height, 1),
            };
            _instance.Vk.CmdCopyImageToBuffer(cmd, image, ImageLayout.TransferSrcOptimal, staging.Handle, 1, &region);
            TransitionSwapchainImage(cmd, image, ImageLayout.TransferSrcOptimal, ImageLayout.PresentSrcKhr, AccessFlags.TransferReadBit, 0);
        });
    }

    private unsafe void RecordBackbufferCapture()
    {
        Extent2D extent = _presentTarget.Extent;
        if (_captureStaging is null || _captureExtent.Width != extent.Width || _captureExtent.Height != extent.Height)
        {
            _device.WaitIdle();
            _captureStaging?.Dispose();
            _captureStaging = new VulkanBuffer(
                _instance.Vk,
                _device,
                _allocator,
                (ulong)(extent.Width * extent.Height * Bgra32.Size),
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            _captureExtent = extent;
        }

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;
        Image image = _swapchain.ImageAt(_frameImageIndex);

        TransitionSwapchainImage(cmd, image, ImageLayout.PresentSrcKhr, ImageLayout.TransferSrcOptimal, 0, AccessFlags.TransferReadBit);
        BufferImageCopy region = new()
        {
            ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
            ImageExtent = new Extent3D(extent.Width, extent.Height, 1),
        };
        vk.CmdCopyImageToBuffer(cmd, image, ImageLayout.TransferSrcOptimal, _captureStaging.Handle, 1, &region);
        TransitionSwapchainImage(cmd, image, ImageLayout.TransferSrcOptimal, ImageLayout.PresentSrcKhr, AccessFlags.TransferReadBit, 0);
    }

    private unsafe void TransitionSwapchainImage(
        CommandBuffer cmd,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
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
                LevelCount = 1,
                LayerCount = 1,
            },
            SrcAccessMask = sourceAccess,
            DstAccessMask = destinationAccess,
        };

        _instance.Vk.CmdPipelineBarrier(
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

    private TextureHandle CreateWhiteTexture()
    {
        Span<Bgra32> white = [Bgra32.White];
        return CreateTexture(new TextureDescription(1, 1), white);
    }

    private VulkanTexture ResolveTextureOrWhite(TextureHandle texture)
    {
        if (texture != TextureHandle.None && _textures.TryGetValue(texture.Id, out VulkanTexture? resource))
        {
            return resource;
        }

        return _textures[_whiteTexture.Id];
    }
}
