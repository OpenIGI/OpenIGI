namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The queue families the backend draws its queues from: one that can render, one that can present to
/// the surface, and one that can run compute.
/// </summary>
/// <remarks>
/// On the overwhelming majority of desktop adapters a single family supports graphics, compute, and
/// presentation, so all three indices coincide and one queue serves the whole backend. They are kept
/// apart so the exceptions — an adapter whose present family is distinct, or one that offers a
/// dedicated async-compute family the GI passes can later overlap onto — are expressible without a
/// second pass over the families.
/// </remarks>
internal readonly record struct QueueFamilyIndices(uint Graphics, uint Present, uint Compute)
{
    /// <summary>Gets a value indicating whether the graphics and present families are the same one.</summary>
    /// <remarks>
    /// When they differ, swapchain images are shared across both families and the present has to wait
    /// on the render through a queue-ownership transfer or a concurrent sharing mode; when they match,
    /// neither is needed. The swapchain reads this to choose.
    /// </remarks>
    public bool GraphicsAndPresentUnified => Graphics == Present;
}
