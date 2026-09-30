using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The player's flashlight as a thing held in a hand: whether it is on, and where it points.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original has no flashlight. This is the half of the remaster's that is not
/// rendering, kept apart from the host so it can be tested: the switch, how the light comes and goes, and the
/// aim.
/// </para>
/// <para>
/// The light is an LED torch's, and it does not fade like a slider being dragged, any more than the level's
/// lamps do (<see cref="World.LampSwitchboard"/>). Nothing changes until the switch's latch snaps over, a beat
/// after the press and on the click's snap (<see cref="OnLatchSeconds"/>). The driver then soft-starts it in
/// about ten milliseconds, with a small overshoot that settles over a fifth of a second. Now and then a worn
/// contact chatters first -- a blip or two before it catches -- rolled per switch-on, so it is never the same
/// twice and never every time. Switching off, most of the light is gone in a few milliseconds and a dim glow
/// bleeds away over a third of a second as the driver's capacitor drains.
/// </para>
/// <para>
/// The curves are functions of the time since the switch, and each frame shows the curve's average over the
/// frame rather than its value at the frame's end, as a camera's exposure would: a blip thirty milliseconds
/// long is seen the same at thirty frames a second as at two hundred and forty, instead of being caught or
/// missed by where the frames happen to fall.
/// </para>
/// <para>
/// The aim is what makes a torch read as held rather than bolted to the screen. It trails the view by a
/// short exponential lag -- no spring, so it never overshoots and wobbles back -- and walking sways it on a
/// figure of eight, one loop a stride, the way a light carried at the chest rides the body's gait. A turn of
/// more than <see cref="SnapDegrees"/> in one update is not a turn but a cut, and the aim is taken as given.
/// </para>
/// </remarks>
public sealed class FlashlightRig
{
    /// <summary>How long after the press the latch snaps over and the light comes on, in seconds.</summary>
    /// <remarks>The switching-on click's latch snap is at the same moment, so the two land together.</remarks>
    public const float OnLatchSeconds = 0.019f;

    /// <summary>How long after the press the latch releases and the light goes out, in seconds.</summary>
    /// <remarks>The switching-off click's release is at the same moment.</remarks>
    public const float OffLatchSeconds = 0.012f;

    /// <summary>How long after switching on the light is certain to have settled to full, in seconds.</summary>
    public const float SettledSeconds = 0.9f;

    /// <summary>How long after switching off the glow is certain to have gone, in seconds.</summary>
    public const float DarkSeconds = 0.5f;

    /// <summary>A change of aim larger than this, in degrees, is a cut rather than a turn.</summary>
    public const float SnapDegrees = 60f;

    /// <summary>How far a stride carries the player, in metres: one loop of the sway.</summary>
    public const float StrideMetres = 1.6f;

    /// <summary>How far the sway swings at full running pace, in degrees.</summary>
    public const float SwayDegrees = 0.6f;

    /// <summary>How fast a chattering contact's blip rises, in seconds: a touch, sharper than the driver's start.</summary>
    private const float ContactRise = 0.004f;

    /// <summary>How many points of the switch's curve each frame averages.</summary>
    private const int FrameSamples = 8;

    private float _switchSeconds = float.PositiveInfinity;
    private float _from;
    private uint _rng = 0x9E3779B9u;
    private int _chatters;
    private float _chatterOn1;
    private float _chatterOff1;
    private float _chatterOn2;
    private float _chatterOff2;
    private float _chatterPeak;
    private float _swayPhase;
    private float _breathPhase;
    private bool _hasAim;

    /// <summary>Gets whether the switch is on.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Gets how much of its light the torch is giving out: nought to one, a little over while it settles.</summary>
    public float Level { get; private set; }

    /// <summary>Gets or sets how worn the switch's contacts are, from nought to one.</summary>
    /// <remarks>
    /// The chance a switch-on chatters before it catches: nought is a new torch that always comes straight on.
    /// Read when the switch is flipped, so changing it never disturbs a light already on.
    /// </remarks>
    public float ContactWear { get; set; } = 0.3f;

    /// <summary>Gets the world direction the beam points, unit length.</summary>
    public Vector3f Aim { get; private set; } = new(0f, 1f, 0f);

    /// <summary>Flips the switch.</summary>
    /// <returns>Whether the light is now on.</returns>
    public bool Toggle()
    {
        IsOn = !IsOn;

        // From wherever the light is now, so a switch flipped back mid-fade carries on from what is seen.
        _from = Level;
        _switchSeconds = 0f;
        _chatters = 0;

        // Only from dark: a contact that is already carrying current has made its connection.
        float wear = Math.Clamp(ContactWear, 0f, 1f);
        if (IsOn && _from < 0.15f && NextFloat() < wear)
        {
            _chatters = NextFloat() < 0.35f ? 2 : 1;
            _chatterOn1 = 0.022f + (0.025f * NextFloat());
            _chatterOff1 = 0.03f + (0.045f * NextFloat());
            _chatterOn2 = 0.012f + (0.018f * NextFloat());
            _chatterOff2 = 0.02f + (0.03f * NextFloat());
            _chatterPeak = 0.55f + (0.4f * NextFloat());
        }

        return IsOn;
    }

    /// <summary>Switches the light off at once, with no fade: a mission ending, a death, a preset without it.</summary>
    public void ForceOff()
    {
        IsOn = false;
        Level = 0f;
        _from = 0f;
        _switchSeconds = float.PositiveInfinity;
    }

    /// <summary>Forgets the aim, so the next update takes the view's direction as it is.</summary>
    public void ResetAim() => _hasAim = false;

    /// <summary>Advances the switch and the aim by one frame.</summary>
    /// <param name="seconds">The frame's elapsed time; nought for a paused frame, which moves nothing.</param>
    /// <param name="target">The world direction the beam should settle on.</param>
    /// <param name="right">The view's world right, which the sway swings across.</param>
    /// <param name="up">The view's world up, which the sway bobs along.</param>
    /// <param name="movedMetres">How far the body travelled this frame, which drives the sway.</param>
    /// <param name="paceFraction">How fast it was going against a run, from nought to one.</param>
    /// <param name="lagHalfLife">The half-life of the aim's lag, in seconds; nought locks it to the target.</param>
    /// <param name="swayScale">How much the sway swings; nought holds it still.</param>
    public void Update(
        float seconds,
        Vector3f target,
        Vector3f right,
        Vector3f up,
        float movedMetres,
        float paceFraction,
        float lagHalfLife,
        float swayScale)
    {
        seconds = MathF.Max(seconds, 0f);

        AdvanceSwitch(seconds);

        target = Normalise(target, Aim);

        // One loop of the figure of eight a stride, plus a slow breath that never stops, so a torch held
        // perfectly still still lives a little.
        _swayPhase = (_swayPhase + (MathF.Max(movedMetres, 0f) / StrideMetres * MathF.Tau)) % MathF.Tau;
        _breathPhase = (_breathPhase + (seconds * 1.4f)) % MathF.Tau;

        float pace = Math.Clamp(paceFraction, 0f, 1f);
        float swing = SwayDegrees * pace * MathF.Max(swayScale, 0f) * (MathF.PI / 180f);
        float breath = 0.08f * MathF.Max(swayScale, 0f) * (MathF.PI / 180f);
        float yaw = (swing * MathF.Sin(_swayPhase)) + (breath * MathF.Sin(_breathPhase * 0.7f));
        float pitch = (0.5f * swing * MathF.Sin(2f * _swayPhase)) + (breath * MathF.Sin(_breathPhase));

        Vector3f swayed = Normalise(target + (right * MathF.Tan(yaw)) + (up * MathF.Tan(pitch)), target);

        if (!_hasAim || Dot(Aim, swayed) < MathF.Cos(SnapDegrees * (MathF.PI / 180f)))
        {
            Aim = swayed;
            _hasAim = true;
            return;
        }

        if (lagHalfLife <= 0f)
        {
            Aim = swayed;
            return;
        }

        // Exponential, so it is the same lag at any frame rate: what is left of the gap halves every half-life.
        float keep = MathF.Pow(0.5f, seconds / lagHalfLife);
        Aim = Normalise((Aim * keep) + (swayed * (1f - keep)), swayed);
    }

    // Moves the switch's clock on by a frame and takes the light's average over that frame.
    private void AdvanceSwitch(float seconds)
    {
        if (seconds <= 0f || float.IsPositiveInfinity(_switchSeconds))
        {
            return;
        }

        float start = _switchSeconds;
        _switchSeconds += seconds;

        if (start >= (IsOn ? SettledSeconds : DarkSeconds))
        {
            Level = IsOn ? 1f : 0f;
            _switchSeconds = float.PositiveInfinity;
            return;
        }

        float sum = 0f;
        for (int i = 1; i <= FrameSamples; i++)
        {
            sum += Curve(start + (seconds * i / FrameSamples));
        }

        Level = sum / FrameSamples;
    }

    // The light this long after the switch was flipped.
    private float Curve(float t)
    {
        if (!IsOn)
        {
            if (t < OffLatchSeconds)
            {
                return _from;
            }

            // Most of it at once as the current stops; the rest the capacitor bleeding the emitter dim.
            float u = t - OffLatchSeconds;
            float left = _from * ((0.85f * MathF.Exp(-u / 0.008f)) + (0.15f * MathF.Exp(-u / 0.09f)));
            return left < 0.002f ? 0f : left;
        }

        if (t < OnLatchSeconds)
        {
            return _from;
        }

        float s = t - OnLatchSeconds;
        if (_chatters == 0)
        {
            return SoftStart(s, _from) * Overshoot(s);
        }

        // The contact touching, springing apart and touching again before it holds: each touch a sharp blip,
        // each gap the driver dropping out.
        if (s < _chatterOn1)
        {
            return _chatterPeak * (1f - MathF.Exp(-s / ContactRise));
        }

        s -= _chatterOn1;
        if (s < _chatterOff1)
        {
            return 0f;
        }

        s -= _chatterOff1;
        if (_chatters > 1)
        {
            if (s < _chatterOn2)
            {
                return 0.8f * _chatterPeak * (1f - MathF.Exp(-s / ContactRise));
            }

            s -= _chatterOn2;
            if (s < _chatterOff2)
            {
                return 0f;
            }

            s -= _chatterOff2;
        }

        return SoftStart(s, 0f) * Overshoot(s);
    }

    // The driver bringing the current up: most of the way in ten milliseconds, from whatever was lit.
    private static float SoftStart(float s, float from) => from + ((1f - from) * (1f - MathF.Exp(-s / 0.01f)));

    // The emitter comes on a touch bright while it is cold and settles as it warms.
    private static float Overshoot(float s) => 1f + (0.06f * MathF.Exp(-s / 0.18f));

    // A small xorshift, so the chatter is rolled the same way every run and never from the clock.
    private float NextFloat()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) / 16777216f;
    }

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector3f Normalise(Vector3f v, Vector3f fallback)
    {
        float length = MathF.Sqrt(Dot(v, v));
        return length > 1e-6f ? v * (1f / length) : fallback;
    }
}
