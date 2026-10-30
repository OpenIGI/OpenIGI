namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>Whether stage timing was last switched on or off.</summary>
    public bool StageTimingEnabled { get; private set; }

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled) => StageTimingEnabled = enabled;

    /// <inheritdoc/>
    public void BeginStageTiming(RenderStage stage) { }

    /// <inheritdoc/>
    public void EndStageTiming() { }

    /// <summary>The timings this device reports; empty, since nothing here runs on a GPU.</summary>
    public List<RenderStageTiming> Timings { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() => Timings;

    /// <inheritdoc/>
    public GpuFrameTiming GpuTiming() => default;

    /// <summary>What <see cref="GpuMeasuredTiming"/> reports. Zero unless a test sets it.</summary>
    public GpuFrameTiming MeasuredTiming { get; set; }

    /// <inheritdoc/>
    public GpuFrameTiming GpuMeasuredTiming() => MeasuredTiming;

    /// <summary>Gets or sets whether the markers <see cref="MarkRecordedWork"/> hands out are complete.</summary>
    /// <remarks>
    /// True unless a test sets it: with no GPU there is nothing outstanding. Read when the marker is, not
    /// when it was taken, so a test can hold work in flight and then let it finish.
    /// </remarks>
    public bool SubmittedWorkComplete { get; set; } = true;

    /// <summary>Gets how many markers have been taken.</summary>
    public int WorkMarkers { get; private set; }

    /// <inheritdoc/>
    public IGpuWorkMarker MarkRecordedWork()
    {
        WorkMarkers++;
        return new RecordingWorkMarker(this);
    }
}
