namespace OpenIGI.Rendering;

/// <summary>Latency measurement and NVIDIA Reflex: the frame's markers, its pacing, and when it is presented.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ILatencyDevice
{
    /// <summary>Begins a frame for latency measurement, pacing its start when low latency is on.</summary>
    /// <returns><see langword="true"/> when a new frame began; false while the last one is still unpresented.</returns>
    /// <remarks>
    /// Called at the top of every pass of the host loop, before input is read; the device begins a frame only
    /// once the previous one has reached the screen, so the loop passes that only tick neither pace nor
    /// count. Where it begins one it sleeps as Reflex asks (the whole point is that the frame starts LATER,
    /// so its input is fresher) and marks the simulation's start. Does nothing without NVIDIA's runtime.
    /// </remarks>
    bool BeginLatencyFrame();

    /// <summary>Marks a boundary in the current frame for latency measurement.</summary>
    /// <param name="marker">Which boundary.</param>
    /// <remarks>The present's own boundaries are the device's; these are the ones only the host knows.</remarks>
    void MarkLatency(LatencyMarker marker);

    /// <summary>Sets NVIDIA Reflex's mode and its frame-rate cap.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="frameLimitFramesPerSecond">A cap in frames per second, or nought for none.</param>
    /// <remarks>Cheap to repeat; sent to the driver only when it changes.</remarks>
    void SetLowLatency(LowLatencyMode mode, int frameLimitFramesPerSecond);

    /// <summary>Gets why low latency cannot run here, or an empty string where it can.</summary>
    string LowLatencyUnavailable { get; }

    /// <summary>Gets a value indicating whether the frame should be presented the moment it is recorded.</summary>
    /// <remarks>
    /// The original presents the frame at the head of the NEXT pass of its loop, and the engine keeps that
    /// cadence. Latency measurement cannot work that way: the frame would be submitted to the GPU only once
    /// the next one had begun, and every marker would describe the wrong frame. Where this is true the host
    /// presents at the end of its recording instead, and the scheduled present finds nothing left to do.
    /// </remarks>
    bool PresentsOnRecord { get; }
}
