namespace OpenIGI.Rendering;

/// <summary>
/// A point in the recorded work, complete once the GPU has finished everything recorded before it.
/// </summary>
/// <remarks>
/// See <see cref="IGraphicsDevice.MarkRecordedWork"/>. Polled, never waited on.
/// </remarks>
public interface IGpuWorkMarker
{
    /// <summary>Gets whether the GPU has finished everything recorded before this was taken.</summary>
    bool IsComplete { get; }
}
