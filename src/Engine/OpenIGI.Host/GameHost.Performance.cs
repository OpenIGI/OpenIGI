using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Diagnostics;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Presentation;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using OpenIGI.Host.Services;

using System.Diagnostics;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    private long _benchmarkAllocStart;
    private (int, int, int) _benchmarkGcStart;

    /// <summary>Writes the <c>-benchmark</c> snapshot once the run is over: the frame times, the GPU's and each stage's.</summary>
    /// <param name="state">What the frame loop kept, whose frame timings are reported.</param>
    private void WriteBenchmarkSnapshot(FrameLoopState state)
    {
        if (_options.BenchmarkPath is { } benchmarkPath)
        {
            IReadOnlyList<RenderStageTiming> gpuStages = _device.StageTimings();
            FramePerformanceStatistics frame = state.FramePerformance.Statistics();
            GpuFrameTiming gpu = _device.GpuTiming();

            if (_benchmarkAllocStart > 0)
            {
                long allocated = GC.GetTotalAllocatedBytes() - _benchmarkAllocStart;
                int measured = Math.Max(1, frame.SampleCount);

                // Warning, not Information: the shell builds its logger at Warning, so an Information call
                // here is discarded and the benchmark's allocation report -- the one number that says
                // whether a stutter is the collector rather than the GPU -- was written to nothing. The
                // camera log had the same hole for the same reason.
                _logger.Warning(
                    "Benchmark",
                    FormattableString.Invariant(
                        $"Managed alloc over {measured} measured frames: {allocated / (1024.0 * 1024.0):0.0} MB, {allocated / (double)measured / 1024.0:0.0} KB/frame; gc {GC.CollectionCount(0) - _benchmarkGcStart.Item1}/{GC.CollectionCount(1) - _benchmarkGcStart.Item2}/{GC.CollectionCount(2) - _benchmarkGcStart.Item3}."));
            }
            RenderPerformanceSnapshot snapshot = new(
                GraphicsPresetRegistry.Default.Current.ToString(),
                _window.Width,
                _window.Height,
                _device.VSyncEnabled,
                frame.FramesPerSecond,
                frame.AverageMilliseconds,
                frame.OnePercentLowFramesPerSecond,
                frame.P95Milliseconds,
                frame.PeakMilliseconds,
                frame.SampleCount,
                gpu.Milliseconds,
                gpu.P95Milliseconds,
                _device.GpuMeasuredTiming().Milliseconds,
                gpuStages.ToArray());

            string? benchmarkDirectory = Path.GetDirectoryName(Path.GetFullPath(benchmarkPath));
            if (!string.IsNullOrEmpty(benchmarkDirectory))
            {
                Directory.CreateDirectory(benchmarkDirectory);
            }

            string json = System.Text.Json.JsonSerializer.Serialize(snapshot, RenderPerformanceJsonContext.Default.RenderPerformanceSnapshot);
            File.WriteAllText(benchmarkPath, json);
            _logger.Information("Perf", $"Wrote benchmark snapshot to {Path.GetFullPath(benchmarkPath)}.");
        }
    }

    /// <summary>Writes the stage, pass and slowest-frame timings to the log when <c>r_perflog</c> is on.</summary>
    private void LogStageTimingsAtExit()
    {
        // The measurement, written down. The overlay answers "is it fast right now" for somebody watching
        // it; this is what a capture run leaves behind, and it is the only form the acceptance criterion
        // can actually be checked in -- nobody reads an overlay in a thirty-frame headless capture.
        if (RenderCVars.PerformanceLog.Value)
        {
            IReadOnlyList<RenderStageTiming> finalTimings = _device.StageTimings();

            if (finalTimings.Count == 0)
            {
                _logger.Information("Perf", "No stage timings were collected; the run was too short to resolve a query.");
            }
            else
            {
                int over = 0;

                foreach (RenderStageTiming timing in finalTimings)
                {
                    if (!timing.Active)
                    {
                        _logger.Information("Perf", $"{timing.Stage,-28} off");
                        continue;
                    }

                    double? budget = RenderBudget.For(timing.Stage);
                    bool exceeded = budget is { } allowed && timing.Milliseconds > allowed;
                    over += exceeded ? 1 : 0;

                    string against = budget is { } allowance
                        ? FormattableString.Invariant($"  budget {allowance:0.0} ms{(exceeded ? "  OVER" : string.Empty)}")
                        : "  (unbudgeted)";

                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant(
                            $"{timing.Stage,-28} avg {timing.Milliseconds,6:0.000} ms  p95 {timing.P95Milliseconds,6:0.000} ms  peak {timing.PeakMilliseconds,6:0.000} ms{against}"));
                }

                foreach (RenderPassTiming pass in _device.PassTimings())
                {
                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant(
                            $"  pass {pass.Label,-32} avg {pass.Milliseconds,6:0.000} ms  peak {pass.PeakMilliseconds,7:0.000} ms  ran {pass.FramesRun} frame(s)  [{pass.Stage}]"));
                }

                // The 1% low, taken apart: what each pass cost in the slowest frames, beside its overall average.
                IReadOnlyList<RenderPassTiming> worst = _device.WorstFramePassTimings(0.01, out int worstFrames, out double worstAverage);
                if (worstFrames > 0)
                {
                    Dictionary<string, double> overall = [];
                    foreach (RenderPassTiming pass in _device.PassTimings())
                    {
                        overall[pass.Label] = pass.Milliseconds;
                    }

                    _logger.Information(
                        "Perf",
                        FormattableString.Invariant($"Slowest 1% ({worstFrames} frame(s)): summed GPU avg {worstAverage:0.000} ms."));

                    foreach (RenderPassTiming pass in worst)
                    {
                        double usual = overall.GetValueOrDefault(pass.Label);
                        _logger.Information(
                            "Perf",
                            FormattableString.Invariant(
                                $"  worst {pass.Label,-32} {pass.Milliseconds,7:0.000} ms  (usually {usual,6:0.000}, {pass.Milliseconds - usual,+7:0.000})  ran in {pass.FramesRun}/{worstFrames}"));
                    }
                }

                GpuFrameTiming gpu = _device.GpuTiming();
                _logger.Information(
                    "Perf",
                    FormattableString.Invariant(
                        $"GPU total avg {gpu.Milliseconds:0.000} ms  p95 {gpu.P95Milliseconds:0.000} ms  peak {gpu.PeakMilliseconds:0.000} ms of the {RenderBudget.FrameMilliseconds:0.00} ms a 60 Hz frame has; {over} stage(s) over budget."));
            }
        }
    }
}
