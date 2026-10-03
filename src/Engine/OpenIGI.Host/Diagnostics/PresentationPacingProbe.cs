using System.Globalization;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Host.Diagnostics;

/// <summary>
/// Measures whether presented motion advances in step with real time, when the <c>Pacing</c> log category is at Debug.
/// </summary>
/// <remarks>
/// <para>
/// Frame <em>rate</em> is not smoothness. What the eye reads as judder is the presented simulation
/// clock advancing unevenly against the wall clock: a frame that covers twice as much sim time as the
/// one before it reads as a stutter however fast the frames arrive. Every frame the renderer decides
/// where it sits between two ticks, so the pair (tick count, blend) IS the sim clock the picture is
/// drawn at, exact to the fraction.
/// </para>
/// <para>
/// The metric is the ratio of sim time advanced to wall time elapsed, per frame. A perfectly paced run
/// holds it at 1: the picture moves one millisecond of animation for every millisecond that passes. A
/// spread around 1 is judder, and its width is how much. This is deliberately blind to frame rate --
/// an even 40 fps scores 1, an uneven 300 fps does not, which is exactly the distinction being made.
/// </para>
/// </remarks>
internal static class PresentationPacingProbe
{
    private const int Capacity = 20000;

    private static readonly double[] Ratios = new double[Capacity];
    private static readonly double[] FrameMilliseconds = new double[Capacity];
    private static readonly double[] Blends = new double[Capacity];
    private static int _saturated;
    private static int _smoothFrames;
    private static int _totalFrames;

    private const string LogCategory = "Pacing";

    private static int _count;
    private static double _lastWallMilliseconds;
    private static double _lastSimMilliseconds;
    private static bool _hasPrevious;

    /// <summary>Gets or sets the logger the probe reports to and asks whether it is wanted.</summary>
    /// <remarks>Turned on with <c>OPENIGI_LOG=Pacing=debug</c>, or <c>log_level Pacing debug</c> in the console.</remarks>
    public static IOpenIgiLogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>Gets whether the probe is listening, so callers can skip their own measuring.</summary>
    public static bool IsEnabled => Enabled;

    private static bool Enabled => Logger.IsEnabled(LogLevel.Debug, LogCategory);

    /// <summary>Records where in the simulation this frame was drawn, and when.</summary>
    /// <param name="wallMilliseconds">The precise wall clock the frame sampled its blend from.</param>
    /// <param name="tickCount">Ticks the simulation has completed.</param>
    /// <param name="tickBlend">How far through the current tick period the frame sits, 0 to 1.</param>
    /// <param name="tickRate">Ticks per second, which converts the pair into milliseconds of sim time.</param>
    /// <param name="smoothing">Whether the frame was presented with smoothing on.</param>
    public static void Sample(
        double wallMilliseconds, int tickCount, float tickBlend, int tickRate, bool smoothing)
    {
        if (!Enabled || tickRate <= 0)
        {
            return;
        }

        _totalFrames++;
        if (smoothing)
        {
            _smoothFrames++;
        }

        double simMilliseconds = (tickCount + tickBlend) * (1000.0 / tickRate);
        if (_hasPrevious)
        {
            double wallStep = wallMilliseconds - _lastWallMilliseconds;
            double simStep = simMilliseconds - _lastSimMilliseconds;

            // A frame that took no measurable time carries no information about pacing, and a paused or
            // rewound clock is not judder. Both are dropped rather than counted as a huge ratio.
            if (wallStep > 0.01 && simStep >= 0.0 && smoothing && _count < Capacity)
            {
                Ratios[_count] = simStep / wallStep;
                FrameMilliseconds[_count] = wallStep;
                Blends[_count] = tickBlend;
                if (tickBlend >= 0.999f)
                {
                    _saturated++;
                }

                _count++;
            }
        }

        // How the 30 Hz schedule actually lands in wall time. If the picture freezes and jumps, either
        // the blend is wrong or the ticks themselves are irregular; this tells the two apart, and a
        // healthy sim shows every interval at the tick period.
        if (tickCount != _lastTickCount)
        {
            if (_lastTickWall > 0.0 && _tickIntervalCount < Capacity)
            {
                TickIntervals[_tickIntervalCount++] =
                    (wallMilliseconds - _lastTickWall) / Math.Max(1, tickCount - _lastTickCount);
            }

            _lastTickWall = wallMilliseconds;
            _lastTickCount = tickCount;
        }

        // How many renders each tick actually gets. Below one, ticks are outrunning the display and
        // some ticks are never seen at all.
        if (tickCount == _lastTickCount)
        {
            _rendersThisTick++;
        }

        _lastWallMilliseconds = wallMilliseconds;
        _lastSimMilliseconds = simMilliseconds;
        _hasPrevious = true;
    }

    private static int _gatePause;
    private static int _gateCutscene;
    private static int _gateLockstep;
    private static int _gateCVarOff;

    /// <summary>Records which of the four conditions withheld smoothing on this frame.</summary>
    /// <remarks>
    /// Smoothing is an AND of four things, so "it was off" says nothing on its own. Counting them
    /// apart is what turns a dead blend into a named cause.
    /// </remarks>
    public static void SampleGate(bool pauseMenu, bool cutscene, bool lockstep, bool cvar)
    {
        if (!Enabled)
        {
            return;
        }

        if (pauseMenu)
        {
            _gatePause++;
        }

        if (cutscene)
        {
            _gateCutscene++;
        }

        if (lockstep)
        {
            _gateLockstep++;
        }

        if (!cvar)
        {
            _gateCVarOff++;
        }
    }

    /// <summary>Logs the pacing distribution gathered so far, if the probe is on.</summary>
    /// <param name="label">What run this is, so two reports can be told apart.</param>
    public static void Report(string label)
    {
        if (!Enabled)
        {
            return;
        }

        if (_count < 2)
        {
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "{0}  no interpolated frames to measure -- withheld by: pauseMenu {1:F1}%  "
                    + "cutscene {2:F1}%  lockstep {3:F1}%  r_smooth=0 {4:F1}%  of {5} frames",
                label,
                100.0 * _gatePause / Math.Max(1, _totalFrames),
                100.0 * _gateCutscene / Math.Max(1, _totalFrames),
                100.0 * _gateLockstep / Math.Max(1, _totalFrames),
                100.0 * _gateCVarOff / Math.Max(1, _totalFrames),
                _totalFrames));
            return;
        }

        double[] ratios = new double[_count];
        double[] frames = new double[_count];
        double[] blends = new double[_count];
        Array.Copy(Ratios, ratios, _count);
        Array.Copy(FrameMilliseconds, frames, _count);
        Array.Copy(Blends, blends, _count);
        Array.Sort(ratios);
        Array.Sort(frames);
        Array.Sort(blends);

        // How far a frame's advance strays from real time, as a fraction. The median of this is the
        // single number worth reading: it is the typical frame's error, not the worst one's.
        double[] deviations = new double[_count];
        int badFrames = 0;
        for (int i = 0; i < _count; i++)
        {
            deviations[i] = Math.Abs(ratios[i] - 1.0);
            if (deviations[i] > 0.25)
            {
                badFrames++;
            }
        }

        Array.Sort(deviations);

        Log(string.Format(
            CultureInfo.InvariantCulture,
            "{0}  frames {1}  fps~{2:F0}  frame ms p50 {3:F2} p99 {4:F2}",
            label,
            _count,
            1000.0 / Percentile(frames, 0.50),
            Percentile(frames, 0.50),
            Percentile(frames, 0.99)));

        Log(string.Format(
            CultureInfo.InvariantCulture,
            "{0}  sim/wall p01 {1:F3} p50 {2:F3} p99 {3:F3}  |err| p50 {4:F3} p95 {5:F3}  >25% off {6:F2}%",
            label,
            Percentile(ratios, 0.01),
            Percentile(ratios, 0.50),
            Percentile(ratios, 0.99),
            Percentile(deviations, 0.50),
            Percentile(deviations, 0.95),
            100.0 * badFrames / _count));

        // What separates the two explanations for a frozen picture. Smoothing simply being off pins
        // every blend at exactly 1; smoothing on but the tick running late pins only the frames that
        // outlast the deadline, and the rest sweep 0 to 1 as they should.
        Log(string.Format(
            CultureInfo.InvariantCulture,
            "{0}  r_smooth active on {1:F1}% of frames  blend p25 {2:F3} p50 {3:F3} p75 {4:F3}  saturated(=1) {5:F1}%",
            label,
            100.0 * _smoothFrames / Math.Max(1, _totalFrames),
            Percentile(blends, 0.25),
            Percentile(blends, 0.50),
            Percentile(blends, 0.75),
            100.0 * _saturated / _count));

        Log(string.Format(
            CultureInfo.InvariantCulture,
            "{0}  withheld by: pauseMenu {1:F1}%  cutscene {2:F1}%  lockstep {3:F1}%  r_smooth=0 {4:F1}%",
            label,
            100.0 * _gatePause / Math.Max(1, _totalFrames),
            100.0 * _gateCutscene / Math.Max(1, _totalFrames),
            100.0 * _gateLockstep / Math.Max(1, _totalFrames),
            100.0 * _gateCVarOff / Math.Max(1, _totalFrames)));

        if (_blockCount > 1)
        {
            double[] blocks = new double[_blockCount];
            Array.Copy(PresentBlocks, blocks, _blockCount);
            Array.Sort(blocks);
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "{0}  present block ms p50 {1:F2} p95 {2:F2}  = {3:F0}% of a {4:F1}ms frame, "
                    + "{5:F0}% of a 33.3ms tick",
                label,
                Percentile(blocks, 0.50),
                Percentile(blocks, 0.95),
                100.0 * Percentile(blocks, 0.50) / Math.Max(0.01, Percentile(frames, 0.50)),
                Percentile(frames, 0.50),
                100.0 * Percentile(blocks, 0.50) / 33.3));
        }

        if (_tickIntervalCount > 1)
        {
            double[] intervals = new double[_tickIntervalCount];
            Array.Copy(TickIntervals, intervals, _tickIntervalCount);
            Array.Sort(intervals);
            Log(string.Format(
                CultureInfo.InvariantCulture,
                "{0}  tick interval ms p01 {1:F2} p50 {2:F2} p99 {3:F2}  (ideal 33.33)  "
                    + "ticks {4}  renders/tick {5:F2}",
                label,
                Percentile(intervals, 0.01),
                Percentile(intervals, 0.50),
                Percentile(intervals, 0.99),
                _tickIntervalCount,
                (double)_count / Math.Max(1, _tickIntervalCount)));
        }
    }

    private static void Log(string line) => Logger.Debug(LogCategory, line);

    private static readonly double[] PresentBlocks = new double[Capacity];
    private static int _blockCount;
    private static readonly double[] TickIntervals = new double[Capacity];
    private static int _tickIntervalCount;
    private static int _lastTickCount = -1;
    private static double _lastTickWall;
    private static int _rendersThisTick;

    /// <summary>Records how long the present-and-acquire blocked the CPU.</summary>
    /// <remarks>
    /// This block sits between the frame arbiter's clock reading and the blend the picture is drawn
    /// at, so whatever it costs is time the decision to render rather than tick was already stale by.
    /// </remarks>
    public static void RecordPresentBlock(double milliseconds)
    {
        if (Enabled && _blockCount < Capacity)
        {
            PresentBlocks[_blockCount++] = milliseconds;
        }
    }

    /// <summary>Forgets everything measured, so a later stretch can be reported on its own.</summary>
    public static void Reset()
    {
        _count = 0;
        _hasPrevious = false;
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        int index = (int)Math.Round(fraction * (sorted.Length - 1));
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
