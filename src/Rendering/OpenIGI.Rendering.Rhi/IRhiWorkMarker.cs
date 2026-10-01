namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// A point in the queue: complete once every submission made before it was taken has finished on the GPU.
/// </summary>
/// <remarks>
/// WebGPU's <c>onSubmittedWorkDone</c>, and a timeline value on Vulkan. What it answers is how far behind the
/// host the GPU has fallen, which no measurement on the host can: the host records a frame and moves on, and
/// until the frames in flight fill up nothing it times includes the work it has queued. A caller that sizes
/// its work by how long its own frames take can therefore bury the GPU in the first few frames before any
/// of that shows.
/// </remarks>
public interface IRhiWorkMarker
{
    /// <summary>Gets whether everything submitted before this was taken has finished.</summary>
    /// <remarks>
    /// Never blocks. Where completion arrives through an event loop, reading this drives it once, so a
    /// caller that polls it every frame sees it complete without doing anything else.
    /// </remarks>
    bool IsComplete { get; }
}
