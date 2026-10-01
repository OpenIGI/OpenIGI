namespace OpenIGI.Rendering;

/// <summary>
/// The conditions the campaign is being played under: what hour missions open at, and what falls out of
/// the sky while they are played.
/// </summary>
/// <remarks>
/// <para>
/// <b>They belong to the campaign, not to a mission.</b> Chosen beside the difficulty on the campaign
/// screen and carried from there into every mission that follows, so picking night for mission one means
/// the whole run after it is at night as well — the same way the difficulty already behaves. A player
/// who wants one mission in the dark and the next in daylight says so on the campaign screen again;
/// nothing resets the choice behind their back.
/// </para>
/// <para>
/// <b>Session-scoped on purpose.</b> Nothing here is written to the settings file. An hour that outlives
/// the session is how every mission afterwards silently opens at night with nobody remembering asking
/// for it, which reads as the game being broken rather than as a preference — see the remarks on
/// <c>r_time_of_day</c>, which was made non-archived for exactly that reason.
/// </para>
/// </remarks>
/// <param name="TimeOfDay">The hour missions open at.</param>
/// <param name="Weather">The weather missions are played in.</param>
public readonly record struct MissionConditions(MissionTimeOfDay TimeOfDay, MissionWeather Weather)
{
    /// <summary>The hour the sun comes up: the arc's own sunrise.</summary>
    /// <remarks>
    /// Elevation crosses zero here — <c>((hour - 6) / 12) * pi</c> is the phase the sun's height is a sine
    /// of — so this is the boundary between a level lit by the moon and a level lit by the sun.
    /// </remarks>
    public const float SunriseHour = 6f;

    /// <summary>The hour the sun goes down.</summary>
    /// <remarks>Twelve hours on from <see cref="SunriseHour"/>, which is the same arc read the other way.</remarks>
    public const float SunsetHour = 18f;

    /// <summary>How far past its horizon crossing each choice actually opens, in hours.</summary>
    /// <remarks>
    /// <para>
    /// <b>Neither choice is any good at the crossing itself.</b> At the exact hour the sun touches the
    /// horizon it is half a sun: the sky is part lit, the moon has not risen, and the level reads as the
    /// same ambiguous half-light whichever side of the day it is on — a "night" mission that looks like a
    /// late afternoon and a "day" mission that is a wall of orange.
    /// </para>
    /// <para>
    /// An hour either way is enough to be unambiguous, and it is a narrow window: the sun's warmth is
    /// faded across the first 0.3 of its height, which an hour of arc very nearly clears, so 7 is ordinary
    /// morning light where 6 is the reddest moment of the whole day. Past the crossing the other way, the
    /// sun is properly under the world, the moon has taken the directional slot and the sky fill has
    /// reached its night blue.
    /// </para>
    /// </remarks>
    public const float HorizonOffsetHours = 1f;

    /// <summary>The conditions a fresh campaign runs under: each level's own hour and its own weather.</summary>
    public static MissionConditions Default => new(MissionTimeOfDay.Default, MissionWeather.Default);

    /// <summary>Gets the hour missions open at, or a negative value for the level's own authored hour.</summary>
    /// <remarks>
    /// Shaped to go straight into <c>r_time_of_day</c>, whose own contract is "an hour, or negative for
    /// the level's own" — so the choice is applied by assigning one variable and nothing downstream of it
    /// needs to know the campaign screen exists.
    /// </remarks>
    public float StartHour => TimeOfDay switch
    {
        MissionTimeOfDay.Day => SunriseHour + HorizonOffsetHours,
        MissionTimeOfDay.Night => SunsetHour + HorizonOffsetHours,
        _ => -1f,
    };

    /// <summary>Gets the weather style to render, given the one the player's own settings ask for.</summary>
    /// <param name="preference">The style <c>r_weather_kind</c> is set to.</param>
    /// <returns>The style to fall, which is the preference unless the campaign has overridden it.</returns>
    public WeatherKind WeatherStyle(WeatherKind preference) => Weather switch
    {
        MissionWeather.Rain => WeatherKind.Rain,
        MissionWeather.Snow => WeatherKind.Snow,
        _ => preference,
    };

    /// <summary>Gets whether the weather falls in levels whose own data authors none.</summary>
    /// <remarks>
    /// A campaign-wide choice of rain or snow has to be forced, or it would only be honoured in the three
    /// missions that already have weather and would look like a setting that does nothing everywhere else.
    /// <see cref="MissionWeather.Default"/> forces nothing and leaves the answer to the level.
    /// </remarks>
    public bool ForcesWeather => Weather != MissionWeather.Default;
}
