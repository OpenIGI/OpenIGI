namespace OpenIGI.Game.Scoring;

/// <summary>How a mission is being played, as its title hands it over: the player's score so far and the mission's requirements.</summary>
/// <param name="Score">The score.</param>
/// <param name="Requirements">What each rank asks of the mission.</param>
public sealed record MissionScoreSheet(MissionScore Score, MissionRankRequirements Requirements);

/// <summary>
/// A mission's rank for any title, I.G.I. 2's <c>MissionScoreSettings</c> rules: the score screen at its end, the best a profile
/// keeps of it, and which of two kept bests is the better. A title only says how its player has played and what its mission asks
/// (<see cref="MissionScoreSheet"/>).
/// </summary>
public static class MissionScoring
{
    /// <summary>Scores a mission just completed: its card, and the record to keep where it improves on the kept one.</summary>
    /// <param name="sheet">How it was played, and what it asks.</param>
    /// <param name="best">The best kept for it, or <see langword="null"/> for none.</param>
    /// <param name="words">The words the card is told in.</param>
    /// <returns>The score.</returns>
    /// <remarks>
    /// <c>Game_CreateMissionScoreScreen</c> makes the screen; <c>MissionScoreSettings_UpdateMissionStats</c> keeps the score where
    /// it improves on the kept one by this mission's requirements (<see cref="MissionRankRequirements.Improves"/>).
    /// </remarks>
    public static MissionScoreResult Score(MissionScoreSheet sheet, TitleMissionRecord? best, MissionScoreWords words)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        MissionScore? kept = MissionScoreRecord.TryDecode(best, out MissionScore score, out _) ? score : null;
        TitleMissionRecord? record = sheet.Requirements.Improves(sheet.Score, kept) ? MissionScoreRecord.Encode(sheet.Score, sheet.Requirements) : null;
        return new MissionScoreResult(MissionScoreCardBuilder.Build(sheet.Requirements, sheet.Score, words), record);
    }

    /// <summary>
    /// Scores a co-op squad for one of its players: the squad's one card -- its rank reckoned from what holds for all of it
    /// (<see cref="SquadScore.Of"/>) -- with each player's part in the table under it, this player's marked; and the squad's score as
    /// the record this player keeps, where it improves on theirs. <b>Not retail</b>, as <see cref="SquadScore"/>.
    /// </summary>
    /// <param name="squad">The squad's score, as the server told it.</param>
    /// <param name="ownEntity">The player the card is for, or <see langword="null"/> for nobody's (a dedicated server's).</param>
    /// <param name="best">The best the player keeps for the mission, or <see langword="null"/> for none.</param>
    /// <param name="words">The words the card is told in.</param>
    /// <returns>The score, or <see langword="null"/> where the player is not one of the squad.</returns>
    public static MissionScoreResult? ForSquadMember(SquadScore squad, int? ownEntity, TitleMissionRecord? best, MissionScoreWords words)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(words);
        if (!squad.Members.Any(member => member.Entity == ownEntity))
        {
            return null;
        }

        MissionScoreResult result = Score(new MissionScoreSheet(squad.Squad, squad.Requirements), best, words);
        MissionSquadLine[] lines =
        [
            .. squad.Members.Select(member => new MissionSquadLine(
                member.Name,
                member.Score.TimesSpotted,
                MissionScoreCardBuilder.HealthText(member.Score),
                MissionScoreCardBuilder.AccuracyText(member.Score, words),
                member.Score.Kills,
                member.Entity == ownEntity)),
        ];
        return result with { Card = result.Card with { Squad = lines } };
    }

    /// <summary>
    /// The record a mission is kept by from the moment it is first played, as <c>Game_LoadHandler</c> keeps one through
    /// <c>MissionScoreSettings_UpdateMissionStats</c>: nothing played yet, beside the mission's requirements.
    /// </summary>
    /// <param name="sheet">The mission as it begins.</param>
    /// <returns>The record.</returns>
    public static TitleMissionRecord FreshRecord(MissionScoreSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return MissionScoreRecord.Encode(MissionScore.Fresh(sheet.Score.MissionId, sheet.Score.Difficulty), sheet.Requirements);
    }

    /// <summary>
    /// The card of a kept best, by the requirements kept beside it, as the menu's stats screen shows one
    /// (<c>MenuStatsScreen_CreateMissionScoreScreen</c>).
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="words">The words the card is told in.</param>
    /// <returns>The card, or <see langword="null"/> for a record that cannot be read.</returns>
    public static MissionScoreCard? CardFor(TitleMissionRecord? record, MissionScoreWords words) =>
        MissionScoreRecord.TryDecode(record, out MissionScore score, out MissionRankRequirements? requirements)
            ? MissionScoreCardBuilder.Build(requirements, score, words)
            : null;

    /// <summary>
    /// Which of two bests of one mission to keep -- this computer's or the account's: the account's, unless this computer's
    /// improves on it by its own requirements; a record that cannot be read gives way to one that can.
    /// </summary>
    /// <param name="local">This computer's.</param>
    /// <param name="remote">The account's.</param>
    /// <returns>The one to keep.</returns>
    public static TitleMissionRecord Prefer(TitleMissionRecord local, TitleMissionRecord remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        if (!MissionScoreRecord.TryDecode(local, out MissionScore mine, out MissionRankRequirements? requirements))
        {
            return remote;
        }

        if (!MissionScoreRecord.TryDecode(remote, out MissionScore theirs, out _))
        {
            return local;
        }

        return requirements.Improves(mine, theirs) ? local : remote;
    }
}
