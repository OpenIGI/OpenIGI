using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// Records Vulkan work, inserting the barriers the interface promises nobody has to think about.
/// </summary>
/// <remarks>
/// The whole asymmetry between the two backends lives here. WebGPU tracks what every resource is ready
/// for and needs none of this; Vulkan does not, so a pass boundary is a barrier and a bound texture is a
/// layout transition. Both are conservative: the stages are the widest that could apply, because a
/// narrower pair would have to know what the caller is about to do and the interface deliberately does
/// not say.
/// </remarks>
internal sealed unsafe class VulkanRhiCommandEncoder : IRhiCommandEncoder
{
    private readonly VulkanRhiDevice _device;
    private readonly Vk _vk;
    private readonly CommandBuffer _command;
    private readonly List<VulkanRhiBuffer> _written = [];
    private bool _finished;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiCommandEncoder"/> class.</summary>
    /// <param name="device">The device recording it.</param>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="command">The command buffer, already begun.</param>
    /// <param name="label">The name it was created with.</param>
    public VulkanRhiCommandEncoder(VulkanRhiDevice device, Vk vk, CommandBuffer command, string label)
    {
        _device = device;
        _vk = vk;
        _command = command;
        Label = label;
    }

    /// <inheritdoc/>
    public string Label { get; }

    /// <summary>Gets the command buffer being recorded, for backend passes that record Vulkan directly.</summary>
    internal CommandBuffer Command => _command;

    /// <inheritdoc/>
    public IRhiRenderPassEncoder BeginRenderPass(RhiRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfFinished();
        _device.RequireDynamicRendering();

        Barrier();

        List<VulkanRhiTexture> attached = [];
        RenderingAttachmentInfo[] colours =
            new RenderingAttachmentInfo[Math.Max(descriptor.ColorAttachments.Count, 1)];

        int width = int.MaxValue;
        int height = int.MaxValue;

        for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
        {
            RhiColorAttachment attachment = descriptor.ColorAttachments[i];
            VulkanRhiTextureView view = (VulkanRhiTextureView)attachment.View;

            // Into the attachment layout before the pass begins: a transition inside one is not allowed,
            // and General is not a layout a colour attachment may be written in.
            _device.Transition(_command, view.Owner, ImageLayout.ColorAttachmentOptimal);
            attached.Add(view.Owner);

            width = Math.Min(width, view.Owner.Width);
            height = Math.Min(height, view.Owner.Height);

            colours[i] = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = view.Handle,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = VulkanRhiTranslation.LoadOp(attachment.Load),
                StoreOp = VulkanRhiTranslation.StoreOp(attachment.Store),
                ClearValue = new ClearValue
                {
                    Color = new ClearColorValue(
                        attachment.ClearValue.R,
                        attachment.ClearValue.G,
                        attachment.ClearValue.B,
                        attachment.ClearValue.A),
                },
            };

            if (attachment.ResolveTarget is VulkanRhiTextureView resolve)
            {
                _device.Transition(_command, resolve.Owner, ImageLayout.ColorAttachmentOptimal);
                attached.Add(resolve.Owner);

                colours[i].ResolveImageView = resolve.Handle;
                colours[i].ResolveImageLayout = ImageLayout.ColorAttachmentOptimal;
                colours[i].ResolveMode = ResolveModeFlags.AverageBit;
            }
        }

        RenderingAttachmentInfo depth = default;
        bool hasDepth = descriptor.DepthAttachment is not null;

        if (descriptor.DepthAttachment is { } depthAttachment)
        {
            VulkanRhiTextureView view = (VulkanRhiTextureView)depthAttachment.View;
            ImageLayout layout = depthAttachment.DepthReadOnly
                ? ImageLayout.DepthStencilReadOnlyOptimal
                : ImageLayout.DepthStencilAttachmentOptimal;

            _device.Transition(_command, view.Owner, layout);
            attached.Add(view.Owner);

            width = Math.Min(width, view.Owner.Width);
            height = Math.Min(height, view.Owner.Height);

            depth = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = view.Handle,
                ImageLayout = layout,
                LoadOp = VulkanRhiTranslation.LoadOp(depthAttachment.DepthLoad),
                StoreOp = VulkanRhiTranslation.StoreOp(depthAttachment.DepthStore),
                ClearValue = new ClearValue
                {
                    DepthStencil = new ClearDepthStencilValue(depthAttachment.DepthClearValue, 0),
                },
            };
        }

        if (width == int.MaxValue || height == int.MaxValue)
        {
            throw new GraphicsDeviceException(
                $"The render pass '{descriptor.Label}' has no attachments, so it has no area to draw in.");
        }

        // The timestamps a pass would write have no dynamic-rendering equivalent: Vulkan writes them
        // with commands around the pass rather than as part of it -- the beginning here, the end when
        // the pass ends.
        VulkanRhiTimestamps.Begin(_vk, _command, descriptor.Timestamps);

        // Vulkan resets a query pool rather than overwriting it, and a query written before it was reset
        // is invalid. The reset is an outside-a-pass command, so it goes here, before rendering begins --
        // and only when the set has been resolved since it was last reset: a set a frame opens in two
        // passes (two scene captures) keeps the first pass's results for the one resolve at the end.
        if (descriptor.OcclusionQuerySet is VulkanRhiQuerySet { NeedsReset: true } occlusion)
        {
            _vk.CmdResetQueryPool(_command, occlusion.Handle, 0, (uint)occlusion.Count);
            occlusion.NeedsReset = false;
        }

        fixed (RenderingAttachmentInfo* firstColour = colours)
        {
            RenderingInfo info = new()
            {
                SType = StructureType.RenderingInfo,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)width, (uint)height)),
                LayerCount = 1,
                ColorAttachmentCount = (uint)descriptor.ColorAttachments.Count,
                PColorAttachments = descriptor.ColorAttachments.Count > 0 ? firstColour : null,
                PDepthAttachment = hasDepth ? &depth : null,
            };

            _vk.CmdBeginRendering(_command, &info);
        }

        return new VulkanRhiRenderPassEncoder(
            _device,
            _vk,
            _command,
            attached,
            descriptor.OcclusionQuerySet as VulkanRhiQuerySet,
            descriptor.Timestamps);
    }

    /// <inheritdoc/>
    public IRhiComputePassEncoder BeginComputePass(in RhiComputePassDescriptor descriptor = default)
    {
        ThrowIfFinished();

        // Everything written before this pass has to be visible to it. One barrier at the boundary is
        // what the interface's promise costs, and it is where WebGPU pays nothing.
        Barrier();
        VulkanRhiTimestamps.Begin(_vk, _command, descriptor.Timestamps);

        return new VulkanRhiComputePassEncoder(_device, _vk, _command, descriptor.Timestamps);
    }

    /// <inheritdoc/>
    public void CopyBufferToBuffer(
        IRhiBuffer source,
        ulong sourceOffset,
        IRhiBuffer destination,
        ulong destinationOffset,
        ulong sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfFinished();
        Barrier();
        _written.Add((VulkanRhiBuffer)destination);

        BufferCopy region = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = sizeInBytes,
        };

        _vk.CmdCopyBuffer(
            _command,
            ((VulkanRhiBuffer)source).Buffer.Handle,
            ((VulkanRhiBuffer)destination).Buffer.Handle,
            1,
            &region);
    }

    /// <inheritdoc/>
    public void CopyBufferToTexture(
        in RhiBufferTextureCopy source,
        in RhiTextureRegion destination,
        in RhiExtent3D size)
    {
        ThrowIfFinished();
        Barrier();

        VulkanRhiTexture texture = (VulkanRhiTexture)destination.Texture;
        _device.Transition(_command, texture, ImageLayout.TransferDstOptimal);

        BufferImageCopy copy = Copy(source, destination, size, texture);
        _vk.CmdCopyBufferToImage(
            _command,
            ((VulkanRhiBuffer)source.Buffer).Buffer.Handle,
            texture.Image,
            ImageLayout.TransferDstOptimal,
            1,
            &copy);

        _device.Transition(_command, texture, ImageLayout.General);
    }

    /// <inheritdoc/>
    public void CopyTextureToBuffer(
        in RhiTextureRegion source,
        in RhiBufferTextureCopy destination,
        in RhiExtent3D size)
    {
        ThrowIfFinished();
        Barrier();
        _written.Add((VulkanRhiBuffer)destination.Buffer);

        VulkanRhiTexture texture = (VulkanRhiTexture)source.Texture;
        _device.Transition(_command, texture, ImageLayout.TransferSrcOptimal);

        BufferImageCopy copy = Copy(destination, source, size, texture);
        _vk.CmdCopyImageToBuffer(
            _command,
            texture.Image,
            ImageLayout.TransferSrcOptimal,
            ((VulkanRhiBuffer)destination.Buffer).Buffer.Handle,
            1,
            &copy);

        _device.Transition(_command, texture, ImageLayout.General);
    }

    /// <inheritdoc/>
    public void CopyTextureToTexture(
        in RhiTextureRegion source,
        in RhiTextureRegion destination,
        in RhiExtent3D size)
    {
        ThrowIfFinished();
        Barrier();

        VulkanRhiTexture from = (VulkanRhiTexture)source.Texture;
        VulkanRhiTexture to = (VulkanRhiTexture)destination.Texture;

        _device.Transition(_command, from, ImageLayout.TransferSrcOptimal);
        _device.Transition(_command, to, ImageLayout.TransferDstOptimal);

        ImageCopy region = new()
        {
            SrcSubresource = Layers(from, source),
            SrcOffset = new Offset3D(source.Origin.X, source.Origin.Y, 0),
            DstSubresource = Layers(to, destination),
            DstOffset = new Offset3D(destination.Origin.X, destination.Origin.Y, 0),
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, (uint)size.DepthOrArrayLayers),
        };

        _vk.CmdCopyImage(
            _command,
            from.Image,
            ImageLayout.TransferSrcOptimal,
            to.Image,
            ImageLayout.TransferDstOptimal,
            1,
            &region);

        _device.Transition(_command, from, ImageLayout.General);
        _device.Transition(_command, to, ImageLayout.General);
    }

    /// <inheritdoc/>
    public void ClearBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfFinished();
        Barrier();

        ulong size = sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset;
        _vk.CmdFillBuffer(_command, ((VulkanRhiBuffer)buffer).Buffer.Handle, offset, size, 0);
    }

    /// <inheritdoc/>
    public void ResolveQuerySet(
        IRhiQuerySet querySet,
        int firstQuery,
        int queryCount,
        IRhiBuffer destination,
        ulong destinationOffset)
    {
        ArgumentNullException.ThrowIfNull(querySet);
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfFinished();
        Barrier();
        _written.Add((VulkanRhiBuffer)destination);

        // Eight bytes a result, and NOT waited for. The barrier above has every write recorded before
        // this finished, so each query written is available; one that never was -- a pass that asked
        // for a timestamp and was not recorded -- is left out rather than waited for, because a wait on
        // a query nothing will ever write never ends, and hangs the GPU.
        _vk.CmdCopyQueryPoolResults(
            _command,
            ((VulkanRhiQuerySet)querySet).Handle,
            (uint)firstQuery,
            (uint)queryCount,
            ((VulkanRhiBuffer)destination).Buffer.Handle,
            destinationOffset,
            sizeof(ulong),
            QueryResultFlags.Result64Bit);

        // Read, so the next pass to write the set starts it afresh.
        if (querySet is VulkanRhiQuerySet { Type: RhiQueryType.Occlusion } occlusion)
        {
            occlusion.NeedsReset = true;
        }
    }

    /// <inheritdoc/>
    public IRhiCommandBuffer Finish(string label = "")
    {
        ThrowIfFinished();

        VkCheck.Ok(_vk.EndCommandBuffer(_command), "vkEndCommandBuffer");
        _finished = true;

        return new VulkanRhiCommandBuffer(_command, label.Length > 0 ? label : Label, [.. _written]);
    }

    /// <inheritdoc/>
    public void Dispose() => _disposed = true;

    /// <summary>Makes everything written so far visible to everything that follows.</summary>
    private void Barrier()
    {
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };

        _vk.CmdPipelineBarrier(
            _command,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null);
    }

    private static BufferImageCopy Copy(
        in RhiBufferTextureCopy buffer,
        in RhiTextureRegion region,
        in RhiExtent3D size,
        VulkanRhiTexture texture)
    {
        // Vulkan counts a padded row in TEXELS where everything else counts bytes, and states an
        // unpadded row as zero rather than as its own width. The texel size comes from the FORMAT:
        // dividing the padded row by its width gives the padding back, which reads as a copy that works
        // for the first row and returns nothing for the rest.
        int texelBytes = texture.Format.BytesPerTexel();
        uint rowLength = texelBytes > 0 && buffer.BytesPerRow != size.Width * texelBytes
            ? (uint)(buffer.BytesPerRow / texelBytes)
            : 0;

        return new BufferImageCopy
        {
            BufferOffset = buffer.Offset,
            BufferRowLength = rowLength,
            BufferImageHeight = 0,
            ImageSubresource = Layers(texture, region),
            ImageOffset = new Offset3D(region.Origin.X, region.Origin.Y, 0),
            ImageExtent = new Extent3D(
                (uint)size.Width,
                (uint)size.Height,
                (uint)(texture.Dimension == RhiTextureViewDimension.Texture3D ? size.DepthOrArrayLayers : 1)),
        };
    }

    private static ImageSubresourceLayers Layers(VulkanRhiTexture texture, in RhiTextureRegion region) => new()
    {
        AspectMask = texture.Aspects,
        MipLevel = (uint)region.MipLevel,
        BaseArrayLayer = (uint)(texture.Dimension == RhiTextureViewDimension.Texture3D ? 0 : region.Origin.Z),
        LayerCount = 1,
    };

    private void ThrowIfFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finished)
        {
            throw new GraphicsDeviceException($"The encoder '{Label}' has already been finished.");
        }
    }
}

/// <summary>Records Vulkan dispatches.</summary>
internal sealed unsafe class VulkanRhiComputePassEncoder : IRhiComputePassEncoder
{
    private readonly VulkanRhiDevice _device;
    private readonly Vk _vk;
    private readonly CommandBuffer _command;
    private readonly RhiTimestampWrites? _timestamps;
    private VulkanRhiPipelineLayout? _layout;
    private bool _dispatched;
    private bool _ended;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiComputePassEncoder"/> class.</summary>
    /// <param name="device">The device recording it.</param>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="command">The command buffer.</param>
    /// <param name="timestamps">The timestamps the pass writes; the end one is written as it ends.</param>
    public VulkanRhiComputePassEncoder(
        VulkanRhiDevice device,
        Vk vk,
        CommandBuffer command,
        RhiTimestampWrites? timestamps = null)
    {
        _device = device;
        _vk = vk;
        _command = command;
        _timestamps = timestamps;
    }

    /// <inheritdoc/>
    public void SetPipeline(IRhiComputePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ThrowIfEnded();

        VulkanRhiComputePipeline compute = (VulkanRhiComputePipeline)pipeline;
        _layout = (VulkanRhiPipelineLayout)compute.Layout;

        _vk.CmdBindPipeline(_command, PipelineBindPoint.Compute, compute.Handle);
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ThrowIfEnded();

        VulkanRhiBindGroup bound = (VulkanRhiBindGroup)group;

        // Every texture it binds into the layout a shader reads. This is the other half of what the
        // interface's promise costs: WebGPU knows what its bind groups hold and does this itself.
        foreach ((VulkanRhiTexture texture, bool _) in bound.Textures)
        {
            _device.Transition(_command, texture, ImageLayout.General);
        }

        DescriptorSet set = bound.Set;
        fixed (uint* offsets = dynamicOffsets)
        {
            _vk.CmdBindDescriptorSets(
                _command,
                PipelineBindPoint.Compute,
                Layout().Handle,
                (uint)index,
                1,
                &set,
                (uint)dynamicOffsets.Length,
                dynamicOffsets.Length > 0 ? offsets : null);
        }
    }

    /// <inheritdoc/>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0)
    {
        ThrowIfEnded();

        VulkanRhiPipelineLayout layout = Layout();
        fixed (byte* bytes = data)
        {
            _vk.CmdPushConstants(
                _command,
                layout.Handle,
                layout.PushDataVisibility,
                (uint)offset,
                (uint)data.Length,
                bytes);
        }
    }

    /// <inheritdoc/>
    public void Dispatch(int x, int y = 1, int z = 1)
    {
        ThrowIfEnded();
        Chain();
        _vk.CmdDispatch(_command, (uint)x, (uint)y, (uint)z);
    }

    /// <inheritdoc/>
    public void DispatchIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        Chain();
        _vk.CmdDispatchIndirect(_command, ((VulkanRhiBuffer)arguments).Buffer.Handle, offset);
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label)
    {
    }

    /// <inheritdoc/>
    public void PopDebugGroup()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Vulkan has no compute pass to end, so what this ends is the interface's notion of one. The
    /// barrier that makes what was dispatched visible is emitted by whatever begins next.
    /// </remarks>
    public void EndPass()
    {
        ThrowIfEnded();
        _ended = true;
        VulkanRhiTimestamps.End(_vk, _command, _timestamps);
    }

    /// <inheritdoc/>
    /// <remarks>A pass left to a <c>using</c> block ends here, and writes its end timestamp.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_ended)
        {
            EndPass();
        }

        _disposed = true;
    }

    /// <summary>Makes the dispatch before this one visible to this one.</summary>
    /// <remarks>
    /// WebGPU treats every dispatch in a pass as its own usage scope and synchronises between them;
    /// Vulkan synchronises nothing it is not told to. Without this, a chain of dependent passes recorded
    /// into one compute pass -- which is how the atmosphere and the temporal resolve are written -- reads
    /// tables the dispatch before is still writing, and produces an answer that is merely plausible and
    /// differs from run to run. The first dispatch needs none: the pass boundary already barriered.
    /// </remarks>
    private void Chain()
    {
        if (!_dispatched)
        {
            _dispatched = true;
            return;
        }

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
        };

        _vk.CmdPipelineBarrier(
            _command,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null);
    }

    private VulkanRhiPipelineLayout Layout() => _layout
        ?? throw new GraphicsDeviceException("No pipeline is bound, so there is no layout to bind against.");

    private void ThrowIfEnded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ended)
        {
            throw new GraphicsDeviceException("The compute pass has already ended.");
        }
    }
}

/// <summary>A buffer's contents, read back once the work that wrote them has finished.</summary>
/// <remarks>
/// A future because the interface says so, not because Vulkan needs one: a browser cannot block and
/// nothing above may assume this one can. What it does is wait on the device and copy, which is what
/// makes it complete.
/// </remarks>
internal sealed class VulkanRhiReadback : IRhiReadback
{
    private readonly VulkanRhiDevice _device;
    private readonly VulkanRhiBuffer _buffer;
    private readonly ulong _offset;
    private readonly ulong _ticket;
    private byte[]? _data;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiReadback"/> class.</summary>
    /// <param name="device">The device to wait on.</param>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="offset">Where the read starts.</param>
    /// <param name="sizeInBytes">How many bytes.</param>
    /// <param name="ticket">The submission count that has to be reached before the bytes are there.</param>
    public VulkanRhiReadback(VulkanRhiDevice device, VulkanRhiBuffer buffer, ulong offset, ulong sizeInBytes, ulong ticket)
    {
        _device = device;
        _buffer = buffer;
        _offset = offset;
        _ticket = ticket;
        SizeInBytes = sizeInBytes;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Looks without waiting: the submission that wrote the bytes has finished, or it has not. Before
    /// submissions were counted this could only be answered by idling the whole device, which the sky's
    /// harmonics did once a frame.
    /// </remarks>
    public bool IsComplete => _data is not null || Wait(TimeSpan.Zero);

    /// <inheritdoc/>
    public ulong SizeInBytes { get; }

    /// <inheritdoc/>
    public bool Wait(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_data is not null)
        {
            return true;
        }

        if (_device.HasTimeline && _buffer.Buffer.HostVisible)
        {
            if (!_device.WaitForTicket(_ticket, timeout))
            {
                return false;
            }

            _data = VulkanRhiDevice.ReadFinished(_buffer, _offset, SizeInBytes);
            return true;
        }

        _data = _device.ReadBack(_buffer, _offset, SizeInBytes);
        return true;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetData()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _data ?? throw new InvalidOperationException("The readback has not completed.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        _data = null;
    }
}
