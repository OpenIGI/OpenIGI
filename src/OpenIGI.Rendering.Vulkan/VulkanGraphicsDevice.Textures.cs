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

        VulkanTexture texture = new(
            _instance.Vk, _device, _allocator, _commands, description, pixels, _maxAnisotropy, TextureMipLodBias());
        uint id = _nextTextureId++;
        _textures[id] = texture;

        // The one moment the pixels are in hand. A traced ray needs the cut-out shape of anything it can
        // hit, and there is nowhere later to get it from without keeping every texture's alpha on the
        // host, so a cut-out texture is reduced to its mask here and nowhere else.
        if (texture.HasCutoutAlpha)
        {
            _opacityMasks?.Register(id, pixels, description.Width, description.Height);
        }

        // And its colour, for every texture large enough to be a world surface rather than interface art
        // -- a reflected ray can strike anything, not only the cut-outs. The array turns away what it
        // does not want, so the size question lives there rather than here.
        _tracedAlbedo?.Register(id, pixels, description.Width, description.Height);

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

        // The albedo layer goes back to the array. Unlike the masks, which are a handful per level, this
        // holds hundreds -- so a second mission would exhaust it during its own load if the first
        // mission's textures never let go of theirs.
        _tracedAlbedo?.Release(texture.Id);

        // The texture and the descriptor sets that name it may still be referenced by a frame in flight;
        // both the descriptor frees and the image dispose are queued and run once those frames cycle,
        // rather than stalling the device to be sure it is idle.
        uint id = texture.Id;
        QueueDeletion(() =>
        {
            _textureDescriptors.Forget(id);
            _meshTextureDescriptors.Forget(id);

            // The pairing a diffuse held, and any pairing that named this map. Identifiers are never
            // reused, so a stale entry would mispair nothing -- but a session that loads mission after
            // mission would accumulate one per textured surface it had ever seen.
            _textureDetail.Remove(id);
            foreach (uint diffuse in _textureDetail.Where(pair => pair.Value == id).Select(pair => pair.Key).ToList())
            {
                _textureDetail.Remove(diffuse);
            }
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
    /// <para>
    /// Two situations, one texture. Mid-frame -- the ordinary case, the menu holding the outgoing level
    /// after the resolve and before the interface goes over it -- the copy is recorded into the frame's
    /// own command buffer: the present pass is closed (which leaves the swapchain image presentable), the
    /// image is moved to a transfer source, copied, moved to a colour attachment, and the pass is
    /// re-opened through <see cref="Pipelines.PresentTarget.ResumeRenderPass"/> so what the resolve drew
    /// survives. The draws that follow can sample the copy in the same frame.
    /// </para>
    /// <para>
    /// Between frames -- a loading screen holding whatever was last on screen before a blocking load --
    /// there is no frame to record into, so the last PRESENTED image is copied with one blocking submit,
    /// the way a one-off screenshot is. That reads an image the presentation engine may still be
    /// scanning out; it is a once-per-load event and the worst case is a tear in a picture that is about
    /// to be dimmed under a panel.
    /// </para>
    /// </remarks>
    public unsafe void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
        ThrowIfDisposed();
        if (!_textures.TryGetValue(texture.Id, out VulkanTexture? destination))
        {
            throw new ArgumentException($"Unknown texture {texture}.", nameof(texture));
        }

        if (_frameOpen)
        {
            EnsurePresentPass();
            EndPresentPassIfActive();

            CommandBuffer cmd = _frameCommandBuffer;
            Image source = _swapchain.ImageAt(_frameImageIndex);
            TransitionSwapchainImage(
                cmd, source, ImageLayout.PresentSrcKhr, ImageLayout.TransferSrcOptimal,
                AccessFlags.ColorAttachmentWriteBit, AccessFlags.TransferReadBit);
            destination.CopyFromImage(cmd, source, ImageLayout.TransferSrcOptimal, width, height);
            TransitionSwapchainImage(
                cmd, source, ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal,
                AccessFlags.TransferReadBit, AccessFlags.ColorAttachmentWriteBit);

            _presentPassResumes = true;
            EnsurePresentPass();
            return;
        }

        if (!_hasPresented)
        {
            return;
        }

        Image last = _swapchain.ImageAt(_frameImageIndex);
        _commands.SingleTime(cmd =>
        {
            TransitionSwapchainImage(
                cmd, last, ImageLayout.PresentSrcKhr, ImageLayout.TransferSrcOptimal, 0, AccessFlags.TransferReadBit);
            destination.CopyFromImage(cmd, last, ImageLayout.TransferSrcOptimal, width, height);
            TransitionSwapchainImage(
                cmd, last, ImageLayout.TransferSrcOptimal, ImageLayout.PresentSrcKhr, AccessFlags.TransferReadBit, 0);
        });
    }

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

    /// <summary>Builds the surface-detail map a diffuse with none of its own is given.</summary>
    /// <returns>A one-pixel map whose every channel is the identity of what it drives.</returns>
    /// <remarks>
    /// Not white, and the exact values are the point. A flat tangent normal is a half in x and y, since
    /// the channel stores the range minus one to one biased into zero to one; a roughness multiplier of
    /// one is stored halved, so a half again; and the alpha holds how DIELECTRIC a surface is, so opaque
    /// leaves the material's own metallic alone. Every term of the detail block therefore folds to what
    /// it would have been, and no draw needs a flag saying whether it has a map.
    /// </remarks>
    private TextureHandle CreateNeutralDetailTexture()
    {
        Span<Bgra32> neutral = [new Bgra32(128, 128, 128, 255)];
        return CreateTexture(new TextureDescription(1, 1), neutral);
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
