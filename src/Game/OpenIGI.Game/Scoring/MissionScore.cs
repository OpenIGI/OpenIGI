namespace OpenIGI.Game.Scoring;

/// <summary>
/// How a mission was played: what its rank is reckoned from. I.G.I. 2's <c>HumanMissionScoreData_s</c>, which Project I.G.I.'s
/// player keeps as well.
/// </summary>
/// <param name="Ticks">The ticks it took, <c>nTime</c>.</param>
/// <param name="TimesSpotted">The times the player was spotted, <c>nSpotted</c>.</param>
/// <param name="Health">The health left, in per cent, <c>nHealth</c>.</param>
/// <param name="Shots">The rounds fired, <c>nShots</c>.</param>
/// <param name="Hits">The rounds that hit, <c>nHits</c>.</param>
public readonly record struct MissionScore(uint Ticks, int TimesSpotted, int Health, uint Shots, uint Hits)
{
    /// <summary>The ticks in a second of play.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>Gets the difficulty it was played at, <c>eGameDifficulty</c>: nought easy to two hard.</summary>
    public int Difficulty { get; init; }

    /// <summary>Gets the mission, <c>iMissionID</c>.</summary>
    public int MissionId { get; init; }

    /// <summary>Gets the save games loaded on the way, <c>nGameLoads</c>.</summary>
    public int GameLoads { get; init; }

    /// <summary>Gets the enemies killed, <c>nKills</c>.</summary>
    public int Kills { get; init; }

    /// <summary>Gets the alarms set off, <c>nTimesAlarmTriggered</c>.</summary>
    public int TimesAlarmTriggered { get; init; }

    /// <summary>Gets the hits per hundred rounds, at most a hundred, or <see langword="null"/> with none fired.</summary>
    public int? Accuracy => Shots == 0 ? null : (int)Math.Min(100u, 100u * Hits / Shots);

    /// <summary>
    /// A score with nothing played yet, as <c>MissionScoreSettings_Init</c> starts a mission's: every count nought.
    /// </summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="difficulty">The difficulty it is played at.</param>
    /// <returns>The score.</returns>
    public static MissionScore Fresh(int missionId, int difficulty) =>
        new(0, 0, 0, 0, 0) { MissionId = missionId, Difficulty = difficulty };
}
