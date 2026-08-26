using System.Collections.Concurrent;

namespace OpenIGI.Rendering.Vulkan.Queues;

/// <summary>
/// Carries recorded work from the thread that recorded it to the queue that runs it, and retires it
/// once the GPU is done.
/// </summary>
/// <remarks>
/// <para>
/// Submitting is not free — the driver validates, patches and hands off, and on the thread that recorded
/// the frame that cost lands squarely between one pass and the next. Moving it onto a thread of its own
/// means a pass finishes recording and immediately starts on the next one while the previous submission
/// is still being handed to the driver. That matters most where work is interleaved across queues:
/// acceleration-structure builds on transfer, gather passes on compute, geometry on graphics, all being
/// submitted while any of them is still being recorded.
/// </para>
/// <para>
/// Retirement is a second thread rather than part of the first. A payload's completion actions free
/// staging memory, read back queries and return descriptor slots, and those should happen as soon as the
/// GPU passes the payload — not whenever the next submission happens to come along. Splitting them means
/// a quiet frame still retires its resources promptly, and a busy one never delays a submission to do
/// bookkeeping.
/// </para>
/// <para>
/// Both threads are optional. With them disabled the submission happens inline on the calling thread and
/// retirement runs whenever <see cref="Poll"/> is called, which is what the frame loop does anyway — the
/// same code path, just without the hand-off.
/// </para>
/// </remarks>
internal sealed class VulkanSubmissionQueue : IDisposable
{
    // Long enough that an idle backend costs nothing, short enough that a payload whose completion frees
    // memory is not held for a visible fraction of a frame.
    private static readonly TimeSpan RetirementPollInterval = TimeSpan.FromMilliseconds(10);

    private readonly ConcurrentQueue<VulkanPayload> _pending = new();
    private readonly AutoResetEvent _submitSignal = new(false);
    private readonly AutoResetEvent _retireSignal = new(false);

    // Drained means every payload handed over has been through vkQueueSubmit — not merely that the queue
    // of waiting payloads is empty. The difference matters: a payload is taken off the queue before it is
    // submitted, and only after the submit does it have a timeline value to wait on.
    private readonly ManualResetEventSlim _drained = new(true);
    private readonly Lock _drainGate = new();
    private int _unsubmitted;
    private readonly IReadOnlyList<VulkanQueue> _queues;
    private readonly Thread? _submissionThread;
    private readonly Thread? _retirementThread;

    private volatile bool _running;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanSubmissionQueue"/> class.</summary>
    /// <param name="queues">Every queue whose completion should be tracked.</param>
    /// <param name="threaded">
    /// Whether to run the submission and retirement threads. When false both happen on the calling
    /// thread, which is the right shape for a single-threaded bring-up and for tests.
    /// </param>
    public VulkanSubmissionQueue(IReadOnlyList<VulkanQueue> queues, bool threaded)
    {
        ArgumentNullException.ThrowIfNull(queues);
        _queues = queues;

        if (!threaded)
        {
            return;
        }

        _running = true;
        _submissionThread = new Thread(SubmissionLoop)
        {
            Name = "Vulkan Submission",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _retirementThread = new Thread(RetirementLoop)
        {
            Name = "Vulkan Retirement",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _submissionThread.Start();
        _retirementThread.Start();
    }

    /// <summary>Gets a value indicating whether submission runs on its own thread.</summary>
    public bool Threaded => _submissionThread is not null;

    /// <summary>Hands a payload over to be submitted.</summary>
    /// <param name="payload">The recorded work.</param>
    /// <remarks>
    /// Returns as soon as the payload is queued when threaded, and after the submission itself when not.
    /// Either way the caller must not touch the payload's command buffers again.
    /// </remarks>
    public void Enqueue(VulkanPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!Threaded)
        {
            payload.Queue.Submit(payload);
            return;
        }

        lock (_drainGate)
        {
            _unsubmitted++;
            _drained.Reset();
            _pending.Enqueue(payload);
        }

        _submitSignal.Set();
    }

    /// <summary>Submits everything queued and blocks until it has been handed to the driver.</summary>
    /// <remarks>
    /// The point where a caller that is about to wait on GPU progress makes sure the work it is waiting
    /// for has actually been submitted — otherwise it would wait for a timeline value the submission
    /// thread has not yet reached.
    /// </remarks>
    public void Flush()
    {
        if (!Threaded)
        {
            return;
        }

        _submitSignal.Set();
        _drained.Wait();
    }

    /// <summary>Retires every payload the GPU has finished with.</summary>
    /// <returns>How many payloads were retired.</returns>
    public int Poll()
    {
        int completed = 0;
        foreach (VulkanQueue queue in _queues)
        {
            completed += queue.Poll();
        }

        return completed;
    }

    /// <summary>Wakes the retirement thread, for when a caller knows work has just finished.</summary>
    public void NotifyRetirement() => _retireSignal.Set();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;
        _submitSignal.Set();
        _retireSignal.Set();
        _submissionThread?.Join();
        _retirementThread?.Join();
        _submitSignal.Dispose();
        _retireSignal.Dispose();
        _drained.Dispose();
    }

    private void SubmissionLoop()
    {
        while (_running)
        {
            while (_pending.TryDequeue(out VulkanPayload? payload))
            {
                payload.Queue.Submit(payload);
                MarkSubmitted();
                _retireSignal.Set();
            }

            _submitSignal.WaitOne();
        }

        // Drain whatever was queued as the backend shut down, so nothing is left holding resources that
        // teardown is about to free.
        while (_pending.TryDequeue(out VulkanPayload? payload))
        {
            payload.Queue.Submit(payload);
            MarkSubmitted();
        }
    }

    private void MarkSubmitted()
    {
        lock (_drainGate)
        {
            if (--_unsubmitted == 0)
            {
                _drained.Set();
            }
        }
    }

    private void RetirementLoop()
    {
        while (_running)
        {
            Poll();

            // A timed wait rather than a pure signal: a payload completes on the GPU's schedule, and
            // nothing on the CPU is told when that happens. The signal is what makes the common case —
            // work was just submitted, so completion is coming — prompt, and the timeout is what covers
            // the rest.
            _retireSignal.WaitOne(RetirementPollInterval);
        }
    }
}
