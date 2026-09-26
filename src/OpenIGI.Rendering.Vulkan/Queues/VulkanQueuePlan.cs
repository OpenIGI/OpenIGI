namespace OpenIGI.Rendering.Vulkan.Queues;

/// <summary>One queue the backend will use: which family it comes from, and which queue within it.</summary>
/// <param name="Family">The queue family index.</param>
/// <param name="Index">The queue's index within that family.</param>
internal readonly record struct VulkanQueueSlot(uint Family, uint Index);

/// <summary>Which of the adapter's queues the backend draws and presents on.</summary>
/// <remarks>
/// Everything the renderer records goes to one queue: the frame's work is one submission that waits on
/// the acquired image and signals the present, and what little runs outside a frame is ordered behind it
/// on the same queue. Presenting may need a family of its own on an adapter whose graphics family cannot
/// reach the surface, which is the only reason there are two slots.
/// </remarks>
internal sealed class VulkanQueuePlan
{
    /// <summary>Initialises a new instance of the <see cref="VulkanQueuePlan"/> class.</summary>
    /// <param name="graphics">The queue every submission goes to.</param>
    /// <param name="present">The queue images are presented from.</param>
    public VulkanQueuePlan(VulkanQueueSlot graphics, VulkanQueueSlot present)
    {
        Graphics = graphics;
        Present = present;

        // One queue per family: a second from the same family shares the hardware, and nothing here
        // needs the ordering freedom it would buy.
        QueueCounts = graphics.Family == present.Family
            ? new Dictionary<uint, uint> { [graphics.Family] = 1 }
            : new Dictionary<uint, uint> { [graphics.Family] = 1, [present.Family] = 1 };
    }

    /// <summary>Gets the graphics queue slot.</summary>
    public VulkanQueueSlot Graphics { get; }

    /// <summary>Gets the present queue slot.</summary>
    public VulkanQueueSlot Present { get; }

    /// <summary>Gets how many queues to create from each family the plan touches.</summary>
    public IReadOnlyDictionary<uint, uint> QueueCounts { get; }

    /// <summary>Gets a value indicating whether graphics and present come from the same family.</summary>
    /// <remarks>
    /// When they differ, swapchain images must be shared across both families — either concurrently or
    /// through an explicit ownership transfer — and when they match neither is needed. The swapchain
    /// reads this to choose.
    /// </remarks>
    public bool GraphicsAndPresentUnified => Graphics.Family == Present.Family;
}
