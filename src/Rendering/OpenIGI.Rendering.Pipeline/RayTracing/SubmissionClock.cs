using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>
/// Which of the traced world's frames the GPU has finished, told from the device's own work markers.
/// </summary>
/// <remarks>
/// <para>
/// Everything the world builds is reused or freed by age: a scratch range, an instance buffer or a top-level
/// structure goes back to its pool once the frame that used it has finished, and a replaced structure is
/// freed a few frames after that, because a frame already in flight may still name it. That needs a clock
/// every backend can read, which <see cref="IRhiDevice.MarkSubmittedWork"/> is.
/// </para>
/// <para>
/// A marker taken as a frame's update begins covers everything submitted before it -- every earlier frame,
/// whose work went out as that frame ended -- so once it completes, every frame before this one has
/// finished. Frames are counted from one; nought means nothing has finished yet.
/// </para>
/// </remarks>
internal sealed class SubmissionClock
{
    private readonly IRhiDevice _device;
    private readonly Queue<(ulong Frame, IRhiWorkMarker Marker)> _pending = [];

    /// <summary>Initialises a new instance of the <see cref="SubmissionClock"/> class.</summary>
    /// <param name="device">The device whose submissions are counted.</param>
    public SubmissionClock(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Gets the frame now being recorded.</summary>
    public ulong Frame { get; private set; }

    /// <summary>Gets the newest frame whose work, and every earlier frame's, the GPU has finished.</summary>
    public ulong CompletedFrame
    {
        get
        {
            while (_pending.TryPeek(out (ulong Frame, IRhiWorkMarker Marker) oldest) && oldest.Marker.IsComplete)
            {
                _pending.Dequeue();
                Completed = oldest.Frame - 1;
            }

            return Completed;
        }
    }

    private ulong Completed { get; set; }

    /// <summary>Begins the next frame: marks everything submitted so far, which finishing will date.</summary>
    /// <returns>The frame now being recorded.</returns>
    public ulong Advance()
    {
        Frame++;
        _pending.Enqueue((Frame, _device.MarkSubmittedWork()));
        return Frame;
    }

    /// <summary>Treats every frame so far as finished, for a device that has just been waited idle.</summary>
    public void Settle()
    {
        _pending.Clear();
        Completed = Frame;
    }
}
