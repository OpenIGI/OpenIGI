using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering;

/// <summary>
/// The eye adaptation's host half: a metered histogram turned into the exposure the resolve applies,
/// eased over time.
/// </summary>
/// <remarks>
/// The GPU half counts the lit frame into <see cref="Bins"/> buckets spanning a fixed log2 luminance
/// range. Here the darkest and brightest tenths of that count are trimmed away, the rest is averaged in
/// log space, the average is clamped to a plausible band and expressed relative to middle grey, and the
/// result is eased toward over time in stops: at a constant rate while far away, proportionally once
/// near, so a large change sweeps steadily and a small one settles without a visible tail.
/// </remarks>
public sealed class ExposureAdaptation
{
    private const string LogCategory = "Exposure";
    private const string HistogramLogCategory = "Exposure.Histogram";

    private readonly IOpenIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="ExposureAdaptation"/> class.</summary>
    /// <param name="logger">Where the metering is reported at Debug, for tuning; nowhere when omitted.</param>
    public ExposureAdaptation(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>How many buckets the histogram has.</summary>
    public const int Bins = 64;

    /// <summary>The number of floats in the histogram module's parameter block.</summary>
    public const int ParameterFloats = 8;

    // The histogram's span, in log2 luminance. Its buckets are evenly spaced centres from the bottom to
    // the top of this range inclusive.
    private const float HistogramLog2Low = -8f;
    private const float HistogramLog2High = 4f;

    // The share of the counted pixels discarded from the dark and the bright end before averaging, so a
    // patch of black sky or a single blown-out lamp does not drag the whole frame's exposure.
    private const double TrimLow = 0.10;
    private const double TrimHigh = 0.90;

    // The luminance a correctly exposed frame averages to.
    private const float MiddleGrey = 0.18f;

    // The band the metered average is held to by day: a tenth of middle grey to three times it. The
    // exposure therefore never opens past ten times nor closes past a third.
    private const float MinAverageLuminance = 0.1f * MiddleGrey;
    private const float MaxAverageLuminance = 3.0f * MiddleGrey;

    // How much further the eye may open at full night, in stops below the daytime floor. The daytime
    // floor was chosen for a DAYTIME scene: any night darker than a tenth of middle
    // grey sat on it, so the adaptation stopped at ten times and every light level under that came out
    // proportionally darker. A dark-adapted eye goes on opening for a good while past where a daylit one
    // stops, which is what this allows.
    //
    // Four tenths of a stop, not the two it was: the histogram now meters the whole frame -- a night's
    // dim surfaces and dark sky included, instead of only the few pixels bright enough to clear its
    // darkest bucket -- so at night the metered average sits at the bottom of the range and this floor is
    // what sets the night's exposure. Four tenths puts it at about thirteen, where the approved night's
    // ground-facing views already were; two stops would have opened a night to forty, three times as
    // bright. Still well above the histogram's own bottom (2^-8), under which nothing can be metered.
    private const float NightFloorStops = 0.4f;

    // How fast the eye follows, in stops per second, opening and closing alike.
    private const float BrightenStopsPerSecond = 3f;
    private const float DarkenStopsPerSecond = 3f;

    // Beyond this many stops from the target the easing moves at its constant rate; inside it, it moves
    // in proportion to the distance, with the slope chosen so the two agree at the boundary.
    private const float ConstantRateStops = 1.5f;

    // The deadband, in stops: a metered target this near the exposure the eye already holds is treated as
    // no change at all, and the smoothed value is left exactly where it is.
    //
    // Without it the exposure never stops moving, because the thing it meters never stops moving. The
    // real-time GI carries a small residual jitter frame to frame -- it is a sampled estimate of a
    // recursive integral, so it settles NEAR its answer rather than ON it -- and that jitter reaches the
    // frame's average luminance, which is exactly what this integrates. The eye then chases it at roughly
    // three percent a frame, and because the whole frame is scaled by the result, a residual far too small
    // to see on any one surface became a global brightness undulation that never settled: the "heat haze"
    // over a still interior. Measured on mission 1's office, holding the camera still, the frame's temporal
    // delta was about nine times larger with the adaptation running than with it off.
    //
    // A twentieth of a stop is far below what anyone can see as a step and an order of magnitude above the
    // jitter being rejected, so a real change -- walking out of a doorway, the day/night keys sweeping the
    // sun -- clears it on the first frame and adapts exactly as before. This is a stability floor on the
    // INPUT, not a slower response.
    private const float DeadbandStops = 0.05f;

    // Fixed-point scale for the atomic counters: safe against overflow up to a four-K frame in one bucket.
    private const float CountScale = 256f;

    // The eased exposure the metering has settled on so far, carried across frames in log space.
    private float _smoothedExposure;
    private bool _adapted;
    private int _debugCounter;

    /// <summary>Fills the histogram module's parameter block.</summary>
    /// <param name="data">The block, <see cref="ParameterFloats"/> long.</param>
    /// <param name="width">The lit colour's width.</param>
    /// <param name="height">The lit colour's height.</param>
    public static void WriteHistogramParameters(Span<float> data, int width, int height)
    {
        if (data.Length != ParameterFloats)
        {
            throw new ArgumentException($"The histogram block is {ParameterFloats} floats, not {data.Length}.", nameof(data));
        }

        data.Clear();

        const float span = HistogramLog2High - HistogramLog2Low;
        data[0] = width;
        data[1] = height;
        data[4] = 1f / span;
        data[5] = -HistogramLog2Low / span;
        data[6] = MathF.Pow(2f, HistogramLog2Low);
        data[7] = CountScale;
    }

    /// <summary>Advances the eased exposure toward what a metered histogram says.</summary>
    /// <param name="histogram">The <see cref="Bins"/> counters the module wrote.</param>
    /// <param name="deltaSeconds">How much time this frame advances the easing by.</param>
    /// <param name="darkAdaptation">
    /// How far into night the sky is, 0 to 1: lowers the metering floor by up to
    /// <see cref="NightFloorStops"/>, so the eye can open further on a night than on a dim day.
    /// </param>
    /// <returns>The scale that maps the metered scene to middle grey; one for an empty histogram.</returns>
    /// <remarks>
    /// The first metered call jumps straight to its target; later calls ease toward theirs, ignoring any
    /// target inside the deadband. The eased value is kept unclamped, and only what is returned is held
    /// to the metering band. An empty histogram leaves the state untouched.
    /// </remarks>
    public float Adapt(ReadOnlySpan<uint> histogram, float deltaSeconds, float darkAdaptation = 0f)
    {
        float minAverage = MinAverageLuminance
            * MathF.Pow(2f, -NightFloorStops * Math.Clamp(darkAdaptation, 0f, 1f));

        if (histogram.Length < Bins)
        {
            return 1f;
        }

        double sum = 0;
        for (int i = 0; i < Bins; i++)
        {
            sum += histogram[i];
        }

        if (sum <= 0)
        {
            return 1f;
        }

        float maxAverage = MaxAverageLuminance;

        // Treat the counts as a cumulative distribution and keep, from each bucket, only the part of its
        // interval that falls between the two trim points.
        double keepFrom = TrimLow * sum;
        double keepTo = TrimHigh * sum;
        double running = 0;
        double kept = 0;
        double weightedLog = 0;
        for (int i = 0; i < Bins; i++)
        {
            double start = running;
            running += histogram[i];
            double overlap = Math.Min(running, keepTo) - Math.Max(start, keepFrom);
            if (overlap > 0)
            {
                double centre = HistogramLog2Low + ((double)i / (Bins - 1) * (HistogramLog2High - HistogramLog2Low));
                kept += overlap;
                weightedLog += overlap * centre;
            }
        }

        float averageLuminance = kept > 0
            ? (float)Math.Pow(2.0, weightedLog / kept)
            : minAverage / MiddleGrey;
        float target = Math.Clamp(averageLuminance, minAverage, maxAverage) / MiddleGrey;

        // How the frame was counted, for tuning the floor: how much of it landed in the bottom bucket and
        // the bottom eighth, which is where a night's dark sky and unlit surfaces go.
        if (_logger.IsEnabled(LogLevel.Debug, HistogramLogCategory))
        {
            double b0 = histogram[0];
            double lowEight = 0;
            for (int i = 0; i < 8; i++)
            {
                lowEight += histogram[i];
            }

            _logger.Debug(
                HistogramLogCategory,
                $"counted={sum:F0} bucket0={b0:F0} ({b0 / sum * 100:F1}%) bins0-7={lowEight / sum * 100:F1}% avgLum={averageLuminance:F4}");
        }

        if (!_adapted)
        {
            _smoothedExposure = target;
            _adapted = true;
        }
        else
        {
            float current = MathF.Log2(_smoothedExposure);
            float distance = MathF.Log2(target) - current;
            if (MathF.Abs(distance) >= DeadbandStops && deltaSeconds > 0f)
            {
                float speed = distance > 0f ? BrightenStopsPerSecond : DarkenStopsPerSecond;
                float step = MathF.Abs(distance) > ConstantRateStops
                    ? MathF.Sign(distance) * deltaSeconds * speed
                    : distance * deltaSeconds * speed / ConstantRateStops;
                _smoothedExposure = MathF.Pow(2f, current + step);
            }
        }

        float clamped = Math.Clamp(_smoothedExposure, minAverage / MiddleGrey, maxAverage / MiddleGrey);

        // Once a second at Debug in the Exposure log category: often enough to follow, too seldom to flood.
        if ((_debugCounter++ % 60) == 0)
        {
            _logger.Debug(
                LogCategory,
                $"avgLum={averageLuminance:F4} target={target:F3} smoothed={_smoothedExposure:F3} scale={1f / clamped:F3}");
        }

        return 1f / MathF.Max(1e-4f, clamped);
    }
}
