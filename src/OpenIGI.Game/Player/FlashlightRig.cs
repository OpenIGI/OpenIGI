using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The player's flashlight as a thing held in a hand: whether it is on, and where it points.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original has no flashlight. This is the half of the remaster's that is not
/// rendering, kept apart from the host so it can be tested: the switch, a very short ramp so switching never
/// pops a whole frame from dark to lit, and the aim.
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
    /// <summary>How long the light takes to come fully on or go fully off, in seconds.</summary>
    public const float RampSeconds = 0.03f;

    /// <summary>A change of aim larger than this, in degrees, is a cut rather than a turn.</summary>
    public const float SnapDegrees = 60f;

    /// <summary>How far a stride carries the player, in metres: one loop of the sway.</summary>
    public const float StrideMetres = 1.6f;

    /// <summary>How far the sway swings at full running pace, in degrees.</summary>
    public const float SwayDegrees = 0.6f;

    private float _swayPhase;
    private float _breathPhase;
    private bool _hasAim;

    /// <summary>Gets whether the switch is on.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Gets how much of its light the torch is giving out, from nought to one.</summary>
    public float Level { get; private set; }

    /// <summary>Gets the world direction the beam points, unit length.</summary>
    public Vector3f Aim { get; private set; } = new(0f, 1f, 0f);

    /// <summary>Flips the switch.</summary>
    /// <returns>Whether the light is now on.</returns>
    public bool Toggle()
    {
        IsOn = !IsOn;
        return IsOn;
    }

    /// <summary>Switches the light off at once, with no ramp: a mission ending, a death, a preset without it.</summary>
    public void ForceOff()
    {
        IsOn = false;
        Level = 0f;
    }

    /// <summary>Forgets the aim, so the next update takes the view's direction as it is.</summary>
    public void ResetAim() => _hasAim = false;

    /// <summary>Advances the switch's ramp and the aim by one frame.</summary>
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

        float step = seconds / RampSeconds;
        Level = IsOn ? MathF.Min(Level + step, 1f) : MathF.Max(Level - step, 0f);

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

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Vector3f Normalise(Vector3f v, Vector3f fallback)
    {
        float length = MathF.Sqrt(Dot(v, v));
        return length > 1e-6f ? v * (1f / length) : fallback;
    }
}
