namespace OpenIGI.Rendering.Vulkan.Queues;

/// <summary>One queue the backend will use: which family it comes from, and which queue within it.</summary>
/// <param name="Family">The queue family index.</param>
/// <param name="Index">The queue's index within that family.</param>
internal readonly record struct VulkanQueueSlot(uint Family, uint Index);

/// <summary>Which of the adapter's queues the backend will draw, compute and copy on.</summary>
/// <remarks>
/// <para>
/// Vulkan exposes an adapter's engines as families, and the useful distinction is not how many families
/// there are but which of them can run <em>independently</em> of the graphics engine. Almost every
/// desktop adapter has one family that does everything, plus a compute-only family and a transfer-only
/// family that map onto real, separate hardware. Work placed on those runs alongside the raster work
/// rather than behind it, which is the whole reason the indirect-lighting passes are compute: they
/// occupy the compute engine while the graphics engine is busy with the geometry and shadow passes.
/// </para>
/// <para>
/// A family that cannot be found is not an error — the slot simply resolves back to the graphics queue,
/// and the work serialises. That keeps every caller writing the same code whether or not the adapter
/// under it has a dedicated engine.
/// </para>
/// </remarks>
internal sealed class VulkanQueuePlan
{
    /// <summary>Initialises a new instance of the <see cref="VulkanQueuePlan"/> class.</summary>
    /// <param name="graphics">The queue drawing and presenting work is recorded onto.</param>
    /// <param name="present">The queue images are presented from.</param>
    /// <param name="asyncCompute">The queue long-running compute is placed on.</param>
    /// <param name="transfer">The queue bulk uploads are placed on.</param>
    /// <param name="queueCounts">How many queues to create from each family.</param>
    public VulkanQueuePlan(
        VulkanQueueSlot graphics,
        VulkanQueueSlot present,
        VulkanQueueSlot asyncCompute,
        VulkanQueueSlot transfer,
        IReadOnlyDictionary<uint, uint> queueCounts)
    {
        Graphics = graphics;
        Present = present;
        AsyncCompute = asyncCompute;
        Transfer = transfer;
        QueueCounts = queueCounts;
    }

    /// <summary>Gets the graphics queue slot.</summary>
    public VulkanQueueSlot Graphics { get; }

    /// <summary>Gets the present queue slot.</summary>
    public VulkanQueueSlot Present { get; }

    /// <summary>Gets the queue asynchronous compute runs on.</summary>
    public VulkanQueueSlot AsyncCompute { get; }

    /// <summary>Gets the queue bulk transfers run on.</summary>
    public VulkanQueueSlot Transfer { get; }

    /// <summary>Gets how many queues to create from each family the plan touches.</summary>
    public IReadOnlyDictionary<uint, uint> QueueCounts { get; }

    /// <summary>Gets a value indicating whether graphics and present come from the same family.</summary>
    /// <remarks>
    /// When they differ, swapchain images must be shared across both families — either concurrently or
    /// through an explicit ownership transfer — and when they match neither is needed. The swapchain
    /// reads this to choose.
    /// </remarks>
    public bool GraphicsAndPresentUnified => Graphics.Family == Present.Family;

    /// <summary>
    /// Gets a value indicating whether asynchronous compute runs on hardware independent of the graphics
    /// engine, so compute work genuinely overlaps drawing rather than queueing behind it.
    /// </summary>
    public bool HasDedicatedCompute => AsyncCompute.Family != Graphics.Family;

    /// <summary>Gets a value indicating whether transfers run on a queue independent of both others.</summary>
    public bool HasDedicatedTransfer =>
        Transfer.Family != Graphics.Family && Transfer.Family != AsyncCompute.Family;
}
