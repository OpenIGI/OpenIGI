using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Input;

namespace OpenIGI.Desktop.World;

/// <summary>
/// An interactive day/night cycle: the level's authored sun, swept across the sky by a time of day the
/// player scrubs with two keys, and re-orientable on every axis.
/// </summary>
/// <remarks>
/// <para>
/// The level ships one authored sun, at noon. This treats that as the arc's high point and rotates it
/// down towards each horizon and under the world as the time of day moves away from noon, so a single
/// static direction becomes a full sunrise-to-night sweep. At the authored hour with no re-orientation the
/// result reproduces the authored sun exactly, so the cycle sitting in the pipeline changes nothing until
/// a key is pressed.
/// </para>
/// <para>
/// It exists to be driven live, which is what the real-time radiance cache was built for: every step of
/// the sun re-lights the surface cache and the cache re-gathers it, so the bounce, the shadows and the sky
/// fill all follow the sun round without a re-bake.
/// </para>
/// <para>
/// Controls (ignored while the console has the keyboard): <c>[</c> and <c>]</c> scrub the time of day --
/// held, a full day passes in <see cref="SecondsPerDay"/> seconds; the numpad re-orients the arc, 4/6
/// around world Z, 8/2 around X, 9/7 around Y, and 5 clears the re-orientation and returns to noon.
/// </para>
/// </remarks>
internal sealed class DayNightCycle
{
    /// <summary>Real seconds a held scrub key takes to advance a whole day.</summary>
    public const float SecondsPerDay = 30f;

    private const float HoursPerDay = 24f;
    private const float Tau = MathF.PI * 2f;

    // How fast the re-orientation keys turn the arc, in radians a second -- about fifty degrees.
    private const float TurnRate = 0.9f;

    private float _timeHours = 12f;
    private float _rotX;
    private float _rotY;
    private float _rotZ;
    private Vector3f _lastDirection = Vector3f.UnitZ;

    /// <summary>Gets the current time of day, an hour in [0, 24).</summary>
    public float TimeHours => _timeHours;

    /// <summary>Gets the sun direction the last <see cref="Apply"/> produced, for the gizmo to point at.</summary>
    public Vector3f Direction => _lastDirection;

    /// <summary>Gets how risen the moon is: nought while the sun stands, one at full night.</summary>
    /// <remarks>
    /// So the shell can treat the moon differently from the sun where the two must not be interchangeable
    /// -- the atmosphere above all, which scatters whatever illuminance it is handed and would render a
    /// moonlit sky as a dim but unmistakably blue day. Stays nought when <see cref="Apply"/> is bypassed
    /// (a pinned <c>-sundir</c> reference sun), which never hangs a moon.
    /// </remarks>
    public float Moonrise { get; private set; }

    /// <summary>Turns the sun's arc about one world axis, for the gizmo to drive as the numpad keys do.</summary>
    /// <param name="axis">0 for X, 1 for Y, 2 for Z.</param>
    /// <param name="radians">The angle to add.</param>
    public void RotateAxis(int axis, float radians)
    {
        switch (axis)
        {
            case 0: _rotX += radians; break;
            case 1: _rotY += radians; break;
            case 2: _rotZ += radians; break;
        }
    }

    /// <summary>Returns the cycle to a starting hour with no re-orientation, for a fresh level.</summary>
    /// <param name="startHours">The hour of day to open on; wrapped into [0, 24).</param>
    public void Reset(float startHours = 12f)
    {
        _timeHours = startHours - (MathF.Floor(startHours / HoursPerDay) * HoursPerDay);
        _rotX = 0f;
        _rotY = 0f;
        _rotZ = 0f;
    }

    /// <summary>Returns the cycle to the hour a level opens on: its own authored hour, unless overridden.</summary>
    /// <param name="authored">The level's sun, or <see langword="null"/> for a level that declares none.</param>
    /// <param name="overrideHours">A start hour asked for explicitly, or negative for the level's own.</param>
    /// <remarks>
    /// <see cref="Apply"/> reproduces the authored sun exactly only at the authored hour -- the arc is
    /// fitted through it -- so this is the one reset that guarantees a level opens under the light its
    /// authors placed. A level with no usable hour opens at noon, which is the arc's own reference.
    /// </remarks>
    public void ResetForLevel(DirectionalLightSettings? authored, float overrideHours = -1f)
    {
        float hour = overrideHours >= 0f
            ? overrideHours
            : authored is { TimeOfDay: > 0f and <= 24f } sun ? sun.TimeOfDay : 12f;

        Reset(hour);
    }

    /// <summary>Reads the cycle's keys and advances it on real elapsed time.</summary>
    /// <param name="keyboard">The keyboard to read. Its own capture flag suppresses input for the console.</param>
    /// <param name="deltaSeconds">Real seconds since the last call, already clamped by the caller.</param>
    public void Update(KeyboardState keyboard, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(keyboard);

        if (deltaSeconds <= 0f)
        {
            return;
        }

        float hourStep = (HoursPerDay / SecondsPerDay) * deltaSeconds;

        // IsDown already returns false while the console has the keyboard, so scrubbing never fights typing.
        if (keyboard.IsDown(KeyCode.RightBracket))
        {
            _timeHours += hourStep;
        }

        if (keyboard.IsDown(KeyCode.LeftBracket))
        {
            _timeHours -= hourStep;
        }

        _timeHours -= MathF.Floor(_timeHours / HoursPerDay) * HoursPerDay;

        float turn = TurnRate * deltaSeconds;

        if (keyboard.IsDown(KeyCode.Numpad6))
        {
            _rotZ += turn;
        }

        if (keyboard.IsDown(KeyCode.Numpad4))
        {
            _rotZ -= turn;
        }

        if (keyboard.IsDown(KeyCode.Numpad8))
        {
            _rotX += turn;
        }

        if (keyboard.IsDown(KeyCode.Numpad2))
        {
            _rotX -= turn;
        }

        if (keyboard.IsDown(KeyCode.Numpad9))
        {
            _rotY += turn;
        }

        if (keyboard.IsDown(KeyCode.Numpad7))
        {
            _rotY -= turn;
        }

        if (keyboard.IsDown(KeyCode.Numpad5))
        {
            Reset();
        }
    }

    /// <summary>Sweeps the authored sun to the current time of day and re-orientation.</summary>
    /// <param name="authored">The level's sun, read at its authored hour. Treated as the arc's high point.</param>
    /// <returns>The sun for this moment: swept direction, and a colour and sky fill that track the day.</returns>
    public DirectionalLightSettings Apply(DirectionalLightSettings authored)
    {
        // The authored sun decomposed into the arc's reference -- its elevation above the horizon and its
        // compass bearing, at its own hour. World Z is up; Direction points towards the sun.
        Vector3f d = authored.Direction;
        float length = d.Length;
        if (length > 1e-5f)
        {
            d /= length;
        }

        float referenceElevation = MathF.Asin(Math.Clamp(d.Z, -1f, 1f));
        float referenceAzimuth = MathF.Atan2(d.Y, d.X);
        float referenceHour = authored.TimeOfDay is > 0f and <= 24f ? authored.TimeOfDay : 12f;

        // Elevation follows a sine over the day, peaking at noon and dipping below the horizon at night,
        // scaled so that at the authored hour it lands back on the authored elevation. The bearing drifts a
        // full turn over the day, so at the authored hour it too returns to the authored value.
        float referencePhase = ((referenceHour - 6f) / 12f) * MathF.PI;
        float referenceSine = MathF.Sin(referencePhase);
        float peakElevation = MathF.Abs(referenceSine) > 0.05f
            ? referenceElevation / referenceSine
            : referenceElevation;
        peakElevation = Math.Clamp(peakElevation, 0.15f, 1.5533f);

        float phase = ((_timeHours - 6f) / 12f) * MathF.PI;
        float elevation = peakElevation * MathF.Sin(phase);
        float azimuth = referenceAzimuth + (((_timeHours - referenceHour) / HoursPerDay) * Tau);

        float cosElevation = MathF.Cos(elevation);
        Vector3f arc = new(
            cosElevation * MathF.Cos(azimuth),
            cosElevation * MathF.Sin(azimuth),
            MathF.Sin(elevation));

        // The player's re-orientation of the whole arc, around the three world axes.
        Vector3f direction = (Matrix3f.RotationZ(_rotZ) * Matrix3f.RotationY(_rotY) * Matrix3f.RotationX(_rotX))
            .Transform(arc);

        float directionLength = direction.Length;
        direction = directionLength > 1e-5f ? direction / directionLength : Vector3f.UnitZ;

        // How much of a day the surface sees: zero once the sun is a little below the horizon, one once it
        // is a little above, a soft twilight band between. Colour and sky fill both cross this band.
        float daylight = Smoothstep(-0.10f, 0.10f, direction.Z);

        // Near the horizon the sun reddens; the tint eases back to white as it climbs. Multiplied by the
        // daylight so the sun fades out entirely at night rather than lingering as a dim disc.
        float lowSun = 1f - Smoothstep(0f, 0.30f, direction.Z);
        Vector3f warmth = Lerp(Vector3f.One, new Vector3f(1f, 0.55f, 0.30f), lowSun);
        Vector3f colour = authored.Colour * warmth * daylight;

        // The moon: the arc's anti-solar point, so it stands highest at midnight and rises as the sun
        // sets. It takes the one directional slot only once the sun is fully faded -- the handover
        // passes through a brief true dark just below the horizon rather than the two fighting over the
        // light -- and it carries three-thousandths of a sun, cool rather than the authored warm:
        // enough for the adapted eye to read shape and for the moon to throw a shadow, never enough to
        // read as day. Because it flows through the same direction and colour as the sun, the cascades,
        // the traced shadow and the surface-cache relight all see the same moon for free. The
        // ATMOSPHERE must not (see Moonrise): handed the moon at face value it scatters a dim but
        // unmistakably blue daytime sky, where a night sky reads near black -- the shell knocks its
        // feed down separately. The sun-shadow pipeline also runs at night again; that is the price of
        // a moon that casts.
        float moonrise = Smoothstep(0.10f, 0.30f, -direction.Z);
        Moonrise = moonrise;
        if (moonrise > 0f)
        {
            direction = -direction;
            colour = new Vector3f(0.003f, 0.0033f, 0.0043f) * moonrise;
        }

        // The sky fill crosses from a faint blue night to the authored daytime ambient, so a night scene
        // reads as moonlit rather than lit by a sun that is no longer there.
        Vector3f nightAmbient = new(0.03f, 0.045f, 0.08f);
        Vector3f ambient = Lerp(nightAmbient, authored.Ambient, daylight);

        _lastDirection = direction;

        return authored with
        {
            Direction = direction,
            Colour = colour,
            Ambient = ambient,
            TimeOfDay = _timeHours,
        };
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static Vector3f Lerp(Vector3f a, Vector3f b, float t) => a + ((b - a) * t);
}
