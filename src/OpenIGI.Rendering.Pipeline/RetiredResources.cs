namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// Things the renderer has finished with and the GPU has not, held until it has.
/// </summary>
/// <remarks>
/// <para>
/// A mesh released, a texture unloaded, a bind group evicted — every one of those is a device resource
/// that a frame already recorded may still name, and frames are in flight: the one being recorded, and
/// however many were submitted and have not finished. Destroying such a resource does not fail at the
/// call. It invalidates the command buffer that named it, and Vulkan then quietly refuses every command
/// recorded into it afterwards — which is a frame that presents nothing, from the first level load
/// onwards, with no error anywhere the renderer can see.
/// </para>
/// <para>
/// So nothing is destroyed where it is released. It is retired against the frame being recorded and
/// freed once that frame can no longer be in flight, which is the same rule the per-frame rings use:
/// begin frame N and frame N minus the frames in flight has certainly finished.
/// </para>
/// <para>
/// The alternative — waiting for the device to go idle at each release — is correct and costs a
/// full pipeline drain per unloaded texture, of which a level has thousands.
/// </para>
/// </remarks>
public sealed class RetiredResources : IDisposable
{
    private readonly int _framesInFlight;
    private readonly List<(ulong Frame, IDisposable Resource)> _pending = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="RetiredResources"/> class.</summary>
    /// <param name="framesInFlight">How many frames may be recorded before the first has finished.</param>
    public RetiredResources(int framesInFlight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);
        _framesInFlight = framesInFlight;
    }

    /// <summary>Gets or sets the frame being recorded, which is what a release is retired against.</summary>
    public ulong Frame { get; set; }

    /// <summary>Gets how many resources are waiting to be freed.</summary>
    /// <remarks>A test watches this: a queue that only grows is a leak, and one that never fills is a
    /// renderer whose releases are still immediate.</remarks>
    public int Pending => _pending.Count;

    /// <summary>Holds a resource until the frames that might name it have finished.</summary>
    /// <param name="resource">What to free later.</param>
    public void Retire(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (_disposed)
        {
            // Nothing is in flight after the device has gone; freeing at once is what the caller meant.
            resource.Dispose();
            return;
        }

        _pending.Add((Frame, resource));
    }

    /// <summary>Frees everything retired during a frame that has since finished.</summary>
    /// <param name="beginning">The frame about to be recorded.</param>
    /// <remarks>
    /// Called as a frame begins rather than as one ends, because that is the moment the renderer knows a
    /// frame has finished: the slot it is about to write is the one the frame this many frames ago used,
    /// and that frame is done with it.
    /// </remarks>
    public void ReleaseBefore(ulong beginning)
    {
        if (_disposed || _pending.Count == 0 || beginning < (ulong)_framesInFlight)
        {
            return;
        }

        ulong finished = beginning - (ulong)_framesInFlight;
        int kept = 0;

        for (int i = 0; i < _pending.Count; i++)
        {
            (ulong frame, IDisposable resource) = _pending[i];

            if (frame <= finished)
            {
                resource.Dispose();
                continue;
            }

            _pending[kept++] = _pending[i];
        }

        _pending.RemoveRange(kept, _pending.Count - kept);
    }

    /// <summary>Frees everything, whatever frame it was retired against.</summary>
    /// <remarks>
    /// For the caller that has waited for the device itself — a resize, a level teardown, the end of the
    /// renderer's life — where nothing can be in flight any more.
    /// </remarks>
    public void ReleaseAll()
    {
        foreach ((_, IDisposable resource) in _pending)
        {
            resource.Dispose();
        }

        _pending.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseAll();
        _disposed = true;
    }
}
