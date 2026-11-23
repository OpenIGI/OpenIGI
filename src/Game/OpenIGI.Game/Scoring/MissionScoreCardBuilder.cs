using System.Globalization;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.Scoring;

/// <summary>
/// What a mission's score screen says, I.G.I. 2's <c>MissionScoreScreen_New</c> (<c>missionscorescreen.c</c>): its words and
/// figures, which the host's own sheet draws in place of retail's text tiles.
/// </summary>
/// <remarks>
/// <para>
/// Retail lays three columns of six lines under <c>MISSION STATS</c>: <c>Category</c>, <c>Achieved</c> and <c>Target</c>, then
/// the time to complete the mission, the times spotted, the health and the accuracy, and the rank. A figure the requirements do
/// not weigh (<see cref="MissionRankRequirements.Weighs"/>) is left out of every column. The achieved figures are white where
/// they meet the rank they are weighed against and red where they fall short (<c>MissionScoreSettings_GetRank</c>'s third
/// argument): the next rank's, or every one met once the top rank is earned. The target column is the next rank's
/// requirements, and stands empty once the top rank is earned. Loads are never shown, and never weighed.
/// </para>
/// <para>
/// The accuracy is the hits per hundred rounds, at most a hundred, or <c>Not Available</c> with none fired; a time is
/// <c>h:mm:ss</c> (<c>Game_GetTimeStringFromGameTicks</c>); the rank is named for the difficulty played.
/// </para>
/// </remarks>
public static class MissionScoreCardBuilder
{
    // The four figures' lines, each with the requirement it is weighed by and its label's key.
    private static readonly (int Item, string Label)[] Figures =
    [
        (MissionRankItem.Time, MissionScoreWords.Time),
        (MissionRankItem.Spotted, MissionScoreWords.Spotted),
        (MissionRankItem.Health, MissionScoreWords.Health),
        (MissionRankItem.Accuracy, MissionScoreWords.Accuracy),
    ];

    /// <summary>Builds the card for a mission as it was played.</summary>
    /// <param name="requirements">The mission's requirements.</param>
    /// <param name="score">How it was played.</param>
    /// <param name="words">The words it is told in.</param>
    /// <returns>The card.</returns>
    public static MissionScoreCard Build(MissionRankRequirements requirements, in MissionScore score, MissionScoreWords words)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(words);
        bool[] met = new bool[MissionRankItem.Count];
        int rank = requirements.GetRank(score, met);
        int next = rank >= MissionRankRequirements.RankCount - 1 ? -1 : rank + 1;

        List<MissionScoreLine> lines = [];
        foreach ((int item, string label) in Figures)
        {
            if (!requirements.Weighs(item))
            {
                continue;
            }

            string? target = next >= 0 ? Requirement(requirements[next, item], item) : null;
            lines.Add(new MissionScoreLine(words[label] + ":", Achieved(score, item, words), met[item], target));
        }

        return new MissionScoreCard(
            words[MissionScoreWords.Title].ToUpperInvariant(),
            words[MissionScoreWords.Category],
            words[MissionScoreWords.Achieved],
            next >= 0 ? words[MissionScoreWords.Target] : null,
            lines,
            words[MissionScoreWords.Rank] + ":",
            words.RankName(rank, score.Difficulty),
            rank,
            MissionRankRequirements.RankCount,
            IsCompleted: score.Ticks != 0);
    }

    /// <summary>The health left, as the card says it: a whole percentage.</summary>
    /// <param name="score">The score.</param>
    /// <returns>The text.</returns>
    public static string HealthText(in MissionScore score) => Percent(score.Health);

    /// <summary>The accuracy, as the card says it: a whole percentage, or <c>Not Available</c> with no round fired.</summary>
    /// <param name="score">The score.</param>
    /// <param name="words">The words it is told in.</param>
    /// <returns>The text.</returns>
    public static string AccuracyText(in MissionScore score, MissionScoreWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        return score.Accuracy is { } accuracy ? Percent(accuracy) : words[MissionScoreWords.NotAvailable];
    }

    // What the player did, as the achieved column says it.
    private static string Achieved(in MissionScore score, int item, MissionScoreWords words) => item switch
    {
        MissionRankItem.Time => SaveRules.MissionTime((int)score.Ticks),
        MissionRankItem.Spotted => score.TimesSpotted.ToString(CultureInfo.InvariantCulture),
        MissionRankItem.Health => HealthText(score),
        _ => AccuracyText(score, words),
    };

    // What a rank asks, as the target column says it: the seconds as a time, the rest whole.
    private static string Requirement(float required, int item) => item switch
    {
        MissionRankItem.Time => SaveRules.MissionTime((int)(MissionScore.TicksPerSecond * (long)required)),
        MissionRankItem.Spotted => ((uint)(long)required).ToString(CultureInfo.InvariantCulture),
        _ => Percent((int)(long)required),
    };

    private static string Percent(int value) => value.ToString(CultureInfo.InvariantCulture) + "%";
}
