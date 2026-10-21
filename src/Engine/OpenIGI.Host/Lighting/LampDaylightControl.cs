using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Rendering;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Host.Lighting;

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
internal sealed class LampDaylightControl
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



    private LampPhotocells? _lampPhotocells;

    /// <summary>Gets the photocells of the level's lamps, once the daylight has first run over them.</summary>
    public LampPhotocells? Photocells => _lampPhotocells;
    private ShelterHeightField? _lampShelter;
    private WorldRayCast? _lampWalls;
    private LampDaylight? _lampDaylightMode;
    private int _lampDaylightEpoch = -1;

    /// <summary>Gets whether the renderer draws lamps that can follow the daylight at all.</summary>
    /// <remarks>The deferred path, which vanilla does not draw, for the same reason the flashlight needs it.</remarks>
    private static bool LampDaylightAvailable => FrameCVars.OffscreenScene.Value && LightingCVars.Deferred.Value;

    /// <summary>Lets the photocells see this pass's daylight, and the lamps' supplies follow.</summary>
    /// <param name="board">The level's lamps.</param>
    /// <param name="elapsed">Real seconds since the last pass.</param>
    /// <param name="fixtures">The level's fixtures.</param>
    /// <param name="missionId">The mission, whose circuits the photocells are wired by.</param>
    /// <param name="shelter">The roofs over the level.</param>
    /// <param name="walls">The world's caster, when it is up.</param>
    /// <param name="sunOverridden">Whether the command line fixed the sun, which keeps every lamp on.</param>
    /// <param name="dayNight">The day-night clock.</param>
    /// <param name="sun">The sun the clock moves.</param>
    /// <param name="cloudCover">How much of the sky the weather covers, from nothing to one.</param>
    public void Update(
        LampSwitchboard board,
        float elapsed,
        IReadOnlyList<LightFixture> fixtures,
        int missionId,
        ShelterHeightField shelter,
        WorldRayCast? walls,
        bool sunOverridden,
        DayNightCycle dayNight,
        DirectionalLightSettings sun,
        float cloudCover)
    {
        if (_lampPhotocells?.Board != board)
        {
            _lampPhotocells = new LampPhotocells(board, fixtures, missionId);
            _lampShelter = null;
            _lampWalls = null;
            _lampDaylightMode = null;
        }

        LampPhotocells cells = _lampPhotocells;
        LampDaylight mode = LampDaylightAvailable && !sunOverridden
            ? ExposureCVars.LampDaylight.Value
            : LampDaylight.Off;
        bool shelterChanged = !ReferenceEquals(_lampShelter, shelter) || !ReferenceEquals(_lampWalls, walls);

        if (mode != _lampDaylightMode || shelterChanged)
        {
            _lampDaylightMode = mode;
            _lampShelter = shelter;
            _lampWalls = walls;

            for (int i = 0; i < fixtures.Count; i++)
            {
                cells.SetFollows(
                    i,
                    mode == LampDaylight.Everywhere || (mode == LampDaylight.Outdoors && !IsIndoors(fixtures[i], shelter, walls)));
            }

            // The roof raster and the walls arrive part-way through a load, after the lamps; what they
            // change is which lamps the daylight had, not the time of day, and is snapped to rather than
            // watched. A player changing the setting is a change they asked to see, and is not.
            if (shelterChanged)
            {
                cells.Unsettle();
            }
        }

        // A level put back to its opening hour opens with its lamps already as that hour has them.
        if (dayNight.LevelEpoch != _lampDaylightEpoch)
        {
            _lampDaylightEpoch = dayNight.LevelEpoch;
            cells.Unsettle();
        }

        float light = dayNight.SunSine(sun) - (LampCloudDarkening * cloudCover);
        cells.Update(elapsed, light);
    }

    /// <summary>How far the enclosure test's rays reach, in metres: past that, a ray has left the room.</summary>
    /// <remarks>Wider than a barrack hall, narrower than most yards between buildings.</remarks>
    private const double LampEnclosureReach = 20.0;

    /// <summary>How many of the enclosure test's rays may get out with the lamp still counted as indoors.</summary>
    /// <remarks>An open door or two; an open side is a quarter of them or more.</remarks>
    private const int LampEnclosureLeaks = 3;

    /// <summary>How many directions round the lamp the enclosure test casts in.</summary>
    private const int LampEnclosureAzimuths = 16;

    /// <summary>How far below the fitting the enclosure test's rays start, in metres.</summary>
    /// <remarks>Clear of the ceiling or canopy the lamp hangs from, which a level ray would graze.</remarks>
    private const double LampEnclosureDrop = 0.5;

    /// <summary>Whether a lamp is inside a building, and so one the building keeps lit by day.</summary>
    /// <param name="fixture">The lamp.</param>
    /// <param name="shelter">The roofs over the level.</param>
    /// <param name="walls">The world's caster, when it is up.</param>
    /// <returns><see langword="true"/> under a roof AND closed in round it.</returns>
    /// <remarks>
    /// <para>
    /// A roof alone is not a building. A walkway's canopy, a porch, the eave a wall lamp hangs under all
    /// have one, and the roof test kept every lamp under them burning at noon -- most of a camp's outdoor
    /// lights. What a room has that a canopy has not is walls: level rays from just under the lamp, all the
    /// way round, meet a wall inside a room and pass out from under a canopy. Glass and chain-link are
    /// seen through, as a line of sight sees through them: a fenced yard is not a room.
    /// </para>
    /// <para>
    /// Where the world has no collision at all under the lamp -- a space drawn but not made solid -- the
    /// rays would leave every room, and the roof alone answers.
    /// </para>
    /// </remarks>
    public static bool IsIndoors(in LightFixture fixture, ShelterHeightField shelter, WorldRayCast? walls)
    {
        if (!IsUnderRoof(fixture, shelter))
        {
            return false;
        }

        if (walls is null)
        {
            return true;
        }

        Vector3d origin = fixture.Position - new Vector3d(0.0, 0.0, LampEnclosureDrop);
        if (!SolidAlong(walls, origin, origin - new Vector3d(0.0, 0.0, 15.0)))
        {
            return true;
        }

        int leaks = 0;
        for (int k = 0; k < LampEnclosureAzimuths; k++)
        {
            double azimuth = (k + 0.5) * (2.0 * Math.PI / LampEnclosureAzimuths);
            Vector3d direction = new(Math.Cos(azimuth), Math.Sin(azimuth), 0.0);
            if (!SolidAlong(walls, origin, origin + (direction * LampEnclosureReach)) && ++leaks > LampEnclosureLeaks)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether something solid stands between two points, seeing through glass and chain-link.</summary>
    /// <param name="walls">The world's caster.</param>
    /// <param name="from">The start, in metres.</param>
    /// <param name="to">The end, in metres.</param>
    /// <returns><see langword="true"/> when an opaque surface lies between them.</returns>
    private static bool SolidAlong(WorldRayCast walls, Vector3d from, Vector3d to)
    {
        Vector3d start = WorldScene.ToUnits(from);
        Vector3d end = WorldScene.ToUnits(to);
        for (int pass = 0; pass < 8; pass++)
        {
            if (!walls(start, end, out WorldRayHit hit))
            {
                return false;
            }

            if (!GameMaterial.IsSightTransparent(hit.Material))
            {
                return true;
            }

            Vector3d onward = end - hit.Position;
            double length = onward.Length;
            if (length <= 1e-6)
            {
                return false;
            }

            // A few units past the see-through face (4/4096 m, about a millimetre).
            start = hit.Position + (onward * (4.0 / length));
        }

        return false;
    }

    /// <summary>Whether a lamp has a roof over it.</summary>
    /// <param name="fixture">The lamp.</param>
    /// <param name="shelter">The roofs over the level.</param>
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
    public static bool IsUnderRoof(in LightFixture fixture, ShelterHeightField shelter)
    {
        if (shelter.IsEmpty)
        {
            return true;
        }

        const double headroom = 0.05;
        Vector3d at = fixture.Position;
        int covered = shelter.IsSheltered(at.X, at.Y, at.Z, headroom) ? 1 : 0;

        for (int k = 0; k < 8; k++)
        {
            double angle = k * (Math.PI / 4.0);
            double x = at.X + (LampRoofRing * Math.Cos(angle));
            double y = at.Y + (LampRoofRing * Math.Sin(angle));

            if (shelter.IsSheltered(x, y, at.Z, headroom))
            {
                covered++;
            }
        }

        return covered >= 6;
    }
}
