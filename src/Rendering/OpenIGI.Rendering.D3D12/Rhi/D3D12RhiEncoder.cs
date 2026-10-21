using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.D3D12.Sync;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>Records one submission's work into a Direct3D 12 command list, barriers included.</summary>
/// <remarks>
/// The RHI has no barriers, so this decides them from what each command uses: every command tells the
/// <see cref="D3D12BarrierTracker"/> what it reads and writes, and the barriers that makes due are issued
/// together just before it. Enhanced barriers only. A texture's layout before the list's first use of it is
/// the submission's business, which moves it there from wherever the lists submitted before left it.
/// </remarks>
internal sealed unsafe class D3D12RhiCommandEncoder : IRhiCommandEncoder
{
    private readonly D3D12RhiDevice _device;
    private readonly List<D3D12RhiBuffer> _written = [];
    private readonly D3D12BarrierTracker _tracker = new();
    private ID3D12GraphicsCommandList7* _list;
    private ID3D12CommandAllocator* _allocator;
    private readonly bool _counted;
    private bool _inPass;
    private bool _finished;

    // Whether something outside the backend has recorded into the list since the heaps were bound.
    private bool _heapsStale;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiCommandEncoder"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="allocator">The allocator it records from.</param>
    /// <param name="list">The list, open and reset.</param>
    /// <param name="label">A name.</param>
    /// <param name="counted">Whether the device counts it as open until it is finished, which only a caller's encoder is.</param>
    public D3D12RhiCommandEncoder(D3D12RhiDevice device, ID3D12CommandAllocator* allocator, ID3D12GraphicsCommandList7* list, string label, bool counted = false)
    {
        _device = device;
        _counted = counted;
        _allocator = allocator;
        _list = list;
        Label = label;

        BindHeaps();
    }

    /// <inheritdoc/>
    public string Label { get; }

    /// <summary>Gets the device it records for.</summary>
    public D3D12RhiDevice Device => _device;

    /// <summary>Gets the list being recorded, for a backend part that records into it directly.</summary>
    public ID3D12GraphicsCommandList7* List => _list;

    /// <summary>Hands the list to code outside the backend -- Streamline, and NVIDIA's features through it.</summary>
    /// <returns>The list.</returns>
    /// <remarks>
    /// What records into it there binds descriptor heaps of its own and leaves them bound: after a ray
    /// reconstruction or DLSS evaluation the list's heaps were NGX's, and every descriptor table this backend
    /// set after it named a heap that was not the one bound, which the debug layer reports on every dispatch
    /// and the hardware is free to read garbage through. The heaps are bound again before the next command.
    /// </remarks>
    public ID3D12GraphicsCommandList7* LendList()
    {
        _heapsStale = true;
        return _list;
    }

    /// <inheritdoc/>
    public IRhiRenderPassEncoder BeginRenderPass(RhiRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        BeginPassOrCommand();

        foreach (RhiColorAttachment attachment in descriptor.ColorAttachments)
        {
            Use(((D3D12RhiTextureView)attachment.View).Owner, D3D12Use.RenderTarget);
        }

        if (descriptor.DepthAttachment is { } depthUse)
        {
            Use(((D3D12RhiTextureView)depthUse.View).Owner, depthUse.DepthReadOnly ? D3D12Use.DepthRead : D3D12Use.DepthWrite);
        }

        FlushBarriers();

        int count = descriptor.ColorAttachments.Count;
        CpuDescriptorHandle* targets = stackalloc CpuDescriptorHandle[Math.Max(1, count)];
        float* colour = stackalloc float[4];
        int width = int.MaxValue;
        int height = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RhiColorAttachment attachment = descriptor.ColorAttachments[i];
            D3D12RhiTextureView view = (D3D12RhiTextureView)attachment.View;
            targets[i] = view.RenderTarget();
            width = Math.Min(width, Math.Max(1, view.Owner.Width >> view.Descriptor.BaseMipLevel));
            height = Math.Min(height, Math.Max(1, view.Owner.Height >> view.Descriptor.BaseMipLevel));

            if (attachment.Load == RhiLoadOp.Clear)
            {
                RhiClearColor clear = attachment.ClearValue;
                colour[0] = clear.R;
                colour[1] = clear.G;
                colour[2] = clear.B;
                colour[3] = clear.A;
                _list->ClearRenderTargetView(targets[i], colour, 0, null);
            }
        }

        CpuDescriptorHandle depth = default;
        bool hasDepth = false;
        if (descriptor.DepthAttachment is { } depthAttachment)
        {
            D3D12RhiTextureView view = (D3D12RhiTextureView)depthAttachment.View;
            depth = view.DepthStencil(depthAttachment.DepthReadOnly);
            hasDepth = true;
            width = Math.Min(width, Math.Max(1, view.Owner.Width >> view.Descriptor.BaseMipLevel));
            height = Math.Min(height, Math.Max(1, view.Owner.Height >> view.Descriptor.BaseMipLevel));

            if (depthAttachment.DepthLoad == RhiLoadOp.Clear && !depthAttachment.DepthReadOnly)
            {
                _list->ClearDepthStencilView(depth, ClearFlags.Depth, depthAttachment.DepthClearValue, 0, 0, null);
            }
        }

        _list->OMSetRenderTargets((uint)count, count > 0 ? targets : null, false, hasDepth ? &depth : null);

        if (width == int.MaxValue)
        {
            width = height = 1;
        }

        Silk.NET.Direct3D12.Viewport viewport = new(0, 0, width, height, 0, 1);
        Silk.NET.Maths.Box2D<int> scissor = new(0, 0, width, height);
        _list->RSSetViewports(1, &viewport);
        _list->RSSetScissorRects(1, &scissor);

        BeginEvent(descriptor.Label);
        WriteTimestamp(descriptor.Timestamps, begin: true);

        _inPass = true;
        return new D3D12RhiRenderPassEncoder(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiComputePassEncoder BeginComputePass(in RhiComputePassDescriptor descriptor = default)
    {
        BeginPassOrCommand();
        BeginEvent(descriptor.Label);
        WriteTimestamp(descriptor.Timestamps, begin: true);
        _inPass = true;
        return new D3D12RhiComputePassEncoder(this, descriptor);
    }

    /// <inheritdoc/>
    public void CopyBufferToBuffer(IRhiBuffer source, ulong sourceOffset, IRhiBuffer destination, ulong destinationOffset, ulong sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        BeginPassOrCommand();

        D3D12RhiBuffer to = (D3D12RhiBuffer)destination;
        D3D12RhiBuffer from = (D3D12RhiBuffer)source;
        Use(from, D3D12Use.CopySource);
        Use(to, D3D12Use.CopyDest);
        FlushBarriers();
        _list->CopyBufferRegion(to.Resource, to.Offset + destinationOffset, from.Resource, from.Offset + sourceOffset, sizeInBytes);
    }

    /// <inheritdoc/>
    public void CopyBufferToTexture(in RhiBufferTextureCopy source, in RhiTextureRegion destination, in RhiExtent3D size)
    {
        BeginPassOrCommand();
        Use((D3D12RhiBuffer)source.Buffer, D3D12Use.CopySource);
        Use((D3D12RhiTexture)destination.Texture, D3D12Use.CopyDest);
        FlushBarriers();
        CopyFootprints(
            (D3D12RhiBuffer)source.Buffer,
            source.Offset,
            source.BytesPerRow,
            source.RowsPerImage,
            (D3D12RhiTexture)destination.Texture,
            destination.MipLevel,
            destination.Origin,
            size,
            toTexture: true);
    }

    /// <inheritdoc/>
    public void CopyTextureToBuffer(in RhiTextureRegion source, in RhiBufferTextureCopy destination, in RhiExtent3D size)
    {
        BeginPassOrCommand();
        D3D12RhiBuffer buffer = (D3D12RhiBuffer)destination.Buffer;
        Use((D3D12RhiTexture)source.Texture, D3D12Use.CopySource);
        Use(buffer, D3D12Use.CopyDest);
        FlushBarriers();
        CopyFootprints(
            buffer,
            destination.Offset,
            destination.BytesPerRow,
            destination.RowsPerImage,
            (D3D12RhiTexture)source.Texture,
            source.MipLevel,
            source.Origin,
            size,
            toTexture: false);
    }

    /// <inheritdoc/>
    public void CopyTextureToTexture(in RhiTextureRegion source, in RhiTextureRegion destination, in RhiExtent3D size)
    {
        BeginPassOrCommand();

        D3D12RhiTexture from = (D3D12RhiTexture)source.Texture;
        D3D12RhiTexture to = (D3D12RhiTexture)destination.Texture;
        Use(from, D3D12Use.CopySource);
        Use(to, D3D12Use.CopyDest);
        FlushBarriers();
        bool volume = from.Descriptor.Dimension == RhiTextureViewDimension.Texture3D;
        int layers = volume ? 1 : size.DepthOrArrayLayers;

        for (int layer = 0; layer < layers; layer++)
        {
            TextureCopyLocation src = new()
            {
                PResource = from.Resource,
                Type = TextureCopyType.SubresourceIndex,
            };
            src.SubresourceIndex = Subresource(from, source.MipLevel, volume ? 0 : source.Origin.Z + layer);

            TextureCopyLocation dst = new()
            {
                PResource = to.Resource,
                Type = TextureCopyType.SubresourceIndex,
            };
            dst.SubresourceIndex = Subresource(to, destination.MipLevel, volume ? 0 : destination.Origin.Z + layer);

            Box box = new()
            {
                Left = (uint)source.Origin.X,
                Top = (uint)source.Origin.Y,
                Front = volume ? (uint)source.Origin.Z : 0,
                Right = (uint)(source.Origin.X + size.Width),
                Bottom = (uint)(source.Origin.Y + size.Height),
                Back = volume ? (uint)(source.Origin.Z + size.DepthOrArrayLayers) : 1,
            };

            // A depth or multisampled copy is all of the subresource or nothing, and Direct3D refuses a box.
            bool whole = from.Format.IsDepth() || from.SampleCount > 1;
            _list->CopyTextureRegion(
                &dst,
                (uint)destination.Origin.X,
                (uint)destination.Origin.Y,
                volume ? (uint)destination.Origin.Z : 0,
                &src,
                whole ? null : &box);
        }
    }

    /// <inheritdoc/>
    public void ClearBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        BeginPassOrCommand();

        D3D12RhiBuffer target = (D3D12RhiBuffer)buffer;
        Use(target, D3D12Use.CopyDest);
        FlushBarriers();
        ulong length = sizeInBytes == 0 ? buffer.SizeInBytes - offset : sizeInBytes;

        // Direct3D 12 clears a buffer only through a UAV it would have to be created with; copies from a
        // buffer of zeroes do it for any buffer the device can copy into. Disjoint ranges of one buffer
        // need nothing between them.
        D3D12RhiBuffer zeroes = _device.Zeroes;
        for (ulong done = 0; done < length; done += zeroes.SizeInBytes)
        {
            _list->CopyBufferRegion(target.Resource, target.Offset + offset + done, zeroes.Resource, 0, Math.Min(zeroes.SizeInBytes, length - done));
        }
    }

    /// <inheritdoc/>
    public void ResolveQuerySet(IRhiQuerySet querySet, int firstQuery, int queryCount, IRhiBuffer destination, ulong destinationOffset)
    {
        ArgumentNullException.ThrowIfNull(querySet);
        ArgumentNullException.ThrowIfNull(destination);
        BeginPassOrCommand();

        D3D12RhiQuerySet set = (D3D12RhiQuerySet)querySet;
        D3D12RhiBuffer target = (D3D12RhiBuffer)destination;
        Use(target, D3D12Use.CopyDest);
        FlushBarriers();
        _list->ResolveQueryData(set.Heap, set.NativeType, (uint)firstQuery, (uint)queryCount, target.Resource, target.Offset + destinationOffset);
    }

    /// <inheritdoc/>
    public void BuildAccelerationStructures(
        ReadOnlySpan<RhiBottomLevelBuild> bottomLevel,
        RhiTopLevelBuild? topLevel,
        RhiTimestampWrites? timestamps = null)
    {
        BeginPassOrCommand();
        if (_device.AccelerationStructures is null)
        {
            throw new NotSupportedException("This device builds no acceleration structures.");
        }

        BeginEvent("acceleration structures");

        // Whatever wrote the geometry, and whatever traced the structures about to be rebuilt, is done first.
        GlobalBarrier();
        WriteTimestamp(timestamps, begin: true);
        D3D12RhiAccelerationStructures.RecordBuilds(this, bottomLevel, topLevel);
        WriteTimestamp(timestamps, begin: false);
        EndEvent();
    }

    /// <inheritdoc/>
    public IRhiCommandBuffer Finish(string label = "")
    {
        if (_finished)
        {
            throw new InvalidOperationException("This encoder has already been finished.");
        }

        if (_inPass)
        {
            throw new InvalidOperationException("A pass is still open; end it before finishing the encoder.");
        }

        _finished = true;
        if (_counted)
        {
            _device.EncoderClosed();
        }

        D3D12Check.Ok(_list->Close(), "ID3D12GraphicsCommandList::Close");
        D3D12RhiCommandBuffer buffer = new(_list, _allocator, label.Length > 0 ? label : Label, [.. _written], _tracker.HandOffs());
        _list = null;
        _allocator = null;
        return buffer;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Recorded and never finished: the list and its allocator go back to the pool unexecuted.
        if (!_finished && _list is not null)
        {
            _list->Close();
            _device.ReturnList(_allocator, _list, ticket: 0);
            _list = null;
            _allocator = null;
            _finished = true;
            if (_counted)
            {
                _device.EncoderClosed();
            }
        }
    }

    /// <summary>Records a barrier making everything written so far visible to everything recorded after.</summary>
    /// <remarks>For work the tracker cannot see: Streamline's, and acceleration-structure builds.</remarks>
    public void GlobalBarrier()
    {
        _tracker.RequireFull();
        FlushBarriers();
    }

    /// <summary>Records a texture's use by the next command.</summary>
    /// <param name="texture">The texture.</param>
    /// <param name="use">How.</param>
    public void Use(D3D12RhiTexture texture, D3D12Use use) => _tracker.UseTexture(texture, (nint)texture.Resource, use);

    /// <summary>Records a buffer's use by the next command.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="use">How.</param>
    public void Use(D3D12RhiBuffer buffer, D3D12Use use)
    {
        _tracker.UseBuffer(buffer, (nint)buffer.Resource, use);
        if (use is D3D12Use.CopyDest or D3D12Use.StorageWrite)
        {
            Written(buffer);
        }
    }

    /// <summary>Records a bind group's uses by the next draw or dispatch.</summary>
    /// <param name="group">The group.</param>
    public void Use(D3D12RhiBindGroup group)
    {
        foreach ((object resource, D3D12Use use) in group.Uses)
        {
            if (resource is D3D12RhiTexture texture)
            {
                Use(texture, use);
            }
            else
            {
                Use((D3D12RhiBuffer)resource, use);
            }
        }
    }

    /// <summary>Issues the barriers the uses recorded since the last command made due.</summary>
    public void FlushBarriers() => _tracker.Flush(_list);

    /// <summary>Called by a pass encoder when it ends.</summary>
    /// <param name="timestamps">Where it records its end, or null.</param>
    public void EndPass(RhiTimestampWrites? timestamps)
    {
        WriteTimestamp(timestamps, begin: false);
        _list->EndEvent();
        _inPass = false;
    }

    /// <summary>Records a timestamp into a query set.</summary>
    /// <param name="writes">The set and its slots, or null.</param>
    /// <param name="begin">Whether the beginning slot, rather than the end.</param>
    public void WriteTimestamp(RhiTimestampWrites? writes, bool begin)
    {
        if (writes is not { } timestamps)
        {
            return;
        }

        int index = begin ? timestamps.BeginIndex : timestamps.EndIndex;
        if (index < 0)
        {
            return;
        }

        D3D12RhiQuerySet set = (D3D12RhiQuerySet)timestamps.QuerySet;
        _list->EndQuery(set.Heap, QueryType.Timestamp, (uint)index);
    }

    /// <summary>Opens a named region the debug layer, PIX and DRED's breadcrumbs report work under.</summary>
    /// <param name="label">The name.</param>
    public void BeginEvent(string label)
    {
        string text = label.Length > 0 ? label : "pass";
        fixed (char* name = text)
        {
            // Metadata 1 is PIX's convention for a wide string, terminator included in the size.
            _list->BeginEvent(1, name, (uint)((text.Length + 1) * sizeof(char)));
        }
    }

    /// <summary>Closes the region <see cref="BeginEvent"/> opened.</summary>
    public void EndEvent() => _list->EndEvent();

    private void Written(D3D12RhiBuffer buffer)
    {
        if (!_written.Contains(buffer))
        {
            _written.Add(buffer);
        }
    }

    private void BeginPassOrCommand()
    {
        if (_finished)
        {
            throw new InvalidOperationException("This encoder has been finished.");
        }

        if (_inPass)
        {
            throw new InvalidOperationException("A pass is open; end it before recording anything else.");
        }

        if (_heapsStale)
        {
            BindHeaps();
        }
    }

    /// <summary>Binds the device's shader-visible heaps, which every table this backend sets points into.</summary>
    private void BindHeaps()
    {
        ID3D12DescriptorHeap** heaps = stackalloc ID3D12DescriptorHeap*[2] { _device.ShaderHeap.Heap, _device.SamplerHeap.Heap };
        _list->SetDescriptorHeaps(2, heaps);
        _heapsStale = false;
    }

    private void CopyFootprints(
        D3D12RhiBuffer buffer,
        ulong offset,
        int bytesPerRow,
        int rowsPerImage,
        D3D12RhiTexture texture,
        int mip,
        RhiOrigin3D origin,
        in RhiExtent3D size,
        bool toTexture)
    {
        bool volume = texture.Descriptor.Dimension == RhiTextureViewDimension.Texture3D;
        int layers = volume ? 1 : size.DepthOrArrayLayers;
        (_, int rows) = D3D12Translation.RowLayout(texture.Format, size.Width, size.Height);
        int imageRows = rowsPerImage > 0 ? (texture.Format.IsBlockCompressed() ? (rowsPerImage + 3) / 4 : rowsPerImage) : rows;
        Format format = D3D12Translation.ViewFormat(texture.Format.IsDepth() ? DepthCopyFormat(texture.Format) : texture.Format);

        for (int layer = 0; layer < layers; layer++)
        {
            ulong at = offset + ((ulong)layer * (ulong)imageRows * (ulong)bytesPerRow);
            if ((buffer.Offset + at) % 512 != 0)
            {
                throw new GraphicsDeviceException(
                    $"A copy between '{buffer.Label}' and '{texture.Label}' starts {at} bytes in; Direct3D 12 "
                    + "places a footprint on a 512-byte boundary.");
            }

            TextureCopyLocation footprint = new() { PResource = buffer.Resource, Type = TextureCopyType.PlacedFootprint };
            footprint.PlacedFootprint = new PlacedSubresourceFootprint
            {
                Offset = buffer.Offset + at,
                Footprint = new SubresourceFootprint(
                    format,
                    (uint)D3D12Translation.FootprintExtent(texture.Format, size.Width),
                    (uint)D3D12Translation.FootprintExtent(texture.Format, size.Height),
                    volume ? (uint)size.DepthOrArrayLayers : 1u,
                    (uint)bytesPerRow),
            };

            TextureCopyLocation subresource = new() { PResource = texture.Resource, Type = TextureCopyType.SubresourceIndex };
            subresource.SubresourceIndex = Subresource(texture, mip, volume ? 0 : origin.Z + layer);

            if (toTexture)
            {
                _list->CopyTextureRegion(&subresource, (uint)origin.X, (uint)origin.Y, volume ? (uint)origin.Z : 0, &footprint, null);
            }
            else
            {
                Box box = new()
                {
                    Left = (uint)origin.X,
                    Top = (uint)origin.Y,
                    Front = volume ? (uint)origin.Z : 0,
                    Right = (uint)(origin.X + size.Width),
                    Bottom = (uint)(origin.Y + size.Height),
                    Back = volume ? (uint)(origin.Z + size.DepthOrArrayLayers) : 1,
                };
                _list->CopyTextureRegion(&footprint, 0, 0, 0, &subresource, texture.Format.IsDepth() ? null : &box);
            }
        }
    }

    /// <summary>The colour format a depth texture's bytes are copied as: its depth plane's own layout.</summary>
    private static RhiTextureFormat DepthCopyFormat(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.Depth32Float => RhiTextureFormat.R32Float,
        _ => throw new GraphicsDeviceException($"A {format} texture is not copied to or from a buffer on Direct3D 12."),
    };

    /// <summary>A subresource's index: its mip, slice and (for depth) plane.</summary>
    public static uint Subresource(D3D12RhiTexture texture, int mip, int slice) =>
        (uint)(mip + (slice * texture.MipLevelCount));
}
