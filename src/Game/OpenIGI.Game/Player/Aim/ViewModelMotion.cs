using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player.Aim;

/// <summary>What the view model's motion reads from one drawn frame.</summary>
/// <param name="Seconds">How long the frame is, in seconds.</param>
/// <param name="Yaw">The look's heading this frame, in radians; turning right raises it.</param>
/// <param name="Pitch">The look's pitch this frame, in radians; looking up raises it.</param>
/// <param name="Strafe">The sideways input, minus one (left) to one (right).</param>
/// <param name="ShotSequence">The count of rounds the weapon in hand has let go, which a shot moves on.</param>
/// <param name="Profile">The aim profile of the weapon in hand, or <see langword="null"/> when it cannot be aimed.</param>
/// <param name="Aim">The aim's eased progress this frame, nought (hip) to one (sights).</param>
/// <param name="Braced">Whether the weapon is braced (<see cref="WeaponMount"/>): it barely sways and kicks less.</param>
/// <param name="BraceSequence">The count of braces, which a new brace moves on: the weapon settles onto it.</param>
public readonly record struct ViewModelMotionFrame(
    float Seconds,
    float Yaw,
    float Pitch,
    float Strafe,
    int ShotSequence,
    AdsProfile? Profile,
    float Aim,
    bool Braced = false,
    int BraceSequence = 0);

/// <summary>
/// The motion modern aim lays over the first-person weapon, frame by frame: it lags the look, leans against a strafe, swings
/// up to the eye, breathes on the sights and kicks with every round. <b>Not retail</b>, and presentation only: nothing here
/// reaches where a round goes.
/// </summary>
/// <remarks>
/// <para>
/// Every part is a damped spring pulled towards a target the frame sets, stepped in short fixed substeps so it settles the
/// same way at thirty frames a second as at three hundred. A shot does not move a target: it throws the springs, which then
/// overshoot a little and settle -- the snap and the recovery a fired weapon has.
/// </para>
/// <para>
/// The weapon turns about where it is held: the grip at the hip, the rear sight on the sights, so a kick through the sights
/// climbs the muzzle without throwing the sight picture off the eye.
/// </para>
/// </remarks>
public sealed class ViewModelMotion
{
    private const float Step = 1f / 240f;
    private const float MaxFrameSeconds = 0.1f;
    private const float Degrees = MathF.PI / 180f;

    // What a brace makes of the sway, and of a round's kick in the hands.
    private const float BracedSway = 0.25f;
    private const float BracedKick = 0.6f;

    // The springs: three of position (world units, eye frame) and three of angle (radians: pitch, yaw, roll).
    private readonly Spring[] _springs = [new(140f, 0.75f), new(140f, 0.75f), new(140f, 0.75f), new(160f, 0.7f), new(160f, 0.7f), new(170f, 0.7f)];
    private readonly Spring[] _kick = [new(260f, 0.55f), new(260f, 0.55f), new(260f, 0.55f), new(300f, 0.5f), new(280f, 0.5f), new(280f, 0.5f)];
    private float _lastYaw;
    private float _lastPitch;
    private int _lastShot;
    private int _lastBrace;
    private bool _primed;
    private float _clock;
    private uint _seed = 0x9E3779B9u;

    /// <summary>Puts the weapon back at rest at once, with nothing in motion: for a teleport, a death, modern aim switched off.</summary>
    public void Reset()
    {
        foreach (Spring spring in _springs)
        {
            spring.Reset();
        }

        foreach (Spring spring in _kick)
        {
            spring.Reset();
        }

        _primed = false;
    }

    /// <summary>Steps the motion through one drawn frame.</summary>
    /// <param name="frame">What the frame reads.</param>
    /// <returns>The move to lay over the rig, in the eye's frame, in world units.</returns>
    public RigOffset Advance(in ViewModelMotionFrame frame)
    {
        float seconds = Math.Clamp(frame.Seconds, 0f, MaxFrameSeconds);
        if (!_primed)
        {
            _lastYaw = frame.Yaw;
            _lastPitch = frame.Pitch;
            _lastShot = frame.ShotSequence;
            _lastBrace = frame.BraceSequence;
            _primed = true;
        }

        float aim = Math.Clamp(frame.Aim, 0f, 1f);
        float sway = AimDownSights.Blend(1f, frame.Profile?.SwayScale ?? 1f, aim) * (frame.Braced ? BracedSway : 1f);
        if (frame.BraceSequence != _lastBrace)
        {
            // Settling onto the brace: a short drop and a nod as the weapon's weight comes onto it.
            _lastBrace = frame.BraceSequence;
            _kick[2].Velocity -= 0.012f * (float)WorldScene.UnitsPerMetre * 30f;
            _kick[3].Velocity -= 0.6f * Degrees * 30f;
        }
        float yawRate = seconds > 0f ? Interpolation.LerpAngle(0f, frame.Yaw - _lastYaw, 1f) / seconds : 0f;
        float pitchRate = seconds > 0f ? (frame.Pitch - _lastPitch) / seconds : 0f;
        _lastYaw = frame.Yaw;
        _lastPitch = frame.Pitch;

        if (frame.ShotSequence != _lastShot)
        {
            _lastShot = frame.ShotSequence;
            Kick(frame.Profile, aim, frame.Braced);
        }

        // The targets. Turning right swings the weapon's muzzle left and shifts it left, behind the turn; looking up drops it.
        // A strafe leans its top away from the way the body goes. Each is bounded so a flick cannot throw the weapon off the
        // screen, and the sights take most of it out.
        float unitsPerMetre = (float)WorldScene.UnitsPerMetre;
        float lagYaw = Math.Clamp(yawRate * 0.012f, -0.07f, 0.07f) * sway;
        float lagPitch = Math.Clamp(-pitchRate * 0.010f, -0.05f, 0.05f) * sway;
        float swing = MathF.Sin(MathF.PI * aim);
        _clock += seconds;
        float breathe = aim * sway;
        _springs[0].Target = -lagYaw * 0.10f * unitsPerMetre;
        _springs[1].Target = 0f;
        _springs[2].Target = (lagPitch * 0.08f * unitsPerMetre) - (swing * 0.006f * unitsPerMetre);
        _springs[3].Target = lagPitch + (breathe * 0.0016f * MathF.Sin((_clock * 1.7f) + 1f));
        _springs[4].Target = lagYaw + (breathe * 0.0020f * MathF.Sin(_clock * 1.1f));
        _springs[5].Target = (-Math.Clamp(frame.Strafe, -1f, 1f) * 2.5f * Degrees * sway) - (swing * 4f * Degrees);

        for (float left = seconds; left > 0f; left -= Step)
        {
            float dt = MathF.Min(Step, left);
            foreach (Spring spring in _springs)
            {
                spring.Advance(dt);
            }

            foreach (Spring spring in _kick)
            {
                spring.Advance(dt);
            }
        }

        Vector3f shift = new(
            _springs[0].Value + _kick[0].Value,
            _springs[1].Value + _kick[1].Value,
            _springs[2].Value + _kick[2].Value);
        Matrix3f turn = Matrix3f.RotationZ(_springs[4].Value + _kick[4].Value)
            * Matrix3f.RotationX(_springs[3].Value + _kick[3].Value)
            * Matrix3f.RotationY(_springs[5].Value + _kick[5].Value);

        // About the hand at the hip and the rear sight on the sights.
        float relief = frame.Profile?.EyeReliefMetres ?? 0.2f;
        Vector3f pivot = Vector3f.Lerp(new Vector3f(0.10f, 0.30f, -0.12f), new Vector3f(0f, relief, 0f), aim) * unitsPerMetre;
        return new RigOffset(turn, pivot - RigOffset.Multiply(turn, pivot) + shift);
    }

    // A round's kick, thrown into the springs: back and up, the muzzle climbing, a little sideways and a roll at random.
    private void Kick(AdsProfile? profile, float aim, bool braced)
    {
        AdsKick kick = profile?.Kick ?? new AdsKick(0.010f, 0.004f, 1.6f, 0.5f, 1f);
        float scale = AimDownSights.Blend(1f, profile?.AimedKickScale ?? 1f, aim) * (braced ? BracedKick : 1f);
        float unitsPerMetre = (float)WorldScene.UnitsPerMetre;
        _kick[1].Velocity -= kick.BackMetres * unitsPerMetre * 30f * scale;
        _kick[2].Velocity += kick.UpMetres * unitsPerMetre * 30f * scale;
        _kick[3].Velocity += kick.PitchDegrees * Degrees * 30f * scale;
        _kick[4].Velocity += kick.YawDegrees * Degrees * 30f * scale * Signed();
        _kick[5].Velocity += kick.RollDegrees * Degrees * 30f * scale * Signed();
    }

    // Minus one to one, from a small generator of the motion's own: the look of a kick needs no share of the game's draws.
    private float Signed()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return ((_seed & 0xFFFF) / 32767.5f) - 1f;
    }

    // One damped spring: a stiffness in its natural frequency's square, and a damping ratio (one stops dead, less overshoots).
    private sealed class Spring(float stiffness, float dampingRatio)
    {
        private readonly float _damping = 2f * dampingRatio * MathF.Sqrt(stiffness);

        public float Target { get; set; }

        public float Value { get; private set; }

        public float Velocity { get; set; }

        public void Reset()
        {
            Target = 0f;
            Value = 0f;
            Velocity = 0f;
        }

        // Semi-implicit Euler: the speed first, then the place from the new speed, which keeps a stiff spring stable.
        public void Advance(float seconds)
        {
            Velocity += ((stiffness * (Target - Value)) - (_damping * Velocity)) * seconds;
            Value += Velocity * seconds;
        }
    }
}
