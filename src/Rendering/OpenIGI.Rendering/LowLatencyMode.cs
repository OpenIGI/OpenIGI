namespace OpenIGI.Rendering;

/// <summary>How the frame's start is paced to cut the time from input to screen (NVIDIA Reflex).</summary>
public enum LowLatencyMode
{
    /// <summary>No pacing. Latency is still measured.</summary>
    Off,

    /// <summary>Start each frame as late as the measured render time allows.</summary>
    On,

    /// <summary>As <see cref="On"/>, and keep the GPU's clocks up when the frame is processor-bound.</summary>
    Boost,
}
