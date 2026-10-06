namespace OpenIGI.Rendering;

/// <summary>The boundaries in a frame's life the application places for latency measurement.</summary>
/// <remarks>
/// Only the ones the application can place honestly. Present start and end are raised by the backend
/// around its own present call, because it is the only thing that knows exactly when that happens.
/// </remarks>
public enum LatencyMarker
{
    /// <summary>The frame's simulation starts. Input sampling follows it.</summary>
    SimulationStart,

    /// <summary>The frame's simulation ends.</summary>
    SimulationEnd,

    /// <summary>The frame's rendering commands start being recorded.</summary>
    RenderSubmitStart,

    /// <summary>The frame's rendering commands have all been recorded and handed over.</summary>
    RenderSubmitEnd,
}
