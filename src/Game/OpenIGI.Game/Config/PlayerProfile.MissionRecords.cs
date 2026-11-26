using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Config;

/// <content>The best the player has done in each mission of each game.</content>
public sealed partial class PlayerProfile
{
    // By game, then by mission: one profile plays every game in the build, and their missions' ids overlap.
    private readonly Dictionary<string, SortedDictionary<int, TitleMissionRecord>> _missionRecords = new(StringComparer.Ordinal);

    /// <summary>Gets the records kept, by game, then by mission.</summary>
    public IEnumerable<(string TitleId, int MissionId, TitleMissionRecord Record)> MissionRecords =>
        _missionRecords
            .OrderBy(static title => title.Key, StringComparer.Ordinal)
            .SelectMany(static title => title.Value.Select(mission => (title.Key, mission.Key, mission.Value)));

    /// <summary>Gets the best kept for a mission.</summary>
    /// <param name="titleId">The game, by title id.</param>
    /// <param name="missionId">The mission.</param>
    /// <returns>The record, or <see langword="null"/> where none is kept.</returns>
    public TitleMissionRecord? MissionRecordOf(string titleId, int missionId) =>
        _missionRecords.TryGetValue(titleId, out SortedDictionary<int, TitleMissionRecord>? missions)
        && missions.TryGetValue(missionId, out TitleMissionRecord? record)
            ? record
            : null;

    /// <summary>Keeps a mission's record in place of any kept before.</summary>
    /// <param name="titleId">The game, by title id.</param>
    /// <param name="missionId">The mission.</param>
    /// <param name="record">The record.</param>
    public void SetMissionRecord(string titleId, int missionId, TitleMissionRecord record)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        ArgumentNullException.ThrowIfNull(record);
        if (!_missionRecords.TryGetValue(titleId, out SortedDictionary<int, TitleMissionRecord>? missions))
        {
            missions = [];
            _missionRecords[titleId] = missions;
        }

        missions[missionId] = record;
    }
}
