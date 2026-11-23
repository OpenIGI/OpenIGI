using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The mission's score, which its rank is reckoned from.</content>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>Gets how the player has played the mission so far, <c>Human.tMissionScoreData</c>.</summary>
    /// <param name="missionId">The mission.</param>
    /// <returns>The score.</returns>
    /// <remarks>
    /// Its difficulty is the one the mission is played at (<c>MissionScoreSettings_Init</c> reads the profile's, which retail
    /// starts the mission at); the original's own configuration file may say another, which is not the game being played.
    /// </remarks>
    public MissionScore CurrentMissionScore(int missionId) => ScoreOf(Human, missionId);

    /// <summary>Gets how a player -- this game's own, or a co-op partner on the server -- has played, <c>Human.tMissionScoreData</c>.</summary>
    /// <param name="human">The player.</param>
    /// <param name="missionId">The mission.</param>
    /// <returns>The score.</returns>
    internal static MissionScore ScoreOf(Igi2HumanPlayer human, int missionId) =>
        new((uint)Math.Max(0, human.MissionTime), human.TimesSpotted, human.MissionHealth, (uint)Math.Max(0, human.MissionShots), (uint)Math.Max(0, human.MissionHits))
        {
            Difficulty = (int)human.Difficulty,
            MissionId = missionId,
            GameLoads = human.GameLoads,
            Kills = human.MissionKills,
            TimesAlarmTriggered = human.TimesAlarmTriggered,
        };

    /// <inheritdoc/>
    /// <remarks>
    /// Only where the level has its <c>MissionScoreSettings</c>, as <c>Game_CreateMissionScoreScreen</c> makes the screen only then:
    /// the player's <c>tMissionScoreData</c> against its requirements.
    /// </remarks>
    public MissionScoreSheet? ScoreSheet(int missionId) =>
        LevelTasks().OfType<Igi2MissionScoreSettingsTask>().FirstOrDefault() is { } settings
            ? new MissionScoreSheet(CurrentMissionScore(missionId), settings.Requirements)
            : null;
}
