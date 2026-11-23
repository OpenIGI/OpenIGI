namespace OpenIGI.Game.Scoring;

/// <summary>
/// How a completed mission was played and the rank it earned, as the screen at its end shows it: a column of what was
/// achieved beside a column of what the next rank asks, and the rank under them. A title's own words fill it; how it is
/// drawn is the host's.
/// </summary>
/// <param name="Title">The heading over it, as <c>MISSION STATS</c>.</param>
/// <param name="CategoryHeading">The head of the labels' column.</param>
/// <param name="AchievedHeading">The head of the achieved column.</param>
/// <param name="TargetHeading">The head of the next rank's column, or <see langword="null"/> where the top rank was earned.</param>
/// <param name="Lines">The figures, in order: one line each.</param>
/// <param name="RankLabel">The rank line's label.</param>
/// <param name="RankName">The rank earned, by its name.</param>
/// <param name="Rank">The rank earned, nought the lowest, or a negative number for none.</param>
/// <param name="RankCount">How many ranks there are to earn.</param>
/// <param name="IsCompleted">
/// Whether the mission was played to its end: <see langword="false"/> for a record kept from its first play, whose figures are
/// the nothing it started with.
/// </param>
/// <param name="Squad">
/// In a co-op mission, every player's part in the squad's score, this player's among them; empty for a mission played alone.
/// </param>
public sealed record MissionScoreCard(
    string Title,
    string CategoryHeading,
    string AchievedHeading,
    string? TargetHeading,
    IReadOnlyList<MissionScoreLine> Lines,
    string RankLabel,
    string RankName,
    int Rank,
    int RankCount,
    bool IsCompleted = true,
    IReadOnlyList<MissionSquadLine>? Squad = null)
{
    /// <summary>Gets every co-op player's part in the squad's score; empty for a mission played alone.</summary>
    public IReadOnlyList<MissionSquadLine> SquadLines => Squad ?? [];
}

/// <summary>One co-op player's part in the squad's score, as the table under it shows it: their figures, not a rank of their own.</summary>
/// <param name="Name">The player's name.</param>
/// <param name="Spotted">The times they were spotted.</param>
/// <param name="Health">Their health left, as the card says it.</param>
/// <param name="Accuracy">Their accuracy, as the card says it.</param>
/// <param name="Kills">The enemies they killed.</param>
/// <param name="IsOwn">Whether this is the player the card is shown to.</param>
public readonly record struct MissionSquadLine(string Name, int Spotted, string Health, string Accuracy, int Kills, bool IsOwn);

/// <summary>One figure of a mission's score.</summary>
/// <param name="Label">What it is, as <c>Time To Complete Mission:</c>.</param>
/// <param name="Achieved">What the player did.</param>
/// <param name="Met">Whether that meets the rank it is weighed against: the next one, or the top one once it is earned.</param>
/// <param name="Target">What the next rank asks, or <see langword="null"/> where there is none.</param>
public readonly record struct MissionScoreLine(string Label, string Achieved, bool Met, string? Target);

/// <summary>
/// The best a player has done in one mission, as their profile keeps it: a title's own figures, which only that title
/// reads -- I.G.I. 2's are its <c>GOPlayerMissionStats</c> record.
/// </summary>
/// <param name="Counts">The counts.</param>
/// <param name="Figures">The figures.</param>
public sealed record TitleMissionRecord(IReadOnlyList<int> Counts, IReadOnlyList<float> Figures);

/// <summary>A completed mission's score: its card, and the record the profile is to keep for it.</summary>
/// <param name="Card">What the screen at its end shows.</param>
/// <param name="Record">The record to keep in place of the one kept, or <see langword="null"/> to keep that one.</param>
public sealed record MissionScoreResult(MissionScoreCard Card, TitleMissionRecord? Record);
