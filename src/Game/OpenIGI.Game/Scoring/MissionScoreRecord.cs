using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Game.Scoring;

/// <summary>
/// A mission's score and its ranks' requirements as a profile keeps them (<see cref="TitleMissionRecord"/>), in the order I.G.I. 2's
/// <c>GOPlayerMissionStats</c> writes its twenty-five values (<c>Config_ParsePlayerMissionData</c>): the mission, the difficulty,
/// the loads, the health, the hits, the kills, the shots, the times spotted, the ticks and the alarms, then the requirements rank
/// by rank (<c>Config_UpdateMissionStats</c> copies them beside the score). Both games keep theirs so.
/// </summary>
public static class MissionScoreRecord
{
    /// <summary>How many counts a record holds.</summary>
    public const int CountCount = 10;

    /// <summary>How many figures a record holds.</summary>
    public const int FigureCount = MissionRankRequirements.RankCount * MissionRankItem.Count;

    /// <summary>Writes a score and the requirements it was weighed against as a record.</summary>
    /// <param name="score">The score.</param>
    /// <param name="requirements">The requirements.</param>
    /// <returns>The record.</returns>
    public static TitleMissionRecord Encode(in MissionScore score, MissionRankRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        int[] counts =
        [
            score.MissionId, score.Difficulty, score.GameLoads, score.Health, (int)score.Hits,
            score.Kills, (int)score.Shots, score.TimesSpotted, (int)score.Ticks, score.TimesAlarmTriggered,
        ];
        float[] figures = new float[FigureCount];
        for (int rank = 0; rank < MissionRankRequirements.RankCount; rank++)
        {
            for (int item = 0; item < MissionRankItem.Count; item++)
            {
                figures[(rank * MissionRankItem.Count) + item] = requirements[rank, item];
            }
        }

        return new TitleMissionRecord(counts, figures);
    }

    /// <summary>Reads a record back into its score and requirements.</summary>
    /// <param name="record">The record.</param>
    /// <param name="score">Receives the score.</param>
    /// <param name="requirements">Receives the requirements.</param>
    /// <returns>Whether the record has a score's shape.</returns>
    public static bool TryDecode(TitleMissionRecord? record, out MissionScore score, [NotNullWhen(true)] out MissionRankRequirements? requirements)
    {
        score = default;
        requirements = null;
        return record is not null && TryReadScore(record.Counts, out score) && TryReadRequirements(record.Figures, out requirements);
    }

    /// <summary>Reads a score from a record's counts.</summary>
    /// <param name="counts">The counts, in the record's order.</param>
    /// <param name="score">Receives the score.</param>
    /// <returns>Whether there are as many counts as a record holds, none below nought.</returns>
    public static bool TryReadScore(IReadOnlyList<int>? counts, out MissionScore score)
    {
        score = default;
        if (counts is not { Count: CountCount } c || c.Any(static n => n < 0))
        {
            return false;
        }

        score = new MissionScore((uint)c[8], c[7], c[3], (uint)c[6], (uint)c[4])
        {
            MissionId = c[0],
            Difficulty = c[1],
            GameLoads = c[2],
            Kills = c[5],
            TimesAlarmTriggered = c[9],
        };
        return true;
    }

    /// <summary>Reads requirements from a record's figures.</summary>
    /// <param name="figures">The figures, rank by rank.</param>
    /// <param name="requirements">Receives the requirements.</param>
    /// <returns>Whether there are as many figures as a record holds.</returns>
    public static bool TryReadRequirements(IReadOnlyList<float>? figures, [NotNullWhen(true)] out MissionRankRequirements? requirements)
    {
        requirements = null;
        if (figures is not { Count: FigureCount } f)
        {
            return false;
        }

        requirements = new MissionRankRequirements();
        for (int rank = 0; rank < MissionRankRequirements.RankCount; rank++)
        {
            for (int item = 0; item < MissionRankItem.Count; item++)
            {
                requirements[rank, item] = f[(rank * MissionRankItem.Count) + item];
            }
        }

        return true;
    }
}
