namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>Every latency marker placed, in order, with the frame beginnings recorded as <see cref="LatencyMarker.SimulationStart"/>.</summary>
    public List<LatencyMarker> LatencyMarkers { get; } = [];

    /// <summary>Gets or sets a value indicating whether <see cref="BeginLatencyFrame"/> begins a frame.</summary>
    public bool BeginsLatencyFrames { get; set; }

    /// <inheritdoc/>
    public bool BeginLatencyFrame()
    {
        if (BeginsLatencyFrames)
        {
            Record(LatencyMarkers, LatencyMarker.SimulationStart);
        }

        return BeginsLatencyFrames;
    }

    /// <inheritdoc/>
    public void MarkLatency(LatencyMarker marker) => Record(LatencyMarkers, marker);

    /// <summary>Every low-latency setting applied, in order.</summary>
    public List<(LowLatencyMode Mode, int FrameLimit)> LowLatencySettings { get; } = [];

    /// <inheritdoc/>
    public void SetLowLatency(LowLatencyMode mode, int frameLimitFramesPerSecond) =>
        Record(LowLatencySettings, (mode, frameLimitFramesPerSecond));

    /// <inheritdoc/>
    public string LowLatencyUnavailable { get; set; } = "not a real device";

    /// <inheritdoc/>
    public bool PresentsOnRecord { get; set; }

    /// <summary>Every frame generation request, in order.</summary>
    public List<(FrameGenerationMode Mode, bool Gameplay)> FrameGenerationRequests { get; } = [];

    /// <inheritdoc/>
    public FrameGenerationStatus SetFrameGeneration(FrameGenerationMode mode, bool gameplay)
    {
        Record(FrameGenerationRequests, (mode, gameplay));
        return new FrameGenerationStatus(mode, false, 1, FrameGenerationUnavailable);
    }

    /// <inheritdoc/>
    public string FrameGenerationUnavailable { get; set; } = "not a real device";
}
