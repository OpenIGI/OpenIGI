namespace OpenIGI.Game.Player;

/// <summary>
/// The shake a hit puts in the player's view — installed by the hit, played out by the camera.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x465970(camera, position, angle, ticks)</c> arms it: an intensity of 1 at <c>camera+436</c>, the two
/// amplitudes at <c>+440</c>/<c>+444</c> and a fade rate of <c>1/ticks</c> at <c>+448</c>. The camera update
/// (<c>0x464BD0</c>) then plays it every tick while the intensity is not zero:
/// </para>
/// <code>
/// pitch += angle * I * u;  roll = angle * I * u;  yaw += angle * I * u;  // three draws, in that order
/// eye.x += position * I * u;  eye.z += position * I * u;                // then two more
/// I -= rate (towards zero, clamped at it)
/// </code>
/// <para>
/// Each <c>u</c> is a fresh draw in <c>[-1, 1)</c>, so the view jitters rather than swings, and the jitter
/// fades linearly to nothing over the installed ticks. The eye offset is along the view's own horizontal
/// right axis and along world Z. A bullet or a blow installs up to 5 cm and 0.75 degrees for fifteen ticks
/// (<c>0x412440</c>, <c>0x412CE0</c>), an electric fence the full amount for 45, and a blast its share for
/// the explosion's own duration (<c>0x412990</c>).
/// </para>
/// </remarks>
public sealed class HumanViewShake
{
    /// <summary>The full eye displacement a hit installs, in units — 5 cm (<c>204.8</c>).</summary>
    public const float HitPositionUnits = 204.8f;

    /// <summary>The full angle a hit installs, in radians — 0.75 degrees (<c>0.01308997</c>).</summary>
    public const float HitAngleRadians = 0.01308997f;

    /// <summary>How long a bullet's or a blow's shake runs — fifteen ticks.</summary>
    public const int HitTicks = 15;

    /// <summary>How long an electric fence's shake runs — forty-five ticks (<c>0x412F80</c>).</summary>
    public const int FenceTicks = 45;

    private float _intensity;
    private float _positionUnits;
    private float _angleRadians;
    private float _fadePerTick;

    /// <summary>Gets this tick's pitch offset, in radians.</summary>
    public float Pitch { get; private set; }

    /// <summary>Gets this tick's roll, about the view's forward axis, in radians.</summary>
    public float Roll { get; private set; }

    /// <summary>Gets this tick's yaw offset, in radians, in the engine's own sense (added to the eye's gamma).</summary>
    public float Yaw { get; private set; }

    /// <summary>Gets this tick's eye displacement along the view's horizontal right axis, in units.</summary>
    public float RightUnits { get; private set; }

    /// <summary>Gets this tick's eye displacement along world Z, in units.</summary>
    public float UpUnits { get; private set; }

    /// <summary>Gets whether a shake is still playing.</summary>
    public bool IsActive => _intensity != 0f;

    /// <summary>Arms the shake, as <c>0x465970</c> does.</summary>
    /// <param name="positionUnits">The full eye displacement, in units.</param>
    /// <param name="angleRadians">The full angle, in radians.</param>
    /// <param name="ticks">How many ticks it fades over.</param>
    public void Install(float positionUnits, float angleRadians, int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        _intensity = 1f;
        _positionUnits = positionUnits;
        _angleRadians = angleRadians;
        _fadePerTick = 1f / ticks;
    }

    /// <summary>Steps the shake one tick, where <c>0x464BD0</c> steps it.</summary>
    /// <param name="random">The engine stream, giving values in <c>[0, 1)</c>; drawn five times while active.</param>
    public void Advance(Func<double> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (_intensity == 0f)
        {
            Pitch = Roll = Yaw = RightUnits = UpUnits = 0f;
            return;
        }

        float angle = _angleRadians * _intensity;
        Pitch = angle * Signed(random);
        Roll = angle * Signed(random);
        Yaw = angle * Signed(random);

        float displacement = _positionUnits * _intensity;
        RightUnits = displacement * Signed(random);
        UpUnits = displacement * Signed(random);

        _intensity = _intensity > 0f
            ? Math.Max(0f, _intensity - _fadePerTick)
            : Math.Min(0f, _intensity + _fadePerTick);
    }

    /// <summary>Stops the shake at once.</summary>
    public void Reset()
    {
        _intensity = 0f;
        Pitch = Roll = Yaw = RightUnits = UpUnits = 0f;
    }

    // (r - 0.5) + (r - 0.5): a draw in [-1, 1).
    private static float Signed(Func<double> random)
    {
        double r = random();
        return (float)((r - 0.5) + (r - 0.5));
    }
}
