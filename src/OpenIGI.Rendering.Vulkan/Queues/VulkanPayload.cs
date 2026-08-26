using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Queues;

/// <summary>
/// One unit of work handed to a queue: what it waits on, the command buffers it runs, what it signals,
/// and what should happen once the GPU has finished with it.
/// </summary>
/// <remarks>
/// <para>
/// Recording and submitting are separated so they can happen on different threads at different times. A
/// pass records into command buffers and describes its dependencies; the payload carries that
/// description to whoever submits, which can then batch, reorder within the rules, or hand it to a
/// dedicated submission thread without the recording code knowing. Anything that must not happen until
/// the GPU is done — freeing a staging buffer, reading back a query, retiring a descriptor slot — is
/// attached here as a completion action rather than guarded by a device-wide wait.
/// </para>
/// <para>
/// A payload is single-use. Its timeline value is assigned by the queue at submission, and it is retired
/// exactly once when that value is reached.
/// </para>
/// </remarks>
internal sealed class VulkanPayload
{
    private readonly List<Action> _onComplete = [];
    private bool _completed;

    /// <summary>Initialises a new instance of the <see cref="VulkanPayload"/> class.</summary>
    /// <param name="queue">The queue the payload is destined for.</param>
    public VulkanPayload(VulkanQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        Queue = queue;
    }

    /// <summary>Gets the queue the payload will be submitted to.</summary>
    public VulkanQueue Queue { get; }

    /// <summary>Gets the semaphores the submission waits on before executing.</summary>
    public List<Semaphore> WaitSemaphores { get; } = [];

    /// <summary>Gets the timeline values the waits are for; ignored for binary semaphores.</summary>
    public List<ulong> WaitValues { get; } = [];

    /// <summary>Gets the pipeline stages each wait blocks, parallel to <see cref="WaitSemaphores"/>.</summary>
    public List<PipelineStageFlags> WaitStages { get; } = [];

    /// <summary>Gets the command buffers to execute, in order.</summary>
    public List<CommandBuffer> CommandBuffers { get; } = [];

    /// <summary>Gets the binary semaphores signalled when the submission completes.</summary>
    /// <remarks>
    /// The queue's own timeline is always signalled as well and is not listed here. These are the edges
    /// to <em>other</em> queues and to presentation, which cannot wait on a timeline value.
    /// </remarks>
    public List<Semaphore> SignalSemaphores { get; } = [];

    /// <summary>Gets or sets a fence signalled on completion, for devices without timeline semaphores.</summary>
    public Fence Fence { get; set; }

    /// <summary>Gets or sets the timeline value this payload signals. Assigned by the queue at submission.</summary>
    public ulong TimelineValue { get; set; }

    /// <summary>Adds a wait on a binary semaphore.</summary>
    /// <param name="semaphore">The semaphore to wait on.</param>
    /// <param name="stages">The stages the wait blocks.</param>
    public void WaitOn(Semaphore semaphore, PipelineStageFlags stages)
    {
        WaitSemaphores.Add(semaphore);
        WaitValues.Add(0);
        WaitStages.Add(stages);
    }

    /// <summary>Adds a wait until another queue's timeline reaches a value.</summary>
    /// <param name="timeline">The timeline semaphore to wait on.</param>
    /// <param name="value">The value to wait for.</param>
    /// <param name="stages">The stages the wait blocks.</param>
    public void WaitOnTimeline(Semaphore timeline, ulong value, PipelineStageFlags stages)
    {
        WaitSemaphores.Add(timeline);
        WaitValues.Add(value);
        WaitStages.Add(stages);
    }

    /// <summary>Registers work to run once the GPU has finished with this payload.</summary>
    /// <param name="action">What to do on completion.</param>
    public void OnComplete(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _onComplete.Add(action);
    }

    /// <summary>Runs the completion actions. Called by the queue once its timeline passes this payload.</summary>
    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        foreach (Action action in _onComplete)
        {
            action();
        }

        _onComplete.Clear();
    }
}
