using System.Runtime.InteropServices;
using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.D3D12.Sync;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>Command lists, the writes queued ahead of the next submission, and submitting.</summary>
/// <remarks>
/// <para>
/// <b>Submission.</b> Every submit goes to the queue as it is made and signals the fence. There is no
/// acquire to wait on and no present semaphore to signal -- DXGI orders a present after the work queued
/// before it -- so nothing is held for the frame's end as the Vulkan backend has to.
/// </para>
/// <para>
/// <b>One list per call.</b> Each command list is an <c>ExecuteCommandLists</c> call of its own. The end of
/// a call is a full finish and cache flush, which is what lets a list's tracker treat its first use of any
/// resource as free of hazards; lists sharing one call would run unsynchronised against each other. Before a
/// list goes, a small one moves every texture it uses from the layout the lists before it left it in to the
/// layout it first needs -- out of undefined with a discard, for a render or depth target's first use.
/// </para>
/// <para>
/// <b>Writes.</b> <c>WriteBuffer</c> and <c>WriteTexture</c> are WebGPU's queue writes: they land before any
/// work submitted after them, and nobody waits. The bytes go into a staging chunk in upload memory and the
/// copy into an upload list, which executes first in the next submission, a call of its own ahead of the rest.
/// </para>
/// <para>
/// <b>Lifetimes.</b> An allocator, a staging chunk and anything disposed are kept until the fence passes the
/// submission that last used them. A closed list is reset as soon as it has been executed; only its
/// allocator has to wait.
/// </para>
/// </remarks>
internal sealed unsafe partial class D3D12RhiDevice
{
    private const ulong StagingChunkBytes = 4UL * 1024 * 1024;
    private const ulong MaxPendingUploadBytes = 64UL * 1024 * 1024;
    private const int MaxFreeStagingChunks = 8;

    private readonly List<(ulong Ticket, nint Allocator)> _allocators = [];
    private readonly Stack<nint> _lists = new();
    private readonly List<D3D12RhiBuffer> _freeStaging = [];
    private readonly List<(ulong Ticket, D3D12RhiBuffer Chunk)> _usedStaging = [];

    // The upload being recorded: its list and allocator, the chunks it reads, what it has written.
    private D3D12RhiCommandEncoder? _upload;
    private readonly List<D3D12RhiBuffer> _uploadChunks = [];
    private ulong _uploadChunkUsed;
    private ulong _uploadBytes;

    /// <inheritdoc/>
    public IRhiCommandEncoder CreateCommandEncoder(string label = "")
    {
        ThrowIfDisposed();
        TakeList(out ID3D12CommandAllocator* allocator, out ID3D12GraphicsCommandList7* list);
        EncoderOpened();
        return new D3D12RhiCommandEncoder(this, allocator, list, label, counted: true);
    }

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<IRhiCommandBuffer> commands)
    {
        ThrowIfDisposed();
        D3D12RhiCommandBuffer[] buffers = new D3D12RhiCommandBuffer[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            D3D12RhiCommandBuffer buffer = (D3D12RhiCommandBuffer)commands[i];
            if (buffer.Submitted)
            {
                throw new GraphicsDeviceException($"'{buffer.Label}' has been submitted already.");
            }

            buffer.Submitted = true;
            buffers[i] = buffer;
        }

        Execute(buffers);
    }

    /// <inheritdoc/>
    /// <remarks>Everything is sent as it is submitted already; this sends the queued writes and waits.</remarks>
    public void Flush()
    {
        ThrowIfDisposed();
        Execute([]);
        Context.WaitFor(Context.Signalled, Timeout.InfiniteTimeSpan);
        ReleaseRetired();
    }

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ThrowIfDisposed();
        Execute([]);
        Context.WaitIdle();
        ReleaseRetired();
        ReleaseFinishedStaging();
    }

    /// <inheritdoc/>
    public IRhiWorkMarker MarkSubmittedWork()
    {
        ThrowIfDisposed();
        return new D3D12RhiWorkMarker(Context, Context.Signalled);
    }

    /// <inheritdoc/>
    public void WriteBuffer<T>(IRhiBuffer buffer, ulong offset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        D3D12RhiBuffer target = (D3D12RhiBuffer)buffer;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        if (bytes.IsEmpty)
        {
            return;
        }

        if (target.Mapped is not null && target.Heap != HeapType.Readback)
        {
            bytes.CopyTo(new Span<byte>(target.Mapped + offset, bytes.Length));
            return;
        }

        (D3D12RhiBuffer chunk, ulong at) = Stage((ulong)bytes.Length, 16);
        bytes.CopyTo(new Span<byte>(chunk.Mapped + at, bytes.Length));

        // Two copies into one buffer are not ordered on their own, and the later write is the one promised:
        // the tracker puts a barrier between them.
        D3D12RhiCommandEncoder upload = OpenUpload();
        upload.Use(target, D3D12Use.CopyDest);
        upload.FlushBarriers();
        upload.List->CopyBufferRegion(target.Resource, target.Offset + offset, chunk.Resource, chunk.Offset + at, (ulong)bytes.Length);
        Uploaded((ulong)bytes.Length);
    }

    /// <inheritdoc/>
    public void WriteTexture<T>(in RhiTextureRegion destination, ReadOnlySpan<T> data, int bytesPerRow, in RhiExtent3D size)
        where T : unmanaged
    {
        ThrowIfDisposed();
        D3D12RhiTexture texture = (D3D12RhiTexture)destination.Texture;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        if (bytes.IsEmpty)
        {
            return;
        }

        // Direct3D 12 places rows 256 bytes apart and a footprint on a 512-byte boundary, where the
        // caller's rows are packed: the rows are laid out again as they are staged.
        bool volume = texture.Descriptor.Dimension == RhiTextureViewDimension.Texture3D;
        int slices = volume ? size.DepthOrArrayLayers : 1;
        (int rowBytes, int rows) = D3D12Translation.RowLayout(texture.Format, size.Width, size.Height);
        int pitch = RhiBufferTextureCopy.AlignRow(rowBytes);
        ulong staged = (ulong)pitch * (ulong)rows * (ulong)slices;

        (D3D12RhiBuffer chunk, ulong at) = Stage(staged, 512);
        byte* target = chunk.Mapped + at;
        for (int slice = 0; slice < slices; slice++)
        {
            for (int row = 0; row < rows; row++)
            {
                int source = ((slice * rows) + row) * bytesPerRow;
                if (source + rowBytes > bytes.Length)
                {
                    throw new ArgumentException($"The data for '{texture.Label}' ends before row {row} of slice {slice}.", nameof(data));
                }

                bytes.Slice(source, rowBytes).CopyTo(new Span<byte>(target + (((slice * rows) + row) * pitch), rowBytes));
            }
        }

        D3D12RhiCommandEncoder upload = OpenUpload();
        upload.Use(texture, D3D12Use.CopyDest);
        upload.FlushBarriers();

        TextureCopyLocation footprint = new() { PResource = chunk.Resource, Type = TextureCopyType.PlacedFootprint };
        footprint.PlacedFootprint = new PlacedSubresourceFootprint
        {
            Offset = chunk.Offset + at,
            // A block-compressed footprint is whole 4x4 blocks, even for a mip smaller than one block: the
            // copy covers the mip's blocks, which is what its rows were staged as.
            Footprint = new SubresourceFootprint(
                D3D12Translation.ViewFormat(texture.Format),
                (uint)D3D12Translation.FootprintExtent(texture.Format, size.Width),
                (uint)D3D12Translation.FootprintExtent(texture.Format, size.Height),
                (uint)slices,
                (uint)pitch),
        };

        TextureCopyLocation subresource = new() { PResource = texture.Resource, Type = TextureCopyType.SubresourceIndex };
        subresource.SubresourceIndex = D3D12RhiCommandEncoder.Subresource(texture, destination.MipLevel, volume ? 0 : destination.Origin.Z);

        upload.List->CopyTextureRegion(
            &subresource,
            (uint)destination.Origin.X,
            (uint)destination.Origin.Y,
            volume ? (uint)destination.Origin.Z : 0,
            &footprint,
            null);
        Uploaded(staged);
    }

    /// <inheritdoc/>
    public IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();
        D3D12RhiBuffer source = (D3D12RhiBuffer)buffer;
        if (source.Heap != HeapType.Readback)
        {
            throw new GraphicsDeviceException($"'{source.Label}' was not created to be read back.");
        }

        ulong size = sizeInBytes == 0 ? source.SizeInBytes - offset : sizeInBytes;
        return new D3D12RhiReadback(this, source, offset, size, source.WriteTicket);
    }

    /// <summary>Gives a list and its allocator back: the list now, the allocator once the fence passes.</summary>
    /// <param name="allocator">The allocator.</param>
    /// <param name="list">The closed list.</param>
    /// <param name="ticket">The fence value after which the allocator's memory is free; nought for unexecuted.</param>
    public void ReturnList(ID3D12CommandAllocator* allocator, ID3D12GraphicsCommandList7* list, ulong ticket)
    {
        _lists.Push((nint)list);
        _allocators.Add((ticket, (nint)allocator));
    }

    /// <summary>Executes the queued writes and then the given lists, each a call of its own, and signals the fence after them.</summary>
    /// <param name="buffers">What to run after the writes.</param>
    /// <returns>The fence value it all completes at.</returns>
    private ulong Execute(D3D12RhiCommandBuffer[] buffers)
    {
        D3D12RhiCommandBuffer? upload = TakeUpload();
        if (buffers.Length == 0 && upload is null)
        {
            return Context.Signalled;
        }

        ApplyFrameWait();
        List<(nint Allocator, nint List)> fixups = [];
        if (upload is not null)
        {
            ExecuteOne(upload, fixups);
        }

        foreach (D3D12RhiCommandBuffer buffer in buffers)
        {
            ExecuteOne(buffer, fixups);
        }

        ulong ticket = Context.Signal();
        DateReleases(ticket);

        foreach ((nint allocator, nint list) in fixups)
        {
            ReturnList((ID3D12CommandAllocator*)allocator, (ID3D12GraphicsCommandList7*)list, ticket);
        }

        if (upload is not null)
        {
            ReturnList(upload.Allocator, upload.List, ticket);
            foreach (D3D12RhiBuffer chunk in _uploadChunks)
            {
                _usedStaging.Add((ticket, chunk));
            }

            _uploadChunks.Clear();
        }

        foreach (D3D12RhiCommandBuffer buffer in buffers)
        {
            foreach (D3D12RhiBuffer written in buffer.Written)
            {
                written.WriteTicket = ticket;
            }

            ReturnList(buffer.Allocator, buffer.List, ticket);
        }

        ReleaseRetired();
        ReleaseFinishedStaging();
        return ticket;
    }

    /// <summary>Executes one list, after the list that hands its textures over in the layouts it first needs.</summary>
    /// <param name="buffer">The list.</param>
    /// <param name="fixups">Receives the hand-over list, to be given back with the rest.</param>
    private void ExecuteOne(D3D12RhiCommandBuffer buffer, List<(nint Allocator, nint List)> fixups)
    {
        List<TextureBarrier> barriers = [];
        foreach (D3D12LayoutHandOff handOff in buffer.HandOffs)
        {
            D3D12RhiTexture texture = (D3D12RhiTexture)handOff.Texture;
            if (texture.Layout != handOff.First)
            {
                bool undefined = texture.Layout == BarrierLayout.Undefined;
                barriers.Add(new TextureBarrier
                {
                    // A call of its own: the one before it finished and flushed everything, and the one after
                    // starts once this has, so the layout change waits for nothing and nothing waits for it.
                    SyncBefore = BarrierSync.None,
                    AccessBefore = BarrierAccess.NoAccess,
                    SyncAfter = BarrierSync.None,
                    AccessAfter = BarrierAccess.NoAccess,
                    LayoutBefore = texture.Layout,
                    LayoutAfter = handOff.First,
                    PResource = (ID3D12Resource*)handOff.Resource,
                    Subresources = new BarrierSubresourceRange { IndexOrFirstMipLevel = uint.MaxValue },

                    // A render or depth target's compression metadata starts valid only through a discard.
                    Flags = undefined && texture.Usage.HasFlag(RhiTextureUsage.RenderTarget)
                        ? TextureBarrierFlags.Discard
                        : TextureBarrierFlags.None,
                });
            }

            texture.Layout = handOff.Last;
        }

        if (barriers.Count > 0)
        {
            TakeList(out ID3D12CommandAllocator* allocator, out ID3D12GraphicsCommandList7* list);
            TextureBarrier[] array = [.. barriers];
            fixed (TextureBarrier* first = array)
            {
                BarrierGroup group = new() { Type = BarrierType.Texture, NumBarriers = (uint)array.Length, PTextureBarriers = first };
                list->Barrier(1, &group);
            }

            D3D12Check.Ok(list->Close(), "ID3D12GraphicsCommandList::Close (layout hand-over)");
            ID3D12CommandList* handOver = (ID3D12CommandList*)list;
            Context.Queue->ExecuteCommandLists(1, &handOver);
            fixups.Add(((nint)allocator, (nint)list));
        }

        ID3D12CommandList* native = (ID3D12CommandList*)buffer.List;
        Context.Queue->ExecuteCommandLists(1, &native);
    }

    private D3D12RhiCommandEncoder OpenUpload()
    {
        if (_upload is null)
        {
            TakeList(out ID3D12CommandAllocator* allocator, out ID3D12GraphicsCommandList7* list);
            _upload = new D3D12RhiCommandEncoder(this, allocator, list, "uploads");
            _upload.BeginEvent("uploads");
        }

        return _upload;
    }

    private D3D12RhiCommandBuffer? TakeUpload()
    {
        if (_upload is null)
        {
            return null;
        }

        _upload.EndEvent();
        D3D12RhiCommandBuffer buffer = (D3D12RhiCommandBuffer)_upload.Finish("uploads");
        _upload = null;
        _uploadChunkUsed = 0;
        _uploadBytes = 0;
        return buffer;
    }

    private void Uploaded(ulong bytes)
    {
        _uploadBytes += bytes;

        // Sent early once enough has piled up, so a level's textures do not all sit in staging at once.
        // It lands ahead of everything after it either way.
        if (_uploadBytes >= MaxPendingUploadBytes)
        {
            Execute([]);
        }
    }

    /// <summary>Room for a write in the staging chunk being filled, or a new chunk.</summary>
    private (D3D12RhiBuffer Chunk, ulong Offset) Stage(ulong size, ulong alignment)
    {
        ulong at = (_uploadChunkUsed + alignment - 1) & ~(alignment - 1);
        if (_uploadChunks.Count > 0 && at + size <= _uploadChunks[^1].SizeInBytes)
        {
            _uploadChunkUsed = at + size;
            return (_uploadChunks[^1], at);
        }

        D3D12RhiBuffer? chunk = null;
        if (size <= StagingChunkBytes && _freeStaging.Count > 0)
        {
            chunk = _freeStaging[^1];
            _freeStaging.RemoveAt(_freeStaging.Count - 1);
        }

        chunk ??= CreateBufferCore(
            new RhiBufferDescriptor(Math.Max(size, StagingChunkBytes), RhiBufferUsage.MapWrite | RhiBufferUsage.CopySource, "staging"),
            pooled: false);

        _uploadChunks.Add(chunk);
        _uploadChunkUsed = size;
        return (chunk, 0);
    }

    private void ReleaseFinishedStaging()
    {
        ulong completed = Context.Completed;
        for (int i = _usedStaging.Count - 1; i >= 0; i--)
        {
            (ulong ticket, D3D12RhiBuffer chunk) = _usedStaging[i];
            if (ticket > completed)
            {
                continue;
            }

            _usedStaging.RemoveAt(i);
            if (chunk.SizeInBytes == StagingChunkBytes && _freeStaging.Count < MaxFreeStagingChunks)
            {
                _freeStaging.Add(chunk);
            }
            else
            {
                chunk.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public void TrimMemory()
    {
        foreach (D3D12RhiBuffer chunk in _freeStaging)
        {
            chunk.Dispose();
        }

        _freeStaging.Clear();
    }

    private void TakeList(out ID3D12CommandAllocator* allocator, out ID3D12GraphicsCommandList7* list)
    {
        allocator = null;
        ulong completed = Context.Completed;
        for (int i = 0; i < _allocators.Count; i++)
        {
            if (_allocators[i].Ticket <= completed)
            {
                allocator = (ID3D12CommandAllocator*)_allocators[i].Allocator;
                _allocators.RemoveAt(i);
                D3D12Check.Ok(allocator->Reset(), "ID3D12CommandAllocator::Reset");
                break;
            }
        }

        if (allocator is null)
        {
            ID3D12CommandAllocator* created = null;
            Guid iid = ID3D12CommandAllocator.Guid;
            D3D12Check.Ok(Native->CreateCommandAllocator(CommandListType.Direct, &iid, (void**)&created), "CreateCommandAllocator");
            allocator = created;
        }

        if (_lists.Count > 0)
        {
            list = (ID3D12GraphicsCommandList7*)_lists.Pop();
        }
        else
        {
            // Created closed, so a new list and a pooled one are both reset the same way below.
            ID3D12GraphicsCommandList7* created = null;
            Guid iid = ID3D12GraphicsCommandList7.Guid;
            D3D12Check.Ok(
                Native->CreateCommandList1(0, CommandListType.Direct, CommandListFlags.None, &iid, (void**)&created),
                "CreateCommandList1");
            list = created;
        }

        D3D12Check.Ok(list->Reset(allocator, null), "ID3D12GraphicsCommandList::Reset");
    }

    private void CreateUploads() => WriteNullView();

    private void DisposeUploads()
    {
        D3D12RhiCommandBuffer? unsent = TakeUpload();
        if (unsent is not null)
        {
            ReturnList(unsent.Allocator, unsent.List, 0);
        }

        foreach ((_, D3D12RhiBuffer chunk) in _usedStaging)
        {
            chunk.Dispose();
        }

        foreach (D3D12RhiBuffer chunk in _uploadChunks.Concat(_freeStaging))
        {
            chunk.Dispose();
        }

        _usedStaging.Clear();
        _uploadChunks.Clear();
        _freeStaging.Clear();
    }

    private void DisposeLists()
    {
        while (_lists.Count > 0)
        {
            ((IUnknown*)_lists.Pop())->Release();
        }

        foreach ((_, nint allocator) in _allocators)
        {
            ((IUnknown*)allocator)->Release();
        }

        _allocators.Clear();
    }
}
