namespace OpenIGI.Rendering;

/// <summary>The hour a mission is played at, as the campaign screen offers it.</summary>
/// <remarks>
/// Three choices rather than a slider, because this is a way to play the game and not a lighting tool.
/// The slider already exists — <c>r_time_of_day</c> on the graphics page — and what it is good for is
/// looking at a level; what a player wants from the campaign is "give me this mission in the dark".
/// </remarks>
public enum MissionTimeOfDay
{
    /// <summary>The hour the level's own authors lit it at.</summary>
    /// <remarks>
    /// The only choice that reproduces the authored sun exactly: the day/night arc is fitted through the
    /// level's own hour and lands back on the authored direction only there.
    /// </remarks>
    Default,

    /// <summary>Morning: the mission opens with the sun newly up.</summary>
    /// <remarks>An hour past sunrise, not at it — see <see cref="MissionConditions.HorizonOffsetHours"/>.</remarks>
    Day,

    /// <summary>After dark: the mission opens under a risen moon.</summary>
    Night,
}
