using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// Buffer writes queued ahead of the next submission, and what every submission holds until the GPU is done.
/// </summary>
/// <remarks>
/// <para>
/// <b>Writes.</b> The interface's <c>WriteBuffer</c> is WebGPU's <c>queue.writeBuffer</c>: it lands before
/// any work submitted after it, and nobody waits for it. It used to be a submission of its own, waited on
/// -- hundreds a frame, each a round trip to the GPU, which left the renderer spending most of its frame
/// asleep. Now the bytes go into a staging chunk and the copy into an upload command buffer, which goes
/// to the queue first in the next submission, ending in a barrier that makes it visible to everything
/// after it.
/// </para>
/// <para>
/// <b>Lifetimes.</b> A command buffer and a staging chunk are freed only once the submission that carried
/// them has finished: inside a frame that is the frame's fence, waited on when its slot comes round;
/// outside one, a fence of the submission's own. Before this, every encoder's command buffer and every
/// frame's present transition were allocated and never freed at all.
/// </para>
/// </remarks>
internal sealed unsafe partial class VulkanRhiDevice
{
    /// <summary>The size of a pooled staging chunk; a larger write gets one of its own size.</summary>
    private const ulong StagingChunkBytes = 4UL * 1024 * 1024;

    /// <summary>How much may wait outside a frame before it is sent on its own, so a load does not pile up.</summary>
    private const ulong MaxPendingUploadBytes = 64UL * 1024 * 1024;

    private readonly List<VulkanBuffer> _freeStaging = [];

    /// <summary>How many pooled chunks are kept for reuse; the rest are freed as they come back.</summary>
    private const int MaxFreeStagingChunks = 8;

    // The upload being recorded, and the chunks it copies out of.
    private CommandBuffer _upload;
    private readonly List<VulkanBuffer> _uploadChunks = [];
    private ulong _uploadChunkUsed;
    private ulong _uploadBytes;

    // The buffers the upload being recorded has already copied into.
    private readonly HashSet<ulong> _uploadTargets = [];

    // Every buffer and image the upload being recorded writes, and what was disposed while it did. A
    // resource destroyed under a recorded, unsubmitted copy invalidates the command buffer -- the upload
    // then failed validation at its barrier and its end, and on a real driver could fault -- so its
    // destruction rides with the upload and happens once that submission has finished.
    private readonly HashSet<ulong> _uploadBuffers = [];
    private readonly HashSet<ulong> _uploadImages = [];
    private readonly List<IDisposable> _uploadDeferred = [];

    // What a frame's submission holds besides its command buffers and staging: destructions deferred from
    // its uploads, run when its slot comes round. Parallel to _slotStaging.
    private readonly List<IDisposable> _pendingDeferred = [];
    private List<IDisposable>[] _slotDeferred = [];

    // What the frame being recorded will submit, beyond the command buffers in _pending.
    private readonly List<VulkanBuffer> _pendingStaging = [];

    // What each frame slot's last submission holds, released once its fence has been waited on.
    private List<CommandBuffer>[] _slotCommands = [];
    private List<VulkanBuffer>[] _slotStaging = [];

    // Submissions made outside a frame, each with a fence of its own.
    private readonly List<LooseSubmission> _loose = [];

    // A counter every submission advances as it finishes, so a readback can wait for exactly the
    // submission that wrote what it reads rather than for the whole device. Null where the device has no
    // timeline semaphores, and then a readback idles the device as it always did.
    private Semaphore _timeline;
    private ulong _signalled;

    /// <summary>Gets whether submissions are counted, and a readback can wait for its own.</summary>
    internal bool HasTimeline => _timeline.Handle != 0;

    /// <summary>Waits for a submission to finish, for as long as it is given.</summary>
    /// <param name="ticket">The count it reaches: a buffer's <see cref="VulkanRhiBuffer.WriteTicket"/>.</param>
    /// <param name="timeout">How long to wait; zero only looks.</param>
    /// <returns><see langword="true"/> once it has finished; false while it has not, or has not been sent.</returns>
    internal bool WaitForTicket(ulong ticket, TimeSpan timeout)
    {
        if (ticket == 0)
        {
            return true;
        }

        if (ticket > _signalled || !HasTimeline)
        {
            return false;
        }

        Semaphore timeline = _timeline;
        SemaphoreWaitInfo info = new()
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &timeline,
            PValues = &ticket,
        };

        ulong nanoseconds = timeout == Timeout.InfiniteTimeSpan
            ? ulong.MaxValue
            : (ulong)Math.Max(0, timeout.Ticks) * 100UL;

        Result result = _vk.WaitSemaphores(_device.Device, &info, nanoseconds);
        if (result == Result.ErrorDeviceLost)
        {
            Checked(result, "vkWaitSemaphores");
        }

        return result == Result.Success;
    }

    /// <summary>Checks what a submit or a wait returned, having a lost device describe itself first.</summary>
    /// <param name="result">What the call returned.</param>
    /// <param name="call">The call, for the message.</param>
    /// <remarks>
    /// The driver's account of a lost device -- the fault, the last checkpoints the queue passed, the
    /// memory -- can only be read before the exception unwinds past the call that found it, so every
    /// submit and every wait comes through here.
    /// </remarks>
    private void Checked(Result result, string call)
    {
        if (result == Result.ErrorDeviceLost)
        {
            _device.ReportDeviceFault();
        }

        VkCheck.Ok(result, call);
    }

    /// <summary>The value the next submission signals, advancing the count.</summary>
    private ulong NextSignal() => ++_signalled;

    /// <summary>Queues a write to a buffer the host cannot map, ahead of the next submission.</summary>
    private void QueueUpload(Buffer destination, ulong offset, ReadOnlySpan<byte> bytes)
    {
        ulong size = (ulong)bytes.Length;
        if (size == 0)
        {
            return;
        }

        VulkanBuffer chunk = StagingFor(size);
        chunk.WriteAt(_uploadChunkUsed, bytes);
        OpenUpload();
        _uploadBuffers.Add(destination.Handle);

        // Copies in one command buffer are not ordered by where they were recorded, and a buffer written
        // twice before a submission -- the distance field's cell lists, rebuilt for two levels in one
        // frame -- would end holding whichever copy the GPU happened to finish last, or a mixture of both.
        // The interface promises the later write, so a copy into a buffer this upload has already written
        // waits for the ones before it. Synchronisation validation reports it as a write-after-write
        // hazard, and on Metal, which runs blits concurrently, it is not only a theoretical one.
        if (!_uploadTargets.Add(destination.Handle))
        {
            MemoryBarrier ordered = new()
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.TransferWriteBit,
            };

            _vk.CmdPipelineBarrier(
                _upload,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.TransferBit,
                0,
                1,
                &ordered,
                0,
                null,
                0,
                null);

            _uploadTargets.Clear();
            _uploadTargets.Add(destination.Handle);
        }

        BufferCopy region = new() { SrcOffset = _uploadChunkUsed, DstOffset = offset, Size = size };
        _vk.CmdCopyBuffer(_upload, chunk.Handle, destination, 1, &region);

        // Aligned for the next copy's source offset, which Vulkan wants a multiple of four.
        _uploadChunkUsed = (_uploadChunkUsed + size + 15UL) & ~15UL;
        _uploadBytes += size;

        // Inside a frame as well as outside one, as texture writes are: a level's meshes written while a
        // frame records would otherwise all sit in staging until that frame is submitted.
        if (_uploadBytes >= MaxPendingUploadBytes)
        {
            SubmitPendingUploads();
        }
    }

    /// <summary>Queues a write to one mip of a texture, ahead of the next submission.</summary>
    /// <param name="texture">The texture.</param>
    /// <param name="bytes">The texels, rows <paramref name="bytesPerRow"/> apart.</param>
    /// <param name="copy">The copy's destination; its buffer offset and row length are filled in here.</param>
    /// <param name="bytesPerRow">The distance between rows in <paramref name="bytes"/>.</param>
    /// <remarks>
    /// The texture's counterpart of <see cref="QueueUpload"/>, and the same contract as WebGPU's
    /// <c>queue.writeTexture</c>: it lands before any work submitted after it, and nobody waits for it. It
    /// was a submission of its own, waited on, per MIP -- with the transition every new texture made, a
    /// level's few hundred textures cost a couple of thousand round trips to the GPU in the one frame that
    /// adopted them, and that frame was the menu's backdrop-swap stall.
    /// </remarks>
    private void QueueTextureUpload(VulkanRhiTexture texture, ReadOnlySpan<byte> bytes, BufferImageCopy copy, int bytesPerRow)
    {
        ulong size = (ulong)bytes.Length;
        if (size == 0)
        {
            return;
        }

        VulkanBuffer chunk = StagingFor(size);
        chunk.WriteAt(_uploadChunkUsed, bytes);
        OpenUpload();
        _uploadImages.Add(texture.Image.Handle);

        int texelBytes = texture.Format.BytesPerTexel();
        copy.BufferOffset = _uploadChunkUsed;

        // Vulkan wants the row length in TEXELS where everything else counts bytes, and a row that is not
        // padded is stated as zero rather than as its own width.
        copy.BufferRowLength = texelBytes > 0 && bytesPerRow != copy.ImageExtent.Width * texelBytes
            ? (uint)(bytesPerRow / texelBytes)
            : 0;
        copy.BufferImageHeight = 0;

        // The transitions are full barriers, so they also order this copy after any earlier one into the
        // same texture -- a mip chain is several writes into one image.
        Transition(_upload, texture, ImageLayout.TransferDstOptimal);
        _vk.CmdCopyBufferToImage(_upload, chunk.Handle, texture.Image, ImageLayout.TransferDstOptimal, 1, &copy);
        Transition(_upload, texture, ImageLayout.General);

        // Sixteen-aligned, which covers every texel size a copy's source offset has to be a multiple of.
        _uploadChunkUsed = (_uploadChunkUsed + size + 15UL) & ~15UL;
        _uploadBytes += size;

        // Sent on its own once enough has piled up, inside a frame as well: a level adopted mid-frame is a
        // couple of hundred megabytes of texels, and held for the frame's submission all of it would sit in
        // staging at once. Sending early changes nothing a reader can see -- it still lands ahead of the
        // frame, and of any later write.
        if (_uploadBytes >= MaxPendingUploadBytes)
        {
            SubmitPendingUploads();
        }
    }

    /// <summary>Queues a texture's move into its resting layout, ahead of the next submission.</summary>
    /// <param name="texture">A texture just created, still undefined.</param>
    private void QueueInitialTransition(VulkanRhiTexture texture)
    {
        OpenUpload();
        _uploadImages.Add(texture.Image.Handle);
        Transition(_upload, texture, ImageLayout.General);
    }

    /// <summary>Holds a resource's destruction while the upload being recorded still writes it.</summary>
    /// <param name="handle">The buffer's or image's handle.</param>
    /// <param name="isImage">Whether it is an image.</param>
    /// <param name="destroy">What destroys it.</param>
    /// <returns><see langword="true"/> when it was held, and will be destroyed once the upload has run.</returns>
    internal bool DeferWhileQueued(ulong handle, bool isImage, IDisposable destroy)
    {
        if (_disposed || _upload.Handle == 0 || !(isImage ? _uploadImages : _uploadBuffers).Contains(handle))
        {
            return false;
        }

        _uploadDeferred.Add(destroy);
        return true;
    }

    /// <summary>Begins the upload command buffer if nothing has been written into it yet.</summary>
    private void OpenUpload()
    {
        if (_upload.Handle != 0)
        {
            return;
        }

        _upload = _commands.AllocatePrimary(1)[0];
        CommandBufferBeginInfo begin = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        VkCheck.Ok(_vk.BeginCommandBuffer(_upload, &begin), "vkBeginCommandBuffer");
    }

    /// <summary>
    /// Sends the queued writes now, inside a frame or not, for a submission that goes around the frame --
    /// a one-off that may read a texture written since the last one.
    /// </summary>
    private void SubmitPendingUploads()
    {
        if (_upload.Handle != 0)
        {
            // Uncounted inside a frame. A buffer an encoder writes this frame was given the value the
            // frame's own submission will signal, so a loose submission signalling it first would tell a
            // readback the write was done while the frame carrying it had not even been sent.
            SubmitLoose([], counted: !_inFrame);
        }
    }

    /// <summary>The chunk the next write goes into, with room for it.</summary>
    private VulkanBuffer StagingFor(ulong size)
    {
        if (_uploadChunks.Count > 0 && _uploadChunkUsed + size <= _uploadChunks[^1].Size)
        {
            return _uploadChunks[^1];
        }

        VulkanBuffer? chunk = null;
        if (size <= StagingChunkBytes)
        {
            int free = _freeStaging.FindIndex(buffer => buffer.Size >= size);
            if (free >= 0)
            {
                chunk = _freeStaging[free];
                _freeStaging.RemoveAt(free);
            }
        }

        chunk ??= new VulkanBuffer(
            _vk,
            _device,
            _allocator,
            Math.Max(size, StagingChunkBytes),
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        _uploadChunks.Add(chunk);
        _uploadChunkUsed = 0;
        return chunk;
    }

    /// <summary>Ends the upload being recorded and hands it over, with the chunks it reads.</summary>
    /// <param name="chunks">Receives the staging the upload reads.</param>
    /// <param name="deferred">Receives the destructions put off until the upload has run.</param>
    /// <returns>The upload, or the null handle when nothing was written.</returns>
    private CommandBuffer TakeUpload(List<VulkanBuffer> chunks, List<IDisposable> deferred)
    {
        // The batched one-offs go to the queue first -- the traced world's texture copies -- so whatever
        // this submission carries is ordered after them, as it was when each of them waited.
        _commands.FlushBatched();

        if (_upload.Handle == 0)
        {
            return default;
        }

        // Everything after this in the queue sees what it wrote.
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };

        _vk.CmdPipelineBarrier(
            _upload,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.AllCommandsBit,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null);

        VkCheck.Ok(_vk.EndCommandBuffer(_upload), "vkEndCommandBuffer");

        CommandBuffer upload = _upload;
        _upload = default;
        chunks.AddRange(_uploadChunks);
        _uploadChunks.Clear();
        _uploadChunkUsed = 0;
        _uploadBytes = 0;
        _uploadTargets.Clear();
        _uploadBuffers.Clear();
        _uploadImages.Clear();
        deferred.AddRange(_uploadDeferred);
        _uploadDeferred.Clear();
        return upload;
    }

    /// <summary>Sends whatever has been written and not yet submitted, outside a frame.</summary>
    /// <remarks>Inside one, the frame's own submission carries it.</remarks>
    private void FlushUploads()
    {
        if (_inFrame || _upload.Handle == 0)
        {
            return;
        }

        SubmitLoose([]);
    }

    /// <summary>Submits outside a frame, uploads first, holding everything until a fence of its own signals.</summary>
    /// <param name="buffers">The command buffers to send after the uploads.</param>
    /// <param name="counted">Whether it advances the timeline, which every submission does but an early upload inside a frame.</param>
    private void SubmitLoose(CommandBuffer[] buffers, bool counted = true)
    {
        List<VulkanBuffer> staging = [];
        List<IDisposable> deferred = [];
        CommandBuffer upload = TakeUpload(staging, deferred);

        List<CommandBuffer> all = new(buffers.Length + 1);
        if (upload.Handle != 0)
        {
            all.Add(upload);
        }

        all.AddRange(buffers);
        if (all.Count == 0)
        {
            // Nothing to submit, so nothing deferred can still be in use.
            foreach (IDisposable held in deferred)
            {
                held.Dispose();
            }

            return;
        }

        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        Fence fence;
        VkCheck.Ok(_vk.CreateFence(_device.Device, &fenceInfo, null, &fence), "vkCreateFence");

        CommandBuffer[] handles = [.. all];
        Semaphore timeline = _timeline;
        bool signals = counted && HasTimeline;
        ulong value = signals ? NextSignal() : 0;
        TimelineSemaphoreSubmitInfo countedInfo = new()
        {
            SType = StructureType.TimelineSemaphoreSubmitInfo,
            SignalSemaphoreValueCount = 1,
            PSignalSemaphoreValues = &value,
        };

        fixed (CommandBuffer* first = handles)
        {
            SubmitInfo submit = new()
            {
                SType = StructureType.SubmitInfo,
                PNext = signals ? &countedInfo : null,
                CommandBufferCount = (uint)handles.Length,
                PCommandBuffers = first,
                SignalSemaphoreCount = signals ? 1u : 0u,
                PSignalSemaphores = &timeline,
            };

            Checked(_vk.QueueSubmit(_device.Graphics, 1, &submit, fence), "vkQueueSubmit");
        }

        _loose.Add(new LooseSubmission(fence, all, staging, deferred));
    }

    /// <summary>Moves what the frame being submitted holds into its slot, to be released when the slot comes round.</summary>
    private void HoldForSlot(int slot, IEnumerable<CommandBuffer> buffers)
    {
        _slotCommands[slot].AddRange(buffers);
        _slotStaging[slot].AddRange(_pendingStaging);
        _pendingStaging.Clear();
        _slotDeferred[slot].AddRange(_pendingDeferred);
        _pendingDeferred.Clear();
    }

    /// <summary>Releases what a slot's last frame held, once its fence has been waited on.</summary>
    private void ReleaseSlot(int slot)
    {
        _commands.Free(_slotCommands[slot]);
        _slotCommands[slot].Clear();

        Recycle(_slotStaging[slot]);
        _slotStaging[slot].Clear();

        foreach (IDisposable held in _slotDeferred[slot])
        {
            held.Dispose();
        }

        _slotDeferred[slot].Clear();

        ReleaseFinishedLoose();
        ReleaseFinishedSets();
    }

    /// <summary>Takes back staging a finished submission read, keeping only what the next one can reuse.</summary>
    /// <remarks>
    /// Every chunk used to be kept, forever. One larger than the pooled size is never handed out again --
    /// <see cref="StagingFor"/> only reuses pooled-size chunks -- and once textures went through the queue a
    /// level's worth of large pictures came back every menu backdrop swap: a soak of the menu grew by
    /// gigabytes of host memory. Those, and anything past a small pool, are freed instead.
    /// </remarks>
    private void Recycle(List<VulkanBuffer> chunks)
    {
        foreach (VulkanBuffer chunk in chunks)
        {
            if (chunk.Size == StagingChunkBytes && _freeStaging.Count < MaxFreeStagingChunks)
            {
                _freeStaging.Add(chunk);
            }
            else
            {
                chunk.Dispose();
            }
        }
    }

    /// <summary>Releases every submission made outside a frame that has finished.</summary>
    private void ReleaseFinishedLoose(bool wait = false)
    {
        for (int i = _loose.Count - 1; i >= 0; i--)
        {
            LooseSubmission loose = _loose[i];
            Fence fence = loose.Fence;

            if (wait)
            {
                Checked(_vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
            }
            else if (_vk.GetFenceStatus(_device.Device, fence) != Result.Success)
            {
                continue;
            }

            _vk.DestroyFence(_device.Device, fence, null);
            _commands.Free(loose.Commands);
            Recycle(loose.Staging);
            foreach (IDisposable held in loose.Deferred)
            {
                held.Dispose();
            }

            _loose.RemoveAt(i);
        }
    }

    /// <summary>Makes the timeline every submission advances, where the device has them.</summary>
    private void CreateTimeline()
    {
        if (_timeline.Handle == 0 && _device.Capabilities.TimelineSemaphore)
        {
            SemaphoreTypeCreateInfo type = new()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = 0,
            };

            SemaphoreCreateInfo create = new() { SType = StructureType.SemaphoreCreateInfo, PNext = &type };
            Semaphore timeline;
            VkCheck.Ok(_vk.CreateSemaphore(_device.Device, &create, null, &timeline), "vkCreateSemaphore");
            _timeline = timeline;
        }
    }

    private void CreateSubmissionState(int frames)
    {

        _slotCommands = new List<CommandBuffer>[frames];
        _slotStaging = new List<VulkanBuffer>[frames];
        _slotDeferred = new List<IDisposable>[frames];
        for (int i = 0; i < frames; i++)
        {
            _slotCommands[i] = [];
            _slotStaging[i] = [];
            _slotDeferred[i] = [];
        }
    }

    /// <summary>Frees everything held, with the device idle.</summary>
    private void DestroySubmissionState()
    {
        ReleaseFinishedLoose(wait: true);

        for (int i = 0; i < _slotCommands.Length; i++)
        {
            ReleaseSlot(i);
        }

        List<VulkanBuffer> unsent = [];
        List<IDisposable> unsentDeferred = [];
        CommandBuffer upload = TakeUpload(unsent, unsentDeferred);
        if (upload.Handle != 0)
        {
            _commands.Free([upload]);
        }

        foreach (VulkanBuffer chunk in unsent.Concat(_pendingStaging).Concat(_freeStaging))
        {
            chunk.Dispose();
        }

        // The device is idle: whatever was put off can go now.
        foreach (IDisposable held in unsentDeferred.Concat(_pendingDeferred))
        {
            held.Dispose();
        }

        _pendingDeferred.Clear();

        _pendingStaging.Clear();
        _freeStaging.Clear();

        if (_timeline.Handle != 0)
        {
            _vk.DestroySemaphore(_device.Device, _timeline, null);
            _timeline = default;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The timeline value the last submission signals, which every submission advances -- a frame's, a loose
    /// one's, a flush's. A device without timeline semaphores has nothing to count with, and its markers are
    /// complete as soon as they are taken, which leaves a caller pacing by them no worse off than it was
    /// before they existed; Vulkan 1.2 made the feature a requirement, so that is an old driver's case.
    /// </remarks>
    public IRhiWorkMarker MarkSubmittedWork()
    {
        ThrowIfDisposed();
        return new VulkanRhiWorkMarker(this, HasTimeline ? _signalled : 0);
    }

    private sealed record LooseSubmission(
        Fence Fence, List<CommandBuffer> Commands, List<VulkanBuffer> Staging, List<IDisposable> Deferred);
}

/// <summary>A timeline value, complete once the semaphore has reached it.</summary>
internal sealed class VulkanRhiWorkMarker : IRhiWorkMarker
{
    private readonly VulkanRhiDevice _device;
    private readonly ulong _ticket;
    private bool _complete;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiWorkMarker"/> class.</summary>
    /// <param name="device">The device whose timeline this reads.</param>
    /// <param name="ticket">The value to wait for; zero is already reached.</param>
    public VulkanRhiWorkMarker(VulkanRhiDevice device, ulong ticket)
    {
        _device = device;
        _ticket = ticket;
        _complete = ticket == 0;
    }

    /// <inheritdoc/>
    public bool IsComplete => _complete || (_complete = _device.CompletedValue >= _ticket);
}
