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

    // The upload being recorded, and the chunks it copies out of.
    private CommandBuffer _upload;
    private readonly List<VulkanBuffer> _uploadChunks = [];
    private ulong _uploadChunkUsed;
    private ulong _uploadBytes;

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
        return result == Result.Success;
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

        if (_upload.Handle == 0)
        {
            _upload = _commands.AllocatePrimary(1)[0];
            CommandBufferBeginInfo begin = new()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };

            VkCheck.Ok(_vk.BeginCommandBuffer(_upload, &begin), "vkBeginCommandBuffer");
        }

        BufferCopy region = new() { SrcOffset = _uploadChunkUsed, DstOffset = offset, Size = size };
        _vk.CmdCopyBuffer(_upload, chunk.Handle, destination, 1, &region);

        // Aligned for the next copy's source offset, which Vulkan wants a multiple of four.
        _uploadChunkUsed = (_uploadChunkUsed + size + 15UL) & ~15UL;
        _uploadBytes += size;

        if (!_inFrame && _uploadBytes >= MaxPendingUploadBytes)
        {
            FlushUploads();
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
    /// <returns>The upload, or the null handle when nothing was written.</returns>
    private CommandBuffer TakeUpload(List<VulkanBuffer> chunks)
    {
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
    private void SubmitLoose(CommandBuffer[] buffers)
    {
        List<VulkanBuffer> staging = [];
        CommandBuffer upload = TakeUpload(staging);

        List<CommandBuffer> all = new(buffers.Length + 1);
        if (upload.Handle != 0)
        {
            all.Add(upload);
        }

        all.AddRange(buffers);
        if (all.Count == 0)
        {
            return;
        }

        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        Fence fence;
        VkCheck.Ok(_vk.CreateFence(_device.Device, &fenceInfo, null, &fence), "vkCreateFence");

        CommandBuffer[] handles = [.. all];
        Semaphore timeline = _timeline;
        ulong value = HasTimeline ? NextSignal() : 0;
        TimelineSemaphoreSubmitInfo counted = new()
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
                PNext = HasTimeline ? &counted : null,
                CommandBufferCount = (uint)handles.Length,
                PCommandBuffers = first,
                SignalSemaphoreCount = HasTimeline ? 1u : 0u,
                PSignalSemaphores = &timeline,
            };

            VkCheck.Ok(_vk.QueueSubmit(_device.Graphics, 1, &submit, fence), "vkQueueSubmit");
        }

        _loose.Add(new LooseSubmission(fence, all, staging));
    }

    /// <summary>Moves what the frame being submitted holds into its slot, to be released when the slot comes round.</summary>
    private void HoldForSlot(int slot, IEnumerable<CommandBuffer> buffers)
    {
        _slotCommands[slot].AddRange(buffers);
        _slotStaging[slot].AddRange(_pendingStaging);
        _pendingStaging.Clear();
    }

    /// <summary>Releases what a slot's last frame held, once its fence has been waited on.</summary>
    private void ReleaseSlot(int slot)
    {
        _commands.Free(_slotCommands[slot]);
        _slotCommands[slot].Clear();

        _freeStaging.AddRange(_slotStaging[slot]);
        _slotStaging[slot].Clear();

        ReleaseFinishedLoose();
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
                VkCheck.Ok(_vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
            }
            else if (_vk.GetFenceStatus(_device.Device, fence) != Result.Success)
            {
                continue;
            }

            _vk.DestroyFence(_device.Device, fence, null);
            _commands.Free(loose.Commands);
            _freeStaging.AddRange(loose.Staging);
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
        for (int i = 0; i < frames; i++)
        {
            _slotCommands[i] = [];
            _slotStaging[i] = [];
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
        CommandBuffer upload = TakeUpload(unsent);
        if (upload.Handle != 0)
        {
            _commands.Free([upload]);
        }

        foreach (VulkanBuffer chunk in unsent.Concat(_pendingStaging).Concat(_freeStaging))
        {
            chunk.Dispose();
        }

        _pendingStaging.Clear();
        _freeStaging.Clear();

        if (_timeline.Handle != 0)
        {
            _vk.DestroySemaphore(_device.Device, _timeline, null);
            _timeline = default;
        }
    }

    private sealed record LooseSubmission(Fence Fence, List<CommandBuffer> Commands, List<VulkanBuffer> Staging);
}
