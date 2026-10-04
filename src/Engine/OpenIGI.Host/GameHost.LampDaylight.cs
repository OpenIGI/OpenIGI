using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The lamps following the daylight: off by day, struck one by one at dusk, and heard doing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original's lamps burn at noon as at midnight. The deciding is
/// <see cref="LampPhotocells"/>'s and the lamps' own behaviour is <see cref="LampSwitchboard"/>'s; what is
/// here is what only the loop knows -- how high the sun is, whether a lamp has a roof over it, where the
/// listener stands -- and the rules for when none of it applies.
/// </para>
/// <para>
/// It stands down entirely under the vanilla preset, which draws the original's lamps, and with a pinned
/// <c>-sundir</c>, which is a comparison capture: both see every lamp as the level ships it.
/// </para>
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>How much darker than its sun a fully overcast sky reads to a photocell, as a sine of elevation.</summary>
    /// <remarks>
    /// About three degrees of sun: under rain the yard's lamps come on that much sooner and go off that
    /// much later, as real ones do on a dark afternoon.
    /// </remarks>
    private const float LampCloudDarkening = 0.05f;

    /// <summary>How far round a lamp the roof test looks, in metres.</summary>
    /// <remarks>
    /// Wider than any lamp's own housing, so a lantern's hood on a pole is not its roof; narrower than any
    /// room, so a lamp in a corridor still finds the roof over the corridor on every side.
    /// </remarks>
    private const double LampRoofRing = 1.5;

    /// <summary>How many lamp sounds may start at once, after a quiet spell.</summary>
    private const float LampSoundBurst = 8f;

    /// <summary>How many lamp sounds may start a second, held over time.</summary>
    /// <remarks>
    /// A scrub through dusk strikes a whole level's lamps in a second or two. Every one of them sounding is
    /// a wall of crackle that buries the gunfire; the budget lets the first few of a run through, which is
    /// what the ear follows anyway, and the voice pool's own priorities still put a shot above any of them.
    /// </remarks>
    private const float LampSoundsPerSecond = 14f;

    private readonly List<LampEvent> _lampEvents = [];
    private LampPhotocells? _lampPhotocells;
    private ShelterHeightField? _lampShelter;
    private LampDaylight? _lampDaylightMode;
    private int _lampDaylightEpoch = -1;
    private float _lampSoundBudget = LampSoundBurst;
    private int _lampStarterCount;

    /// <summary>Gets whether the renderer draws lamps that can follow the daylight at all.</summary>
    /// <remarks>The deferred path, which vanilla does not draw, for the same reason the flashlight needs it.</remarks>
    private static bool LampDaylightAvailable => RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value;

    /// <summary>Lets the photocells see this pass's daylight, and the lamps' supplies follow.</summary>
    /// <param name="board">The level's lamps.</param>
    /// <param name="elapsed">Real seconds since the last pass.</param>
    private void UpdateLampDaylight(LampSwitchboard board, float elapsed)
    {
        if (_lampPhotocells?.Board != board)
        {
            _lampPhotocells = new LampPhotocells(board, _lightFixtures, _lampsMissionId);
            _lampShelter = null;
            _lampDaylightMode = null;
        }

        LampPhotocells cells = _lampPhotocells;
        LampDaylight mode = LampDaylightAvailable && _options.SunDirection is null
            ? RenderCVars.LampDaylight.Value
            : LampDaylight.Off;
        bool shelterChanged = !ReferenceEquals(_lampShelter, _shelterField);

        if (mode != _lampDaylightMode || shelterChanged)
        {
            _lampDaylightMode = mode;
            _lampShelter = _shelterField;

            for (int i = 0; i < _lightFixtures.Count; i++)
            {
                cells.SetFollows(
                    i,
                    mode == LampDaylight.Everywhere || (mode == LampDaylight.Outdoors && !IsUnderRoof(_lightFixtures[i])));
            }

            // The roof raster arrives part-way through a load, after the lamps; what it changes is which
            // lamps the daylight had, not the time of day, and is snapped to rather than watched. A player
            // changing the setting is a change they asked to see, and is not.
            if (shelterChanged)
            {
                cells.Unsettle();
            }
        }

        // A level put back to its opening hour opens with its lamps already as that hour has them.
        if (_dayNight.LevelEpoch != _lampDaylightEpoch)
        {
            _lampDaylightEpoch = _dayNight.LevelEpoch;
            cells.Unsettle();
        }

        float light = _dayNight.SunSine(_cycleSun ?? _sun ?? FallbackSun) - (LampCloudDarkening * WeatherCloudCover());
        cells.Update(elapsed, light);
    }

    /// <summary>Whether a lamp has a roof over it, and so is one a building keeps lit by day.</summary>
    /// <param name="fixture">The lamp.</param>
    /// <returns><see langword="true"/> when most of the ground round it is covered, or when nothing can tell.</returns>
    /// <remarks>
    /// <para>
    /// A ring round the lamp as well as the lamp itself, and most of them rather than any. The lamp's own
    /// point is not enough: a lantern's hood is solid and stands over it, and a lamp flush with a ceiling
    /// may have only the slab's thickness over it. A lamp on the outside of a wall sees the building over
    /// half its ring and open sky over the other half, and is outdoors, which it is.
    /// </para>
    /// <para>
    /// With no roof raster -- a level that places nothing solid, or one not yet built -- every lamp answers
    /// as covered, which keeps it lit: a lamp left on in daylight is a far smaller wrong than a room gone
    /// dark at noon.
    /// </para>
    /// </remarks>
    private bool IsUnderRoof(in LightFixture fixture)
    {
        if (_shelterField.IsEmpty)
        {
            return true;
        }

        const double headroom = 0.05;
        Vector3d at = fixture.Position;
        int covered = _shelterField.IsSheltered(at.X, at.Y, at.Z, headroom) ? 1 : 0;

        for (int k = 0; k < 8; k++)
        {
            double angle = k * (Math.PI / 4.0);
            double x = at.X + (LampRoofRing * Math.Cos(angle));
            double y = at.Y + (LampRoofRing * Math.Sin(angle));

            if (_shelterField.IsSheltered(x, y, at.Z, headroom))
            {
                covered++;
            }
        }

        return covered >= 6;
    }

    /// <summary>Plays what the lamps did this pass, where they stand.</summary>
    /// <param name="board">The level's lamps.</param>
    /// <param name="elapsed">Real seconds since the last pass, which refills the sound budget.</param>
    /// <param name="audible">Whether the level is being played rather than shown behind a menu.</param>
    /// <remarks>
    /// Drained every pass whether or not anything is played, so a lamp that struck behind the menu is not
    /// heard the moment the menu closes.
    /// </remarks>
    private void PlayLampSounds(LampSwitchboard board, float elapsed, bool audible)
    {
        _lampEvents.Clear();
        board.DrainEvents(_lampEvents);
        _lampSoundBudget = Math.Min(LampSoundBurst, _lampSoundBudget + (elapsed * LampSoundsPerSecond));

        if (!audible || _sounds is not { } sounds || _lampEvents.Count == 0)
        {
            return;
        }

        Vector3d listener = sounds.Listener.Position;

        foreach (LampEvent lampEvent in _lampEvents)
        {
            if ((uint)lampEvent.Index >= (uint)_lightFixtures.Count)
            {
                continue;
            }

            // The wall switch is under the player's own hand, whichever lamp it works.
            if (lampEvent.Sound == LampSound.Switch)
            {
                sounds.PlayExternal(LampSounds.SwitchName, gain: 0.55f, priority: 60, pitch: LampPitch(lampEvent.Index, 0.06f));
                continue;
            }

            (string name, float gain, float reach, float spread) = LampVoice(lampEvent, board.CharacterOf(lampEvent.Index));
            Vector3d at = _lightFixtures[lampEvent.Index].Position;

            if ((at - listener).Length > reach || _lampSoundBudget < 1f)
            {
                continue;
            }

            _lampSoundBudget -= 1f;
            sounds.PlayExternal(
                name,
                at * SoundSystem.UnitsPerMetre,
                gain,
                priority: 30,
                pitch: LampPitch(lampEvent.Index, spread),
                minimumDistanceInMetres: 1.5f,
                maximumDistanceInMetres: reach);
        }
    }

    /// <summary>Which sound a lamp's event is, how loud, how far it carries and how much its pitch may vary.</summary>
    /// <remarks>
    /// <para>
    /// The hums vary least: a ballast hums at twice the mains frequency, and every one on a site shares the
    /// mains. The sparks and ticks vary most, being small parts that are never quite the same twice.
    /// </para>
    /// <para>
    /// Every sound is normalised to the same peak, so the gains here are the mix. The hums carry several
    /// times the energy of a tick for a second or more, and are turned well down for it: what the ear should
    /// pick out of a dusk is the starters, with the ballasts a texture under them.
    /// </para>
    /// </remarks>
    private (string Name, float Gain, float Reach, float Spread) LampVoice(LampEvent lampEvent, LampCharacter character) =>
        lampEvent.Sound switch
        {
            LampSound.Starter => ((_lampStarterCount++ & 1) == 0 ? LampSounds.StarterName : LampSounds.StarterAltName, 0.55f, 24f, 0.07f),
            LampSound.Ignitor => (LampSounds.IgnitorName, 0.5f, 30f, 0.05f),
            LampSound.Arc when character == LampCharacter.Discharge => (LampSounds.DischargeHumName, 0.26f, 34f, 0.012f),
            LampSound.Arc => (LampSounds.TubeHumName, 0.18f, 20f, 0.012f),
            LampSound.Filament => (LampSounds.FilamentName, 0.25f, 10f, 0.08f),
            LampSound.Extinguish => (LampSounds.OffName, 0.25f, 16f, 0.03f),
            _ => (LampSounds.SputterName, 0.3f, 22f, 0.06f),
        };

    /// <summary>A lamp's own pitch: fixed per fitting, so the same lamp always sounds like itself.</summary>
    private static float LampPitch(int index, float spread)
    {
        uint h = (uint)(index + 1) * 0x9E3779B1u;
        h ^= h >> 15;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        float unit = (h >> 8) * (1f / 16777216f);

        return 1f + (((unit * 2f) - 1f) * spread);
    }
}
