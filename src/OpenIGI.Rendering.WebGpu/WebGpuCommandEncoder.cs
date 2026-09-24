using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// Records WebGPU work: passes, copies and queries.
/// </summary>
/// <remarks>
/// There are no barriers, and that is not an omission. WebGPU tracks what every resource is ready for
/// itself, which is exactly why the RHI was shaped this way: the Vulkan implementation has a barrier
/// batcher to write and this one has nothing at all.
/// </remarks>
public sealed unsafe class WebGpuCommandEncoder : IRhiCommandEncoder
{
    private readonly WebGpuRhiDevice _device;
    private WGPUCommandEncoder _handle;
    private bool _finished;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuCommandEncoder"/> class.</summary>
    /// <param name="device">The device recording it.</param>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuCommandEncoder(WebGpuRhiDevice device, WGPUCommandEncoder handle, string label)
    {
        _device = device;
        _handle = handle;
        Label = label;
    }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public IRhiRenderPassEncoder BeginRenderPass(RhiRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfFinished();

        using WebGpuString label = new(descriptor.Label);
        WGPURenderPassColorAttachment[] colours =
            new WGPURenderPassColorAttachment[Math.Max(descriptor.ColorAttachments.Count, 1)];

        for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
        {
            RhiColorAttachment attachment = descriptor.ColorAttachments[i];
            colours[i] = new WGPURenderPassColorAttachment
            {
                view = ((WebGpuTextureView)attachment.View).Handle,
                resolveTarget = attachment.ResolveTarget is WebGpuTextureView resolve ? resolve.Handle : default,
                loadOp = WebGpuTranslation.LoadOp(attachment.Load),
                storeOp = WebGpuTranslation.StoreOp(attachment.Store),
                clearValue = new WGPUColor
                {
                    r = attachment.ClearValue.R,
                    g = attachment.ClearValue.G,
                    b = attachment.ClearValue.B,
                    a = attachment.ClearValue.A,
                },

                // WebGPU wants the slice of a volume attachment stated, and the sentinel for "this is
                // not one" is the largest unsigned value rather than zero -- zero is slice zero.
                depthSlice = uint.MaxValue,
            };
        }

        WGPURenderPassDepthStencilAttachment depth = default;
        WGPUPassTimestampWrites timestamps = default;

        fixed (WGPURenderPassColorAttachment* firstColour = colours)
        {
            WGPURenderPassDescriptor native = new()
            {
                label = label.View,
                colorAttachmentCount = (nuint)descriptor.ColorAttachments.Count,
                colorAttachments = descriptor.ColorAttachments.Count > 0 ? firstColour : null,
                occlusionQuerySet = descriptor.OcclusionQuerySet is WebGpuQuerySet queries
                    ? queries.Handle
                    : default,
            };

            if (descriptor.DepthAttachment is { } attachment)
            {
                depth = new WGPURenderPassDepthStencilAttachment
                {
                    view = ((WebGpuTextureView)attachment.View).Handle,
                    depthLoadOp = WebGpuTranslation.LoadOp(attachment.DepthLoad),
                    depthStoreOp = WebGpuTranslation.StoreOp(attachment.DepthStore),
                    depthClearValue = attachment.DepthClearValue,
                    depthReadOnly = attachment.DepthReadOnly ? 1u : 0u,
                };

                // A depth-only format has no stencil aspect, and stating an operation for one it does
                // not have is a validation failure rather than something ignored.
                if (attachment.View.Format.HasStencil())
                {
                    depth.stencilLoadOp = WGPULoadOp.Clear;
                    depth.stencilStoreOp = WGPUStoreOp.Discard;
                }

                native.depthStencilAttachment = &depth;
            }

            if (descriptor.Timestamps is { } writes)
            {
                timestamps = new WGPUPassTimestampWrites
                {
                    querySet = ((WebGpuQuerySet)writes.QuerySet).Handle,
                    beginningOfPassWriteIndex = writes.BeginIndex < 0 ? uint.MaxValue : (uint)writes.BeginIndex,
                    endOfPassWriteIndex = writes.EndIndex < 0 ? uint.MaxValue : (uint)writes.EndIndex,
                };

                native.timestampWrites = &timestamps;
            }

            WGPURenderPassEncoder pass = Wgpu.wgpuCommandEncoderBeginRenderPass(_handle, &native);
            return pass.IsValid
                ? new WebGpuRenderPassEncoder(pass, _device.PushRing, WebGpuPushBlocks.Group, _device)
                : throw new InvalidOperationException($"The render pass '{descriptor.Label}' could not begin.");
        }
    }

    /// <inheritdoc/>
    public IRhiComputePassEncoder BeginComputePass(in RhiComputePassDescriptor descriptor = default)
    {
        ThrowIfFinished();
        using WebGpuString label = new(descriptor.Label);

        WGPUComputePassDescriptor native = new() { label = label.View };
        WGPUPassTimestampWrites timestamps = default;

        if (descriptor.Timestamps is { } writes)
        {
            timestamps = new WGPUPassTimestampWrites
            {
                querySet = ((WebGpuQuerySet)writes.QuerySet).Handle,

                // WebGPU's own "no index here" is the largest unsigned value rather than a negative one,
                // which the interface uses because it has no unsigned index anywhere else.
                beginningOfPassWriteIndex = writes.BeginIndex < 0 ? uint.MaxValue : (uint)writes.BeginIndex,
                endOfPassWriteIndex = writes.EndIndex < 0 ? uint.MaxValue : (uint)writes.EndIndex,
            };

            native.timestampWrites = &timestamps;
        }

        WGPUComputePassEncoder pass = Wgpu.wgpuCommandEncoderBeginComputePass(_handle, &native);
        return pass.IsValid
            ? new WebGpuComputePassEncoder(pass, _device.PushRing, WebGpuPushBlocks.Group, _device)
            : throw new InvalidOperationException($"The compute pass '{descriptor.Label}' could not begin.");
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

        Wgpu.wgpuCommandEncoderCopyBufferToBuffer(
            _handle,
            ((WebGpuBuffer)source).Handle,
            sourceOffset,
            ((WebGpuBuffer)destination).Handle,
            destinationOffset,
            sizeInBytes);
    }

    /// <inheritdoc/>
    public void CopyBufferToTexture(
        in RhiBufferTextureCopy source,
        in RhiTextureRegion destination,
        in RhiExtent3D size)
    {
        ThrowIfFinished();

        WGPUTexelCopyBufferInfo from = BufferSide(source);
        WGPUTexelCopyTextureInfo to = TextureSide(destination);
        WGPUExtent3D extent = Extent(size);

        Wgpu.wgpuCommandEncoderCopyBufferToTexture(_handle, &from, &to, &extent);
    }

    /// <inheritdoc/>
    public void CopyTextureToBuffer(
        in RhiTextureRegion source,
        in RhiBufferTextureCopy destination,
        in RhiExtent3D size)
    {
        ThrowIfFinished();

        WGPUTexelCopyTextureInfo from = TextureSide(source);
        WGPUTexelCopyBufferInfo to = BufferSide(destination);
        WGPUExtent3D extent = Extent(size);

        Wgpu.wgpuCommandEncoderCopyTextureToBuffer(_handle, &from, &to, &extent);
    }

    /// <inheritdoc/>
    public void CopyTextureToTexture(
        in RhiTextureRegion source,
        in RhiTextureRegion destination,
        in RhiExtent3D size)
    {
        ThrowIfFinished();

        WGPUTexelCopyTextureInfo from = TextureSide(source);
        WGPUTexelCopyTextureInfo to = TextureSide(destination);
        WGPUExtent3D extent = Extent(size);

        Wgpu.wgpuCommandEncoderCopyTextureToTexture(_handle, &from, &to, &extent);
    }

    /// <inheritdoc/>
    public void ClearBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfFinished();

        ulong size = sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset;
        Wgpu.wgpuCommandEncoderClearBuffer(_handle, ((WebGpuBuffer)buffer).Handle, offset, size);
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

        Wgpu.wgpuCommandEncoderResolveQuerySet(
            _handle,
            ((WebGpuQuerySet)querySet).Handle,
            (uint)firstQuery,
            (uint)queryCount,
            ((WebGpuBuffer)destination).Handle,
            destinationOffset);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Where the device's scheduler runs work beside the caller, the finish itself goes to it and this
    /// returns at once: finishing is where wgpu validates and translates everything recorded, milliseconds
    /// for a frame's draws, and none of that needs the recording thread. The submission waits for it. A
    /// browser has one thread and an inline scheduler, so there it is finished here, as it always was.
    /// </remarks>
    public IRhiCommandBuffer Finish(string label = "")
    {
        ThrowIfFinished();
        _finished = true;

        // The encoder goes with the finish, which releases it; this object's disposal then has nothing to do.
        WGPUCommandEncoder handle = _handle;
        _handle = default;

        if (_device.Finishing.IsConcurrent)
        {
            return new WebGpuFinishingCommandBuffer(_device.Finishing.Run(() => FinishAndRelease(handle, label)), label);
        }

        WGPUCommandBuffer commands = FinishAndRelease(handle, label);
        return commands.IsValid
            ? new WebGpuCommandBuffer(commands, label)
            : throw new InvalidOperationException($"The encoder '{Label}' produced no commands.");
    }

    private static WGPUCommandBuffer FinishAndRelease(WGPUCommandEncoder handle, string label)
    {
        try
        {
            using WebGpuString name = new(label);
            WGPUCommandBufferDescriptor native = new() { label = name.View };
            return Wgpu.wgpuCommandEncoderFinish(handle, &native);
        }
        finally
        {
            Wgpu.wgpuCommandEncoderRelease(handle);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle.IsValid)
        {
            Wgpu.wgpuCommandEncoderRelease(_handle);
            _handle = default;
        }
    }

    private static WGPUExtent3D Extent(in RhiExtent3D size) => new()
    {
        width = (uint)size.Width,
        height = (uint)size.Height,
        depthOrArrayLayers = (uint)size.DepthOrArrayLayers,
    };

    private static WGPUTexelCopyTextureInfo TextureSide(in RhiTextureRegion region) => new()
    {
        texture = ((WebGpuTexture)region.Texture).Handle,
        mipLevel = (uint)region.MipLevel,
        origin = new WGPUOrigin3D
        {
            x = (uint)region.Origin.X,
            y = (uint)region.Origin.Y,
            z = (uint)region.Origin.Z,
        },
        aspect = WGPUTextureAspect.All,
    };

    private static WGPUTexelCopyBufferInfo BufferSide(in RhiBufferTextureCopy copy) => new()
    {
        buffer = ((WebGpuBuffer)copy.Buffer).Handle,
        layout = new WGPUTexelCopyBufferLayout
        {
            offset = copy.Offset,
            bytesPerRow = (uint)copy.BytesPerRow,
            rowsPerImage = (uint)copy.RowsPerImage,
        },
    };

    private void ThrowIfFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finished)
        {
            throw new InvalidOperationException($"The encoder '{Label}' has already been finished.");
        }
    }
}

/// <summary>Records WebGPU dispatches.</summary>
public sealed unsafe class WebGpuComputePassEncoder : IRhiComputePassEncoder
{
    private readonly WebGpuRhiDevice _device;
    private readonly WebGpuPushRing _pushes;
    private readonly int _pushGroup;
    private WGPUComputePassEncoder _handle;
    private bool _ended;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuComputePassEncoder"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="pushes">The ring push data is written into.</param>
    /// <param name="pushGroup">The bind group the push block is bound as.</param>
    /// <param name="device">The device whose push group this binds.</param>
    public WebGpuComputePassEncoder(
        WGPUComputePassEncoder handle,
        WebGpuPushRing pushes,
        int pushGroup,
        WebGpuRhiDevice device)
    {
        _handle = handle;
        _pushes = pushes;
        _pushGroup = pushGroup;
        _device = device;
    }

    /// <summary>Gets the group the push block is bound as.</summary>
    public int PushGroup => _pushGroup;

    /// <summary>Gets where the last push landed in the ring.</summary>
    public uint LastPushOffset { get; private set; }

    /// <inheritdoc/>
    public void SetPipeline(IRhiComputePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ThrowIfEnded();

        WebGpuHotCalls.wgpuComputePassEncoderSetPipeline(_handle.Handle, ((WebGpuComputePipeline)pipeline).Handle.Handle);
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ThrowIfEnded();

        fixed (uint* offsets = dynamicOffsets)
        {
            WebGpuHotCalls.wgpuComputePassEncoderSetBindGroup(
                _handle.Handle,
                (uint)index,
                ((WebGpuBindGroup)group).Handle.Handle,
                (nuint)dynamicOffsets.Length,
                offsets);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same ring the raster side uses, for the same reason: WebGPU has no push constants, so a push
    /// is a slice of a uniform buffer bound by dynamic offset. Only the write happens here; the binding
    /// is the caller's, because the group it goes in belongs to a pipeline layout this cannot see.
    /// </remarks>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0)
    {
        ThrowIfEnded();

        if (offset != 0)
        {
            throw new NotSupportedException(
                "A partial push is not something this backend can do: the block is a uniform binding here, "
                + "and a binding is written whole. Set the whole block.");
        }

        // The write AND the binding, for the reason the raster side gives: nothing above this knows the
        // ring, the group index or where the write landed.
        WebGpuPushSlice landed = _pushes.Write(data);
        LastPushOffset = landed.Offset;

        uint at = landed.Offset;
        SetBindGroup(_pushGroup, landed.Group, new ReadOnlySpan<uint>(in at));
    }

    /// <inheritdoc/>
    public void Dispatch(int x, int y = 1, int z = 1)
    {
        ThrowIfEnded();
        WebGpuHotCalls.wgpuComputePassEncoderDispatchWorkgroups(_handle.Handle, (uint)x, (uint)y, (uint)z);
    }

    /// <inheritdoc/>
    public void DispatchIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        Wgpu.wgpuComputePassEncoderDispatchWorkgroupsIndirect(
            _handle, ((WebGpuBuffer)arguments).Handle, offset);
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label)
    {
        ThrowIfEnded();
        using WebGpuString name = new(label);
        Wgpu.wgpuComputePassEncoderPushDebugGroup(_handle, name.View);
    }

    /// <inheritdoc/>
    public void PopDebugGroup()
    {
        ThrowIfEnded();
        Wgpu.wgpuComputePassEncoderPopDebugGroup(_handle);
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfEnded();
        Wgpu.wgpuComputePassEncoderEnd(_handle);
        _ended = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_ended && _handle.IsValid)
        {
            Wgpu.wgpuComputePassEncoderEnd(_handle);
            _ended = true;
        }

        if (_handle.IsValid)
        {
            Wgpu.wgpuComputePassEncoderRelease(_handle);
            _handle = default;
        }
    }

    private void ThrowIfEnded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ended)
        {
            throw new InvalidOperationException("The compute pass has already ended.");
        }
    }
}
