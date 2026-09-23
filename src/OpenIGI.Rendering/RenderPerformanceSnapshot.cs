namespace OpenIGI.Rendering;

/// <summary>A structured, serialisable snapshot of renderer and presentation performance.</summary>
/// <param name="Preset">The active graphics preset.</param>
/// <param name="Width">Presentation width.</param>
/// <param name="Height">Presentation height.</param>
/// <param name="VSync">Whether presentation synchronization was active.</param>
/// <param name="FramesPerSecond">Average wall FPS.</param>
/// <param name="FrameMilliseconds">Average wall-frame time.</param>
/// <param name="OnePercentLowFramesPerSecond">One-percent-low FPS.</param>
/// <param name="P95FrameMilliseconds">95th-percentile wall-frame time.</param>
/// <param name="PeakFrameMilliseconds">Worst wall-frame time.</param>
/// <param name="FrameSampleCount">Number of wall-frame intervals in the distribution.</param>
/// <param name="GpuTotalMilliseconds">Sum of active average GPU stages.</param>
/// <param name="GpuP95Milliseconds">95th percentile of total timed GPU work per frame.</param>
/// <param name="GpuMeasuredMilliseconds">
/// The whole GPU frame measured end to end. Above <c>GpuTotalMilliseconds</c> by however much work the
/// frame does outside every stage bracket; zero on a backend that does not measure it.
/// </param>
/// <param name="GpuStages">Detailed GPU-stage statistics.</param>
public sealed record RenderPerformanceSnapshot(
    string Preset,
    int Width,
    int Height,
    bool VSync,
    double FramesPerSecond,
    double FrameMilliseconds,
    double OnePercentLowFramesPerSecond,
    double P95FrameMilliseconds,
    double PeakFrameMilliseconds,
    int FrameSampleCount,
    double GpuTotalMilliseconds,
    double GpuP95Milliseconds,
    double GpuMeasuredMilliseconds,
    IReadOnlyList<RenderStageTiming> GpuStages);

/// <summary>How a <see cref="RenderPerformanceSnapshot"/> is written: indented, by generated code.</summary>
/// <remarks>
/// Generated rather than reflected, so the shared libraries stay trimmable: the browser build trims and
/// compiles ahead of time, where a serializer that discovers its type by reflection finds nothing.
/// </remarks>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(RenderPerformanceSnapshot))]
public sealed partial class RenderPerformanceJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
