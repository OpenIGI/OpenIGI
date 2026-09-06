using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The graphics command pool, and the one-off submissions that stand outside the frame loop.
/// </summary>
/// <remarks>
/// <para>
/// Every command the backend issues is recorded into a command buffer allocated from a pool tied to the
/// graphics family. The per-frame buffers the frame loop owns come from here; so do the short-lived
/// buffers that upload a texture or transition an image, which do not belong to any frame and are
/// submitted on their own through <see cref="SingleTime"/>.
/// </para>
/// <para>
/// <see cref="SingleTime"/> submits and then waits: it is for load-time work — staging a mesh, building
/// a mip chain, moving an image to its first layout — where the simplicity of a synchronous copy is
/// worth more than the pipelining a fence-and-poll would keep. Nothing on the frame path uses it.
/// </para>
/// <para>
/// <see cref="SingleTimeBatched"/> is the same work without the per-item wait. A submit-and-wait costs
/// a queue round trip whatever it carries, and a level's worth of textures is hundreds of them: measured
/// over a menu session, six hundred milliseconds of the render thread went on <c>vkWaitForFences</c>
/// waiting for copies that together took a fraction of that. Batched, they accumulate into one buffer
/// and pay one round trip between them. The guarantee is unchanged — the batch is flushed, and waited
/// on, before anything else is submitted or a frame opens — so a caller still cannot observe an upload
/// that has not happened.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanCommands : IDisposable
{
    // How much a batch accumulates before it goes on its own. The count keeps a pathological run of tiny
    // uploads from holding a command buffer open all load; the byte cap keeps the staging memory they
    // hold alive bounded, which is the real resource at stake -- a level's textures are megabytes each.
    private const int MaxBatchedItems = 192;
    private const ulong MaxBatchedBytes = 96UL * 1024 * 1024;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly CommandPool _pool;
    private readonly CommandPool _singleTimePool;

    // The open batch, the staging it holds alive until the batch has run, and how much of each it has.
    private readonly List<IDisposable> _batchHeld = [];
    private CommandBuffer _batch;
    private int _batchItems;
    private ulong _batchBytes;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanCommands"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool is created on.</param>
    /// <exception cref="GraphicsDeviceException">The command pool could not be created.</exception>
    public VulkanCommands(Vk vk, VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device;

        CommandPoolCreateInfo createInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = device.Selection.Queues.Graphics.Family,

            // The frame buffers are re-recorded every frame, so the pool must let a buffer be reset on
            // its own rather than only by resetting the whole pool.
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };

        CommandPool pool;
        VkCheck.Ok(_vk.CreateCommandPool(device.Device, &createInfo, null, &pool), "vkCreateCommandPool");
        _pool = pool;

        // A separate pool for one-off uploads. A storage mesh uploads mid-frame, while a frame command
        // buffer from the pool above is still being recorded; sharing a pool would mean allocating and
        // freeing from it while another of its buffers is recording, which is a host-synchronisation
        // hazard. Its own pool keeps the two apart.
        CommandPoolCreateInfo singleTimeInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = device.Selection.Queues.Graphics.Family,
            Flags = CommandPoolCreateFlags.TransientBit,
        };
        CommandPool singleTimePool;
        VkCheck.Ok(_vk.CreateCommandPool(device.Device, &singleTimeInfo, null, &singleTimePool), "vkCreateCommandPool");
        _singleTimePool = singleTimePool;
    }

    /// <summary>Allocates primary command buffers from the pool.</summary>
    /// <param name="count">How many to allocate.</param>
    /// <returns>The allocated buffers.</returns>
    /// <exception cref="GraphicsDeviceException">The buffers could not be allocated.</exception>
    public CommandBuffer[] AllocatePrimary(int count)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)count,
        };

        CommandBuffer[] buffers = new CommandBuffer[count];
        fixed (CommandBuffer* first = buffers)
        {
            VkCheck.Ok(_vk.AllocateCommandBuffers(_device.Device, &allocInfo, first), "vkAllocateCommandBuffers");
        }

        return buffers;
    }

    /// <summary>Records and submits a one-off command buffer, then waits for it to finish.</summary>
    /// <param name="record">Records the commands into the buffer.</param>
    /// <exception cref="GraphicsDeviceException">The submission failed.</exception>
    public void SingleTime(Action<CommandBuffer> record)
    {
        ArgumentNullException.ThrowIfNull(record);

        CommandBuffer commandBuffer = BeginOneOff();
        record(commandBuffer);
        SubmitOneOff(commandBuffer);
    }

    /// <summary>Records one-off work into the shared batch instead of submitting it on its own.</summary>
    /// <param name="record">Records the commands into the batch's buffer.</param>
    /// <param name="keepAlive">
    /// A staging resource the recorded commands read, disposed once the batch has actually run. Passing
    /// it here rather than disposing at the call site is the whole difference: the copy no longer has to
    /// have completed by the time the caller returns.
    /// </param>
    /// <param name="stagingBytes">How much host-visible memory <paramref name="keepAlive"/> holds.</param>
    /// <remarks>
    /// For uploads and layout moves whose result nothing reads until the next submission — which is every
    /// texture, mesh and buffer the loaders create. Work whose result the HOST reads back, or which must
    /// have finished before the call returns for any other reason, belongs in <see cref="SingleTime"/>.
    /// </remarks>
    public void SingleTimeBatched(Action<CommandBuffer> record, IDisposable? keepAlive = null, ulong stagingBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_batch.Handle == 0)
        {
            _batch = BeginOneOffCore();
        }

        record(_batch);
        _batchItems++;
        _batchBytes += stagingBytes;

        if (keepAlive is not null)
        {
            _batchHeld.Add(keepAlive);
        }

        if (_batchItems >= MaxBatchedItems || _batchBytes >= MaxBatchedBytes)
        {
            FlushBatched();
        }
    }

    /// <summary>Submits whatever the batch holds and waits for it, freeing the staging it kept alive.</summary>
    /// <remarks>
    /// Called before anything else reaches the queue -- another one-off, a frame opening -- so batched
    /// work is always ordered and complete ahead of whatever might read it. Cheap and safe to call with
    /// an empty batch, which is what it is on nearly every frame.
    /// </remarks>
    public void FlushBatched()
    {
        if (_batch.Handle == 0)
        {
            return;
        }

        CommandBuffer buffer = _batch;
        _batch = default;
        _batchItems = 0;
        _batchBytes = 0;

        try
        {
            SubmitOneOffCore(buffer);
        }
        finally
        {
            foreach (IDisposable held in _batchHeld)
            {
                held.Dispose();
            }

            _batchHeld.Clear();
        }
    }

    /// <summary>Opens a one-off command buffer whose recording spans more than one call.</summary>
    /// <returns>The command buffer, already begun.</returns>
    /// <remarks>
    /// <see cref="SingleTime"/> covers the usual case, where everything to be recorded is known at the
    /// point of asking. Card capture is not that case: the caller opens the capture, draws into it over
    /// many calls it decides one at a time, and closes it. This is the same buffer, held open.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">The buffer could not be allocated or begun.</exception>
    public CommandBuffer BeginOneOff()
    {
        // Ahead of anything that is not the batch, so the queue sees the uploads first and the buffer
        // about to be recorded can read what they wrote.
        FlushBatched();
        return BeginOneOffCore();
    }

    private CommandBuffer BeginOneOffCore()
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _singleTimePool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        CommandBuffer commandBuffer;
        VkCheck.Ok(_vk.AllocateCommandBuffers(_device.Device, &allocInfo, &commandBuffer), "vkAllocateCommandBuffers");

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkCheck.Ok(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        _device.Checkpoint(commandBuffer, VulkanCheckpointMarkers.OneOffBatch);
        return commandBuffer;
    }

    /// <summary>Closes, submits and waits on a buffer from <see cref="BeginOneOff"/>.</summary>
    /// <param name="commandBuffer">The buffer to finish.</param>
    /// <exception cref="GraphicsDeviceException">The submission failed.</exception>
    public void SubmitOneOff(CommandBuffer commandBuffer)
    {
        FlushBatched();
        SubmitOneOffCore(commandBuffer);
    }

    private void SubmitOneOffCore(CommandBuffer commandBuffer)
    {
        VkCheck.Ok(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };

        // A short-lived fence rather than a queue-wait-idle, so the wait is scoped to this submission
        // and not to everything the queue happens to be doing.
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        Fence fence;
        VkCheck.Ok(_vk.CreateFence(_device.Device, &fenceInfo, null, &fence), "vkCreateFence");

        try
        {
            // The fault is asked for here as well as on the frame queue's submit. VK_EXT_device_fault can
            // only be read from a device that has just been lost, and only before the exception unwinds
            // past it -- so a path that throws without asking throws away the one description of what the
            // GPU was doing. This path carries the uploads and the card captures, which is exactly the
            // work a device loss during a level's build lands in, and it reported nothing at all.
            Result submitResult = _vk.QueueSubmit(_device.Graphics, 1, &submitInfo, fence);
            if (submitResult == Result.ErrorDeviceLost)
            {
                _device.ReportDeviceFault();
            }

            VkCheck.Ok(submitResult, "vkQueueSubmit");

            Result waitResult = _vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue);
            if (waitResult == Result.ErrorDeviceLost)
            {
                _device.ReportDeviceFault();
            }

            VkCheck.Ok(waitResult, "vkWaitForFences");
        }
        finally
        {
            _vk.DestroyFence(_device.Device, fence, null);
            _vk.FreeCommandBuffers(_device.Device, _singleTimePool, 1, &commandBuffer);
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
        FlushBatched();
        _vk.DestroyCommandPool(_device.Device, _singleTimePool, null);
        _vk.DestroyCommandPool(_device.Device, _pool, null);
    }
}
