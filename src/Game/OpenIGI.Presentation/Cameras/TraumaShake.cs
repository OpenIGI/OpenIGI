using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// The violent shake a blast gives the view: a <em>trauma</em> the blast adds and time takes away, read as the square of
/// itself through continuous noise, with a kick away from the blast and a punch of the field of view on top.
/// </summary>
/// <remarks>
/// <para>
/// Trauma rather than an amplitude, after the camera talks of the last decade: blasts add to one number in <c>[0,1]</c>, it
/// falls away linearly, and the shake is its <b>square</b> -- so a distant blast barely stirs the view and a near one throws
/// it about, and two blasts together shake harder than either without the sum running away.
/// </para>
/// <para>
/// Mostly rotation, a little translation: a turned view moves the whole picture, a moved one only what is near, which reads
/// as the ground moving under the player's feet. Three frequencies -- a fast rattle, a body-sized sway and a slow roll --
/// out of <see cref="SmoothValueNoise"/>, never fresh random numbers each frame.
/// </para>
/// <para>
/// Each blast's trauma arrives when its sound does (<c>distance / 343 m/s</c>), so the flash is seen first and the ground
/// moves after, as it does for anybody watching a charge from a distance. Stepped on the game's tick and sampled at the
/// frame's blend, so a lockstep capture shakes the same every time.
/// </para>
/// </remarks>
public sealed class TraumaShake
{
    // The widest each axis turns at full trauma, in radians, and the widest the eye moves, in metres.
    private const float MaxYaw = 0.085f;
    private const float MaxPitch = 0.07f;
    private const float MaxRoll = 0.11f;
    private const float MaxOffsetMetres = 0.09f;

    // The rattle's, the sway's and the roll's rates, in cycles a second.
    private const float Rattle = 17f;
    private const float Sway = 4.2f;
    private const float Roll = 2.1f;

    // How much trauma a second takes away: a full shake has gone in a little under two seconds.
    private const float DecayPerSecond = 0.6f;

    // The kick: an underdamped spring, so the view snaps away from the blast and settles back.
    private const float KickFrequency = 7f;
    private const float KickDamping = 4.5f;
    private const float MaxKickRadians = 0.09f;

    // The field of view's punch at full trauma, as a fraction of the field.
    private const float MaxPunch = 0.07f;

    private readonly SmoothValueNoise _noise = new(0x7EA0_B1A5);
    private readonly List<Arrival> _arrivals = [];

    private int _ticks;
    private float _trauma;
    private float _previousTrauma;
    private float _kickPitch;
    private float _kickYaw;
    private float _kickPitchSpeed;
    private float _kickYawSpeed;
    private float _previousKickPitch;
    private float _previousKickYaw;

    /// <summary>Gets the trauma at the last tick, in <c>[0,1]</c>.</summary>
    public float Trauma => _trauma;

    /// <summary>Gets a value indicating whether there is anything to shake or anything still to arrive.</summary>
    public bool IsActive => _trauma > 0f || _arrivals.Count > 0 || MathF.Abs(_kickPitch) + MathF.Abs(_kickYaw) > 1e-5f;

    /// <summary>Adds trauma that arrives after a delay, kicking the view away from where it came from.</summary>
    /// <param name="amount">How much, in <c>[0,1]</c>.</param>
    /// <param name="delaySeconds">How long until it arrives.</param>
    /// <param name="kickPitch">How far, at full trauma, it pitches the view, in radians; up is positive.</param>
    /// <param name="kickYaw">How far it turns it.</param>
    public void Add(float amount, float delaySeconds, float kickPitch = 0f, float kickYaw = 0f)
    {
        if (amount <= 0f)
        {
            return;
        }

        _arrivals.Add(new Arrival(_ticks + (int)MathF.Round(Math.Max(0f, delaySeconds) * TicksPerSecond), Math.Clamp(amount, 0f, 1f), kickPitch, kickYaw));
    }

    /// <summary>Takes away every shake at once, as the level is put back.</summary>
    public void Clear()
    {
        _arrivals.Clear();
        _trauma = _previousTrauma = 0f;
        _kickPitch = _kickYaw = _kickPitchSpeed = _kickYawSpeed = _previousKickPitch = _previousKickYaw = 0f;
    }

    /// <summary>Steps the shake one game tick.</summary>
    public void Tick()
    {
        const float dt = 1f / TicksPerSecond;
        _ticks++;
        _previousTrauma = _trauma;
        _previousKickPitch = _kickPitch;
        _previousKickYaw = _kickYaw;

        for (int i = _arrivals.Count - 1; i >= 0; i--)
        {
            Arrival arrival = _arrivals[i];
            if (arrival.Tick > _ticks)
            {
                continue;
            }

            _trauma = MathF.Min(1f, _trauma + arrival.Amount);

            // The kick is an impulse into the spring, harder the stronger the blast that reached the eye.
            _kickPitchSpeed += arrival.KickPitch * arrival.Amount * KickFrequency * 2.5f;
            _kickYawSpeed += arrival.KickYaw * arrival.Amount * KickFrequency * 2.5f;
            _arrivals.RemoveAt(i);
        }

        _trauma = MathF.Max(0f, _trauma - (DecayPerSecond * dt));

        // Semi-implicit Euler on a damped spring: stable at the tick's step for these rates.
        float stiffness = (2f * MathF.PI * KickFrequency) * (2f * MathF.PI * KickFrequency) * 0.25f;
        _kickPitchSpeed += ((-stiffness * _kickPitch) - (KickDamping * 2f * _kickPitchSpeed)) * dt;
        _kickYawSpeed += ((-stiffness * _kickYaw) - (KickDamping * 2f * _kickYawSpeed)) * dt;
        _kickPitch = Math.Clamp(_kickPitch + (_kickPitchSpeed * dt), -MaxKickRadians, MaxKickRadians);
        _kickYaw = Math.Clamp(_kickYaw + (_kickYawSpeed * dt), -MaxKickRadians, MaxKickRadians);
    }

    /// <summary>The shake at this frame.</summary>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <param name="scale">How much of it applies: the player's setting, 0 to 1.</param>
    /// <returns>The turn, the move and the punch.</returns>
    public ShakeSample Sample(float blend, float scale)
    {
        if (scale <= 0f || !IsActive)
        {
            return default;
        }

        float t = Math.Clamp(blend, 0f, 1f);
        float trauma = _previousTrauma + ((_trauma - _previousTrauma) * t);
        float shake = trauma * trauma * scale;
        float seconds = (_ticks + t) / TicksPerSecond;

        float yaw = ((_noise.Channel(seconds, Rattle, 0f) * 0.55f) + (_noise.Channel(seconds, Sway, 23f) * 0.45f)) * MaxYaw * shake;
        float pitch = ((_noise.Channel(seconds, Rattle, 7f) * 0.55f) + (_noise.Channel(seconds, Sway, 31f) * 0.45f)) * MaxPitch * shake;
        float roll = _noise.Channel(seconds, Roll, 47f) * MaxRoll * shake;
        Vector3f offset = new Vector3f(
            _noise.Channel(seconds, Rattle * 0.8f, 13f),
            _noise.Channel(seconds, Rattle * 0.7f, 19f),
            _noise.Channel(seconds, Rattle * 0.9f, 29f)) * (MaxOffsetMetres * shake);

        float kickPitch = (_previousKickPitch + ((_kickPitch - _previousKickPitch) * t)) * scale;
        float kickYaw = (_previousKickYaw + ((_kickYaw - _previousKickYaw) * t)) * scale;
        return new ShakeSample(yaw + kickYaw, pitch + kickPitch, roll, offset, MaxPunch * shake);
    }

    /// <summary>Applies a shake to an eye: turned about its own axes, moved along them.</summary>
    /// <param name="eye">The eye.</param>
    /// <param name="sample">The shake.</param>
    /// <returns>The shaken eye.</returns>
    public static Camera3D Apply(in Camera3D eye, in ShakeSample sample)
    {
        if (sample.IsNone)
        {
            return eye;
        }

        // The rows are the view's axes: right, up, forward. Yaw turns forward toward right about up; pitch turns it
        // toward up about right; roll mixes right and up about forward.
        Matrix3f look = eye.Orientation;
        Vector3f right = look.Row1;
        Vector3f up = look.Row2;
        Vector3f forward = look.Row3;

        (right, forward) = Turn(right, forward, sample.Yaw);
        (up, forward) = Turn(up, forward, sample.Pitch);
        Matrix3f turned = HandheldShake.ApplyRoll(Matrix3f.FromRows(right, up, forward), sample.Roll);

        Vector3f move = (look.Row1 * sample.Offset.X) + (look.Row2 * sample.Offset.Y) + (look.Row3 * sample.Offset.Z);
        Vector3d position = new(eye.Position.X + move.X, eye.Position.Y + move.Y, eye.Position.Z + move.Z);
        return new Camera3D(position, turned);
    }

    // Two axes turned in their own plane by an angle, kept unit length.
    private static (Vector3f A, Vector3f B) Turn(Vector3f a, Vector3f b, float angle)
    {
        if (angle == 0f)
        {
            return (a, b);
        }

        float c = MathF.Cos(angle);
        float s = MathF.Sin(angle);
        return ((a * c) - (b * s), (b * c) + (a * s));
    }

    private const int TicksPerSecond = 30;

    private readonly record struct Arrival(int Tick, float Amount, float KickPitch, float KickYaw);
}

/// <summary>One frame of a <see cref="TraumaShake"/>.</summary>
/// <param name="Yaw">The turn about the view's up, in radians.</param>
/// <param name="Pitch">The turn about its right.</param>
/// <param name="Roll">The turn about its forward.</param>
/// <param name="Offset">The move along the view's right, up and forward, in metres.</param>
/// <param name="Punch">How much wider the field of view is drawn, as a fraction of it.</param>
public readonly record struct ShakeSample(float Yaw, float Pitch, float Roll, Vector3f Offset, float Punch)
{
    /// <summary>Gets a value indicating whether it moves nothing.</summary>
    public bool IsNone => Yaw == 0f && Pitch == 0f && Roll == 0f && Offset == default;
}
