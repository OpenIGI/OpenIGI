using OpenIGI.Core.Graphics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// When a race's ghost glitches: most of the time a steady projection, and every few seconds a burst of failure -- a fraction of
/// a second of the picture torn and split, its pattern changing every few hundredths of a second, sometimes with a short
/// aftershock -- before it steadies again.
/// </summary>
/// <remarks>
/// Its own random stream, never the level's: what the ghost looks like changes nothing the level does. Seeded the same every race,
/// so a capture of one is the same every time.
/// </remarks>
public sealed class GhostGlitch
{
    // Seconds between bursts, a burst's length, and how often within one its pattern changes.
    private const double QuietLeast = 2.5;
    private const double QuietMost = 7.0;
    private const double BurstLeast = 0.12;
    private const double BurstMost = 0.45;
    private const double PatternSeconds = 0.045;

    // How often a burst is followed by an aftershock, and how soon.
    private const double AftershockChance = 0.3;
    private const double AftershockDelay = 0.16;

    private readonly Random _random;
    private double _burstAt;
    private double _burstLength;
    private float _burstPeak;
    private long _pattern = -1;
    private float _seed;

    /// <summary>Initializes a new instance of the <see cref="GhostGlitch"/> class.</summary>
    /// <param name="seed">The stream's seed.</param>
    public GhostGlitch(int seed = 0x6A05)
    {
        _random = new Random(seed);
        _burstAt = Between(1.2, 3.0);
    }

    /// <summary>How the ghost looks at a moment of its being shown.</summary>
    /// <param name="seconds">How long it has been shown; asked in order, never backwards.</param>
    /// <returns>Its look.</returns>
    public GhostLook Look(double seconds)
    {
        if (seconds >= _burstAt + _burstLength)
        {
            ScheduleAfter(seconds);
        }

        float glitch = 0f;
        if (seconds >= _burstAt)
        {
            // Up hard, down soft: a failure arrives at once and the projection recovers.
            double t = (seconds - _burstAt) / Math.Max(_burstLength, 1e-3);
            double envelope = t < 0.15 ? t / 0.15 : 1.0 - ((t - 0.15) / 0.85 * 0.7);
            long pattern = (long)Math.Floor((seconds - _burstAt) / PatternSeconds);
            if (pattern != _pattern)
            {
                _pattern = pattern;
                _seed = (float)Math.Floor(_random.NextDouble() * 4096.0);
            }

            glitch = (float)Math.Clamp(envelope * _burstPeak * (0.75 + (0.25 * Hash(_seed))), 0.0, 1.0);
        }

        return new GhostLook((float)seconds, glitch, _seed);
    }

    // The next burst: a quiet spell, or now and then an aftershock straight after the one just over.
    private void ScheduleAfter(double now)
    {
        bool aftershock = _burstLength > 0.0 && _random.NextDouble() < AftershockChance;
        _burstAt = now + (aftershock ? AftershockDelay : Between(QuietLeast, QuietMost));
        _burstLength = aftershock ? Between(0.06, 0.14) : Between(BurstLeast, BurstMost);
        _burstPeak = (float)(aftershock ? Between(0.25, 0.55) : Between(0.45, 1.0));
        _pattern = -1;
    }

    private double Between(double least, double most) => least + (_random.NextDouble() * (most - least));

    private static float Hash(float x) => (float)(Math.Abs(Math.Sin(x * 12.9898) * 43758.5453) % 1.0);
}
