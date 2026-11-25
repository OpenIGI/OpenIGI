using OpenIGI.Core.Graphics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// A race's ghost's optical cloak. It decides each frame how much of the ghost is there and whether the cloak is holding.
/// </summary>
/// <remarks>
/// <para>
/// The ghost materialises when it is first shown, with one failure as it does. It dissolves once its runner has fallen and
/// lain a moment, or once its run is over. Between those the cloak is steady, and fails only when something happens to the
/// run: a shudder with each shot, a hard failure when the runner is hurt (sometimes with a short aftershock), and one clean
/// failure when the player and the ghost change places. A rare, weak flicker keeps a quiet stretch from being dead. After each
/// failure the cloak closes again over a moment, which <see cref="GhostLook.Recloak"/> carries.
/// </para>
/// <para>
/// Its own random stream, never the level's: what the ghost looks like changes nothing the level does. Seeded the same every
/// race, so the same race looks the same every time.
/// </para>
/// </remarks>
public sealed class GhostCloak
{
    // How long the ghost takes to come in and to go, and how long it lies fallen, or stands at its finish, before it goes.
    private const double MaterialiseSeconds = 0.8;
    private const double DissolveSeconds = 1.2;
    private const double FallenHold = 1.5;
    private const double FinishHold = 0.4;

    // The quiet between ambient flickers, and how often a failure's pattern changes within it.
    private const double QuietLeast = 9.0;
    private const double QuietMost = 20.0;
    private const double PatternSeconds = 0.045;

    // How often a hard failure is followed by an aftershock, and how soon after it ends.
    private const double AftershockChance = 0.3;
    private const double AftershockDelay = 0.16;

    private readonly Random _random;
    private GhostEvents _pending;
    private double? _shownAt;
    private double? _dissolveAt;
    private double _ambientAt;
    private double? _aftershockAt;

    // The failure now running or due, and whether its end has started the re-cloak.
    private double _burstAt = double.NegativeInfinity;
    private double _burstLength;
    private float _burstPeak;
    private long _pattern = -1;
    private float _seed;

    // When the cloak began to close again, and how long it takes.
    private double _recloakFrom = double.NegativeInfinity;
    private double _recloakLength = 1.0;

    /// <summary>Initializes a new instance of the <see cref="GhostCloak"/> class.</summary>
    /// <param name="seed">The stream's seed.</param>
    public GhostCloak(int seed = 0x6A05) => _random = new Random(seed);

    /// <summary>Notes what happened to the runner, for the next look to answer.</summary>
    /// <param name="events">What happened.</param>
    public void Notice(GhostEvents events) => _pending |= events;

    /// <summary>How the ghost looks at a moment of its being shown.</summary>
    /// <param name="seconds">How long it has been shown; asked in order, never backwards.</param>
    /// <returns>Its look.</returns>
    public GhostLook Look(double seconds)
    {
        if (_shownAt is null)
        {
            // In it comes, and the cloak catches as it does.
            _shownAt = seconds;
            _ambientAt = seconds + Between(QuietLeast, QuietMost);
            Fail(seconds + (MaterialiseSeconds * 0.7), 0.18, 0.7f);
        }

        Answer(seconds);
        if (_aftershockAt is { } aftershock && seconds >= aftershock)
        {
            _aftershockAt = null;
            Fail(seconds, Between(0.06, 0.14), (float)Between(0.3, 0.55));
        }

        if (seconds >= _ambientAt)
        {
            _ambientAt = seconds + Between(QuietLeast, QuietMost);
            if (!Failing(seconds))
            {
                Fail(seconds, Between(0.05, 0.12), (float)Between(0.15, 0.4));
            }
        }

        float glitch = Glitch(seconds);
        float recloak = glitch > 0f
            ? 0f
            : double.IsNegativeInfinity(_recloakFrom) ? 1f : (float)Math.Clamp((seconds - _recloakFrom) / _recloakLength, 0.0, 1.0);
        return new GhostLook((float)seconds, glitch, _seed, Presence(seconds), recloak);
    }

    // What happened since the last look, answered.
    private void Answer(double seconds)
    {
        GhostEvents events = _pending;
        _pending = GhostEvents.None;
        if (events.HasFlag(GhostEvents.Fired))
        {
            Fail(seconds, 0.06, 0.25f);
        }

        if (events.HasFlag(GhostEvents.Overtaken))
        {
            Fail(seconds, 0.25, 0.6f);
        }

        if (events.HasFlag(GhostEvents.Hurt))
        {
            double length = Between(0.25, 0.35);
            Fail(seconds, length, (float)Between(0.8, 1.0));
            if (_random.NextDouble() < AftershockChance)
            {
                _aftershockAt = seconds + length + AftershockDelay;
            }
        }

        if (events.HasFlag(GhostEvents.Died))
        {
            _dissolveAt ??= seconds + FallenHold;
        }

        if (events.HasFlag(GhostEvents.Finished) && _dissolveAt is null)
        {
            _dissolveAt = seconds + FinishHold;
            Fail(_dissolveAt.Value, 0.25, 0.6f);
        }
    }

    // A failure, unless one at least as strong is already under way.
    private void Fail(double at, double length, float peak)
    {
        if (Failing(at) && Glitch(at, advance: false) >= peak)
        {
            return;
        }

        _burstAt = at;
        _burstLength = length;
        _burstPeak = peak;
        _pattern = -1;
        _recloakFrom = double.NegativeInfinity;
        _recloakLength = 0.2 + (0.35 * peak);
    }

    private bool Failing(double seconds) => seconds >= _burstAt && seconds < _burstAt + _burstLength;

    // How hard the cloak is failing: up hard, down soft, its pattern changing every few hundredths of a second.
    private float Glitch(double seconds, bool advance = true)
    {
        if (!Failing(seconds))
        {
            if (advance && seconds >= _burstAt + _burstLength && double.IsNegativeInfinity(_recloakFrom) && !double.IsNegativeInfinity(_burstAt))
            {
                _recloakFrom = _burstAt + _burstLength;
            }

            return 0f;
        }

        double t = (seconds - _burstAt) / Math.Max(_burstLength, 1e-3);
        double envelope = t < 0.15 ? t / 0.15 : 1.0 - ((t - 0.15) / 0.85 * 0.7);
        if (advance)
        {
            long pattern = (long)Math.Floor((seconds - _burstAt) / PatternSeconds);
            if (pattern != _pattern)
            {
                _pattern = pattern;
                _seed = (float)Math.Floor(_random.NextDouble() * 4096.0);
            }
        }

        // Never quite nought while it lasts, so a failure's first frame is one.
        return (float)Math.Clamp(envelope * _burstPeak * (0.75 + (0.25 * Hash(_seed))), 0.01, 1.0);
    }

    // How much of the ghost is there: coming in when first shown, going once it has fallen or finished.
    private float Presence(double seconds)
    {
        double come = Math.Clamp((seconds - _shownAt!.Value) / MaterialiseSeconds, 0.0, 1.0);
        double go = _dissolveAt is { } at ? 1.0 - Math.Clamp((seconds - at) / DissolveSeconds, 0.0, 1.0) : 1.0;
        return (float)(Smooth(come) * Smooth(go));

        static double Smooth(double x) => x * x * (3.0 - (2.0 * x));
    }

    private double Between(double least, double most) => least + (_random.NextDouble() * (most - least));

    private static float Hash(float x) => (float)(Math.Abs(Math.Sin(x * 12.9898) * 43758.5453) % 1.0);
}
