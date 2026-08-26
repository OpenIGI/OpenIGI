using System.Collections.Concurrent;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Queues;

/// <summary>What a queue is for, which decides both where work is placed and what it may contain.</summary>
internal enum VulkanQueueKind
{
    /// <summary>Drawing, presentation, and anything that must interleave with them.</summary>
    Graphics,

    /// <summary>Compute that should run alongside drawing rather than behind it.</summary>
    AsyncCompute,

    /// <summary>Bulk copies into device memory.</summary>
    Transfer,
}

/// <summary>
/// One of the device's queues, and the timeline that says how much of what was submitted to it has
/// finished.
/// </summary>
/// <remarks>
/// <para>
/// A timeline semaphore is a counter the queue signals in order. Every submission is given the next
/// value, so asking "has submission N completed" becomes "has the counter reached N" — a question the
/// CPU can ask cheaply, at any time, without a fence object per submission and without the ring of
/// fences a frame loop would otherwise need. It is also the only completion signal that can be waited
/// on for a value that has not been submitted yet, which is what lets the submission thread run ahead.
/// </para>
/// <para>
/// The counter counts <em>submissions</em>, not frames or command buffers: one increment per payload,
/// however many command buffers that payload carried. Everything that tracks GPU progress — resource
/// retirement, query readback, the acceleration-structure compaction handshake — is expressed against
/// that number.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanQueue : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly Semaphore _timeline;
    private readonly Lock _submitLock = new();
    private readonly Lock _pollLock = new();
    private readonly ConcurrentQueue<VulkanPayload> _pendingCompletion = new();

    private ulong _nextTimelineValue = 1;
    private ulong _completedTimelineValue;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanQueue"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the queue belongs to.</param>
    /// <param name="slot">Which family and index the queue comes from.</param>
    /// <param name="kind">What the queue is for.</param>
    public VulkanQueue(Vk vk, VulkanDevice device, VulkanQueueSlot slot, VulkanQueueKind kind)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device;
        Slot = slot;
        Kind = kind;

        _vk.GetDeviceQueue(device.Device, slot.Family, slot.Index, out Queue handle);
        Handle = handle;

        if (device.Capabilities.TimelineSemaphore)
        {
            SemaphoreTypeCreateInfo typeInfo = new()
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = 0,
            };
            SemaphoreCreateInfo createInfo = new()
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &typeInfo,
            };

            Semaphore semaphore;
            VkCheck.Ok(
                _vk.CreateSemaphore(device.Device, &createInfo, null, &semaphore),
                "vkCreateSemaphore (timeline)");
            _timeline = semaphore;
        }
    }

    /// <summary>Gets the queue handle.</summary>
    public Queue Handle { get; }

    /// <summary>Gets which family and index the queue came from.</summary>
    public VulkanQueueSlot Slot { get; }

    /// <summary>Gets the queue family index.</summary>
    public uint Family => Slot.Family;

    /// <summary>Gets what the queue is for.</summary>
    public VulkanQueueKind Kind { get; }

    /// <summary>Gets the timeline semaphore, or a null handle when the device has none.</summary>
    public Semaphore Timeline => _timeline;

    /// <summary>Gets a value indicating whether completion is tracked by a timeline.</summary>
    public bool HasTimeline => _timeline.Handle != 0;

    /// <summary>Gets the timeline value the most recent submission will signal.</summary>
    public ulong LastSubmittedValue => Volatile.Read(ref _nextTimelineValue) - 1;

    /// <summary>Gets the highest timeline value the queue is known to have reached.</summary>
    /// <remarks>
    /// A cached value refreshed by <see cref="Poll"/>, not a live read: callers ask it many times a frame
    /// and the driver query is not free.
    /// </remarks>
    public ulong CompletedValue => Volatile.Read(ref _completedTimelineValue);

    /// <summary>Submits a payload, assigning it the next timeline value.</summary>
    /// <param name="payload">The work to submit. The queue takes ownership of tracking its completion.</param>
    /// <exception cref="GraphicsDeviceException">The submission failed.</exception>
    public void Submit(VulkanPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        lock (_submitLock)
        {
            ulong signalValue = _nextTimelineValue;

            CommandBuffer[] commandBuffers = payload.CommandBuffers.ToArray();
            Semaphore[] waits = payload.WaitSemaphores.ToArray();
            PipelineStageFlags[] waitStages = payload.WaitStages.ToArray();

            // The queue's own timeline is always the last signal, so a payload that also signals binary
            // semaphores for another queue signals both from the one submission.
            Semaphore[] signals = HasTimeline
                ? [.. payload.SignalSemaphores, _timeline]
                : payload.SignalSemaphores.ToArray();

            // A timeline signal carries a value; a binary one ignores its slot. The arrays are parallel,
            // so the binary entries are left at zero and only the timeline's slot is meaningful.
            ulong[] waitValues = new ulong[waits.Length];
            ulong[] signalValues = new ulong[signals.Length];
            for (int i = 0; i < payload.WaitSemaphores.Count; i++)
            {
                waitValues[i] = payload.WaitValues[i];
            }

            if (HasTimeline)
            {
                signalValues[^1] = signalValue;
            }

            fixed (CommandBuffer* firstCommandBuffer = commandBuffers)
            fixed (Semaphore* firstWait = waits)
            fixed (PipelineStageFlags* firstWaitStage = waitStages)
            fixed (Semaphore* firstSignal = signals)
            fixed (ulong* firstWaitValue = waitValues)
            fixed (ulong* firstSignalValue = signalValues)
            {
                TimelineSemaphoreSubmitInfo timelineInfo = new()
                {
                    SType = StructureType.TimelineSemaphoreSubmitInfo,
                    WaitSemaphoreValueCount = (uint)waitValues.Length,
                    PWaitSemaphoreValues = firstWaitValue,
                    SignalSemaphoreValueCount = (uint)signalValues.Length,
                    PSignalSemaphoreValues = firstSignalValue,
                };

                SubmitInfo submitInfo = new()
                {
                    SType = StructureType.SubmitInfo,
                    PNext = HasTimeline ? &timelineInfo : null,
                    WaitSemaphoreCount = (uint)waits.Length,
                    PWaitSemaphores = firstWait,
                    PWaitDstStageMask = firstWaitStage,
                    CommandBufferCount = (uint)commandBuffers.Length,
                    PCommandBuffers = firstCommandBuffer,
                    SignalSemaphoreCount = (uint)signals.Length,
                    PSignalSemaphores = firstSignal,
                };

                Result submitResult = _vk.QueueSubmit(Handle, 1, &submitInfo, payload.Fence);
                if (submitResult == Result.ErrorDeviceLost)
                {
                    _device.ReportDeviceFault();
                }

                VkCheck.Ok(submitResult, "vkQueueSubmit");
            }

            payload.TimelineValue = signalValue;
            _nextTimelineValue = signalValue + 1;
        }

        _pendingCompletion.Enqueue(payload);
    }

    /// <summary>Refreshes the completed value and retires every payload the queue has finished with.</summary>
    /// <returns>How many payloads completed.</returns>
    /// <remarks>
    /// Called from whoever is driving progress — the interrupt thread when there is one, the frame loop
    /// when there is not. Payloads complete in submission order because the timeline advances in
    /// submission order, so the first one still pending is the only one that has to be examined.
    /// </remarks>
    public int Poll()
    {
        // Both the frame loop and the retirement thread poll, and peeking a payload and then dequeuing it
        // is two operations: without the lock, two pollers could each decide the same payload is theirs.
        // The lock is uncontended in the ordinary case and never held across a wait.
        lock (_pollLock)
        {
            if (HasTimeline)
            {
                ulong value = 0;
                if (_vk.GetSemaphoreCounterValue(_device.Device, _timeline, &value) == Result.Success)
                {
                    Volatile.Write(ref _completedTimelineValue, value);
                }
            }

            int completed = 0;
            while (_pendingCompletion.TryPeek(out VulkanPayload? payload))
            {
                if (HasTimeline)
                {
                    if (payload.TimelineValue > CompletedValue)
                    {
                        break;
                    }
                }
                else if (payload.Fence.Handle != 0)
                {
                    Fence fence = payload.Fence;
                    if (_vk.GetFenceStatus(_device.Device, fence) != Result.Success)
                    {
                        break;
                    }
                }

                _pendingCompletion.TryDequeue(out _);
                payload.Complete();
                completed++;
            }

            return completed;
        }
    }

    /// <summary>Blocks until the queue has passed a timeline value.</summary>
    /// <param name="value">The value to wait for.</param>
    /// <exception cref="GraphicsDeviceException">The wait failed.</exception>
    public void WaitForValue(ulong value)
    {
        if (!HasTimeline)
        {
            VkCheck.Ok(_vk.QueueWaitIdle(Handle), "vkQueueWaitIdle");
            return;
        }

        if (value <= CompletedValue)
        {
            return;
        }

        Semaphore semaphore = _timeline;
        ulong target = value;
        SemaphoreWaitInfo waitInfo = new()
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &semaphore,
            PValues = &target,
        };

        VkCheck.Ok(_vk.WaitSemaphores(_device.Device, &waitInfo, ulong.MaxValue), "vkWaitSemaphores");
        Poll();
    }

    /// <summary>Blocks until everything submitted so far has finished, then retires it.</summary>
    public void WaitIdle()
    {
        WaitForValue(LastSubmittedValue);
        Poll();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        while (_pendingCompletion.TryDequeue(out VulkanPayload? payload))
        {
            payload.Complete();
        }

        if (_timeline.Handle != 0)
        {
            _vk.DestroySemaphore(_device.Device, _timeline, null);
        }
    }
}
