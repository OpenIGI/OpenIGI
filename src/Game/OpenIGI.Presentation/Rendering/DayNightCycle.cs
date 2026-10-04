using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Rendering.Sky;

namespace OpenIGI.Presentation.Rendering;

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
/// It also runs on its own, when the player has asked it to, at a day length they set: that is the
/// ordinary day/night cycle rather than the scrub, and it arrives through <see cref="Update"/>.
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

    /// <summary>How many times the automatic cycle moves the clock over one whole day.</summary>
    /// <remarks>
    /// <para>
    /// The clock is advanced in whole ticks rather than continuously, and this is why: the sun is the
    /// most expensive thing in the frame to move. Every distinct direction re-lights the surface cache,
    /// re-gathers the radiance cache and re-fits the shadow cascades, and a sun advanced by a sliver on
    /// every frame is a sun that never repeats -- so all of that runs perpetually, for a change nobody
    /// can see. Held still between ticks, the sweep hands its consumers the same direction for a stretch
    /// of frames and they can tell that nothing moved.
    /// </para>
    /// <para>
    /// Counted per DAY rather than fixed in seconds so that a tick is always the same slice of the arc
    /// -- a tenth of a degree of sun -- whatever a day has been set to cost in real time. That is what
    /// lets the day length be a variable at all: at forty minutes a day the clock ticks about every
    /// two-thirds of a second, at an hour once a second, at a minute every frame, and the sweep is
    /// equally smooth at all of them. A tick fixed in seconds would instead be a step of sky that grew
    /// with the rate, and past a point that is a sun which visibly jumps.
    /// </para>
    /// <para>
    /// The scrub keys deliberately do NOT tick -- they sweep a day in <see cref="SecondsPerDay"/> and
    /// are meant to be held and watched.
    /// </para>
    /// </remarks>
    public const float AutomaticTicksPerDay = 3600f;

    /// <summary>What the moon carries when the level does not say: about an eightieth of a sun, cool.</summary>
    /// <remarks>
    /// <para>
    /// This is the cycle's own invention, and it stands for every level that authors no night of its own --
    /// which is all of Project I.G.I.
    /// </para>
    /// <para>
    /// <b>Not the physical ratio, on purpose.</b> A full moon is some four hundred thousand times dimmer
    /// than the sun, about nineteen stops. No display and no eye adaptation this game models spans that,
    /// and shown at the true ratio a night is simply black. Games and film show a night a few stops under
    /// day and carry the rest of "night" in colour: a cool key, a blue sky, and the resolve's Purkinje shift
    /// taking the colour out of what the moon lights. It was three-thousandths of a sun; with an exposure
    /// that also fell by more than a stop at night, a night mission's ground came out nearly six times
    /// darker than the original draws it. A fortieth, with the night sky and airglow filling the
    /// shadows, lit the world as a floodlit yard; at an eightieth it reads as moonlight, and
    /// <c>r_moonlight</c> scales it from there.
    /// </para>
    /// </remarks>
    public static readonly Vector3f DefaultNightColour = new(0.010f, 0.012f, 0.016f);

    /// <summary>And the fill a night crosses to when the level does not say: a faint blue.</summary>
    public static readonly Vector3f DefaultNightAmbient = new(0.03f, 0.045f, 0.08f);

    private const float HoursPerDay = 24f;
    private const float Tau = MathF.PI * 2f;

    // How fast the re-orientation keys turn the arc, in radians a second -- about fifty degrees.
    private const float TurnRate = 0.9f;

    // The elevation sine the authored sun colour is taken to be seen through: thirty degrees, where the
    // air takes little enough that a level's sun reads as its artists painted it. Lower than this the
    // sun's colour comes from the air (see AtmosphereTransmittance); higher, it stays the authored one.
    private const float ClearSunSine = 0.5f;

    // The sine of the sun's angular radius, a quarter of a degree: how far across the horizon the disc
    // takes to set, and so how long its last light takes to go.
    private const float SunDiscSine = 0.0047f;

    // Where the fill finishes crossing from day to night: the sun eighteen degrees under, the end of
    // astronomical twilight, when the last of its light has left the sky.
    private const float TwilightEndSine = -0.31f;

    // And where it starts: the sun some nine degrees up, as the golden hour begins.
    private const float TwilightStartSine = 0.15f;

    private float _timeHours = 12f;
    private float _rotX;
    private float _rotY;
    private float _rotZ;
    private float _automaticSeconds;
    private Vector3f _lastDirection = Vector3f.UnitZ;
    private Vector3f _nightColour = DefaultNightColour;
    private Vector3f _nightAmbient = DefaultNightAmbient;

    /// <summary>Gets the current time of day, an hour in [0, 24).</summary>
    public float TimeHours => _timeHours;

    /// <summary>Gets the sun direction the last <see cref="Apply"/> produced, for the gizmo to point at.</summary>
    public Vector3f Direction => _lastDirection;

    /// <summary>Gets how risen the moon is: nought while the sun stands, one at full night.</summary>
    /// <remarks>
    /// Nought until the moon holds the direct light, which is when the shell lets the weather and
    /// <c>r_moonlight</c> at it. Stays nought when <see cref="Apply"/> is bypassed (a pinned <c>-sundir</c>
    /// sun), which never hangs a moon.
    /// </remarks>
    public float Moonrise { get; private set; }

    /// <summary>Gets how far into night the sun has gone: nought in daylight, one under a midnight sky.</summary>
    /// <remarks>
    /// Measured off the sun's own elevation before the moon takes its place, and eased across a band that
    /// opens as the sun reaches the horizon and closes once it is properly under it -- so it comes on
    /// through dusk and goes off through dawn rather than switching at a moment. What reads it is the
    /// exposure: the frame is shown a little darker at night, because the eye adaptation otherwise spends
    /// the night lifting it back towards day. Stays nought when <see cref="Apply"/> is bypassed.
    /// </remarks>
    public float Nightfall { get; private set; }

    /// <summary>Gets the world direction towards the moon, whether or not it is up.</summary>
    /// <remarks>
    /// The arc's anti-solar point, which is where the cycle hangs its moon -- always full, because it always
    /// stands opposite the sun. Reported even while the sun holds the directional slot, so the sky can draw
    /// a rising moon through dusk before its light takes over. Straight down when <see cref="Apply"/> is
    /// bypassed.
    /// </remarks>
    public Vector3f MoonDirection { get; private set; } = -Vector3f.UnitZ;

    /// <summary>Gets how visible the stars are: nought in daylight, one once the sun is well under.</summary>
    /// <remarks>
    /// The eye picks out the first stars at nautical twilight and the full field only at astronomical
    /// twilight, the sun six and eighteen degrees under. The band follows that, in the sine of the sun's
    /// elevation, so the stars come out through dusk and fade through dawn. Stays nought when
    /// <see cref="Apply"/> is bypassed.
    /// </remarks>
    public float StarVisibility { get; private set; }

    /// <summary>Gets the world direction towards the sun, wherever it is -- under the horizon included.</summary>
    /// <remarks>
    /// Unlike <see cref="Direction"/>, never the moon's. The sky keeps scattering the sun from here after
    /// the moon has taken the direct light: that is the twilight glow, which fades as the sun sinks rather
    /// than going out when the direct light changes hands. Straight up when <see cref="Apply"/> is bypassed.
    /// </remarks>
    public Vector3f SunDirection { get; private set; } = Vector3f.UnitZ;

    /// <summary>Gets the sun's light before the air has coloured it, for the sky to scatter.</summary>
    /// <remarks>
    /// The level's authored sun, untinted and at full strength whatever the hour. The sky's own march
    /// reddens and dims it on the way through, and the planet's shadow puts it out; a sun already
    /// reddened and dimmed here would be reddened a second time -- which is what turned every direction of
    /// a sunset sky orange-brown -- and would take the twilight glow down with the direct light.
    /// </remarks>
    public Vector3f SkySunColour { get; private set; }

    /// <summary>Gets the moon's light as it stands, before any weather: nought until it rises.</summary>
    /// <remarks>
    /// Reported whether or not it holds the direct light, so the sky can scatter it as a second source
    /// from the moment it clears the horizon rather than only once it outshines the setting sun.
    /// </remarks>
    public Vector3f MoonColour { get; private set; }

    /// <summary>Gets a number that moves every time the cycle is put back to a level's opening hour.</summary>
    /// <remarks>
    /// For whatever follows the sun with a lag of its own -- the lamps' photocells -- to tell a new level's
    /// clock from the same clock having been scrubbed. The first is a state to snap to; the second is time
    /// passing, and is followed the slow way.
    /// </remarks>
    public int LevelEpoch { get; private set; }

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

        // A fresh level opens exactly on its hour: whatever the last one had banked towards its next tick
        // is not this one's, and carrying it over would move the sun off the authored hour immediately.
        _automaticSeconds = 0f;

        // And with the cycle's own night, until the level says otherwise. A night belonging to the last
        // level is worse than no night at all: it is a mission lit by another mission's moon.
        _nightColour = DefaultNightColour;
        _nightAmbient = DefaultNightAmbient;
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
        LevelEpoch++;
    }

    /// <summary>Gets the sine of the sun's elevation at the current hour, the sun's own, never the moon's.</summary>
    /// <param name="authored">The level's sun, as <see cref="Apply"/> is given it.</param>
    /// <returns>One with the sun overhead, nought on the horizon, negative under it.</returns>
    /// <remarks>
    /// The same sweep <see cref="Apply"/> lights the level with, re-orientation included, but without
    /// touching anything: it is for asking how light the world is from outside the frame -- before the
    /// frame has applied the sun, or on a pass that draws no frame at all.
    /// </remarks>
    public float SunSine(DirectionalLightSettings authored) => Sweep(authored, out _).Z;

    /// <summary>Sets what the cycle's night is made of, for a level that authors one of its own.</summary>
    /// <param name="colour">What the moon carries, or null for the cycle's own faint moonlight.</param>
    /// <param name="ambient">The fill at full night, or null for the cycle's own night blue.</param>
    /// <remarks>
    /// <para>
    /// The cycle invents a night because a level that authors its sun at noon does not have one. A level
    /// that DOES author one -- I.G.I. 2's night missions, which paint the sun nearly black and leave it up
    /// -- is bound to the cycle by that end instead, and then the invented night is the wrong night: it is
    /// about thirty times darker in the directional and half as bright in the fill, so the mission would
    /// open far darker than its artists drew it.
    /// </para>
    /// <para>
    /// Set per level and cleared by <see cref="ResetForLevel"/>, so a level that says nothing cannot
    /// inherit the last one's night. Project I.G.I. never says anything and keeps the constants it always
    /// had.
    /// </para>
    /// </remarks>
    public void SetNightLighting(Vector3f? colour, Vector3f? ambient)
    {
        _nightColour = colour ?? DefaultNightColour;
        _nightAmbient = ambient ?? DefaultNightAmbient;
    }

    /// <summary>Reads the cycle's keys and advances it on real elapsed time.</summary>
    /// <param name="keyboard">The keyboard to read. Its own capture flag suppresses input for the console.</param>
    /// <param name="deltaSeconds">Real seconds since the last call, already clamped by the caller.</param>
    /// <param name="automaticSecondsPerDay">
    /// Real seconds a whole day should take, for the clock that runs on its own. Nought or less stops it
    /// -- which is what the caller passes wherever time should not be passing, a menu or a paused
    /// mission -- and drops the seconds banked towards the next tick, so a pause never releases a jump.
    /// The rate is the caller's because it is the player's: it lives in <c>r_day_length</c>, and taking
    /// it as an argument is what keeps this class something a test can drive at any rate it likes.
    /// </param>
    public void Update(KeyboardState keyboard, float deltaSeconds, float automaticSecondsPerDay = 0f)
    {
        ArgumentNullException.ThrowIfNull(keyboard);

        if (deltaSeconds <= 0f)
        {
            return;
        }

        // The automatic cycle, in whole ticks -- see AutomaticTicksPerDay for why it is not continuous.
        // Accumulating and then spending the WHOLE accumulator, rather than one step of it, is what keeps
        // the clock honest at any frame rate: a pass longer than a step still contributes every second it
        // took, and a run of short passes contributes them together.
        if (automaticSecondsPerDay > 0f)
        {
            _automaticSeconds += deltaSeconds;
            if (_automaticSeconds >= automaticSecondsPerDay / AutomaticTicksPerDay)
            {
                _timeHours += (HoursPerDay / automaticSecondsPerDay) * _automaticSeconds;
                _automaticSeconds = 0f;
            }
        }
        else
        {
            _automaticSeconds = 0f;
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
        Vector3f direction = Sweep(authored, out Vector3f d);

        // How far into night the sky is, for the exposure. Deliberately a WIDER and lower band than the
        // daylight above: that one is about whether there is a sun to light with and wants to be decisive
        // about it, this one is about how dark the picture should be shown and wants the whole of dusk to
        // fade through. It closes at a sun still a little above the horizon rather than at zero, because a
        // level whose authored sun barely rises has its arc clamped to about that elevation and must still
        // read as full day at its own noon.
        Nightfall = 1f - Smoothstep(-0.35f, 0.10f, direction.Z);
        StarVisibility = 1f - Smoothstep(-0.30f, -0.08f, direction.Z);
        MoonDirection = -direction;
        SunDirection = direction;
        SkySunColour = authored.Colour;

        // What reaches the ground of the sun is what the air lets through along the path to it: the same
        // air the sky is marched through, so the light on the walls and the sky above them always agree.
        // Low, the path is long and the blue is scattered out of it first, then the green, leaving the
        // golden hour's amber and the last minutes' deep red, and much less of it. Taken relative to a sun
        // thirty degrees up, which the authored colour is read as, and never brighter than authored.
        //
        // The disc sets across its own width rather than at a point, so the last of it goes over a
        // fraction of a degree instead of switching off.
        Vector3f through = AtmosphereTransmittance.At(Math.Max(direction.Z, 0f));
        Vector3f clear = AtmosphereTransmittance.At(Math.Max(d.Z, ClearSunSine));
        Vector3f air = new(
            Math.Min(through.X / Math.Max(clear.X, 1e-6f), 1f),
            Math.Min(through.Y / Math.Max(clear.Y, 1e-6f), 1f),
            Math.Min(through.Z / Math.Max(clear.Z, 1e-6f), 1f));
        float disc = Smoothstep(-SunDiscSine, SunDiscSine, direction.Z);
        Vector3f colour = authored.Colour * air * disc;

        // The moon: the arc's anti-solar point, so it stands highest at midnight and -- being full -- rises
        // exactly as the sun sets, its light growing as it climbs off the horizon. It carries a small
        // fraction of a sun, cool rather than the authored warm (see DefaultNightColour for why not the
        // physical ratio). Because it flows through the same direction and colour as the sun, the
        // cascades, the traced shadow and the surface-cache relight all see the same moon for free.
        //
        // There is one directional slot, and it goes to whichever of the two is BRIGHTER here. So the
        // handover happens where the fading sun and the rising moon are equally bright, and the light
        // never falls below the moon's. The moon used to wait for the sun to be fully gone before it began
        // to rise (sun faded out at an elevation sine of -0.10, moon faded in from there to -0.30), which
        // put a stretch of total darkness between every dusk and night, and again before every dawn.
        //
        // The sky does not share this slot: it is handed the sun (SunDirection, SkySunColour) and the moon
        // (MoonDirection, MoonColour) as two lights of its own, so it glows through twilight whichever of
        // them the direct light belongs to.
        float moonLevel = Smoothstep(0f, 0.15f, -direction.Z);
        Vector3f moonColour = _nightColour * moonLevel;
        MoonColour = moonColour;
        bool moonHolds = moonLevel > 0f && Luminance(moonColour) > Luminance(colour);
        Moonrise = moonHolds ? moonLevel : 0f;
        if (moonHolds)
        {
            direction = -direction;
            colour = moonColour;
        }

        // The sky fill crosses from the night's to the authored daytime ambient, so a night scene reads as
        // moonlit rather than lit by a sun that is no longer there -- across the whole of twilight, from
        // the golden hour to the sun eighteen degrees under, as the sky's own light goes. It used to cross
        // within six degrees either side of the horizon, which had the fill at night while the sky still
        // glowed and the moon was still too low to light anything: the dark moment between every dusk
        // and night.
        //
        // Crossed in ratio rather than in step, as light is seen: the sky's light falls by roughly the
        // same factor for every degree the sun sinks, so an even fade to the eye is an even fade in the
        // logarithm. It never dips under the night's fill on the way.
        float twilight = Smoothstep(TwilightEndSine, TwilightStartSine, direction.Z);
        Vector3f ambient = RatioLerp(_nightAmbient, authored.Ambient, twilight);

        _lastDirection = direction;

        return authored with
        {
            Direction = direction,
            Colour = colour,
            Ambient = ambient,
            TimeOfDay = _timeHours,
        };
    }

    /// <summary>The sun's direction at the current hour and re-orientation, under the horizon included.</summary>
    /// <param name="authored">The level's sun, read at its authored hour.</param>
    /// <param name="reference">Receives the authored sun's own direction, normalised.</param>
    private Vector3f Sweep(DirectionalLightSettings authored, out Vector3f reference)
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

        reference = d;
        return direction;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static Vector3f RatioLerp(Vector3f a, Vector3f b, float t) => new(
        RatioLerp(a.X, b.X, t),
        RatioLerp(a.Y, b.Y, t),
        RatioLerp(a.Z, b.Z, t));

    // Geometric where both ends are light, straight where either is black: a ratio from nothing is not one.
    private static float RatioLerp(float a, float b, float t) =>
        a > 1e-6f && b > 1e-6f
            ? a * MathF.Pow(b / a, t)
            : a + ((b - a) * t);

    private static float Luminance(Vector3f c) => (0.2126f * c.X) + (0.7152f * c.Y) + (0.0722f * c.Z);
}
