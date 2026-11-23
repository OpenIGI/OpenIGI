namespace OpenIGI.Game.Scoring;

/// <summary>One co-op player's part in the squad's score, as the server reckons it.</summary>
/// <param name="Entity">The player's entity in the session.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Score">How they played the mission.</param>
public sealed record SquadScoreMember(int Entity, string Name, MissionScore Score);

/// <summary>
/// A co-op mission's score at its end, as the server tells every player: what its ranks ask, the squad's one score, every
/// player's part in it, and how long is left before the squad goes on by itself. <b>Not retail</b>: I.G.I. 2 ranks
/// single-player missions only; a co-op squad is ranked as one, by the same rules (<see cref="MissionScoring"/>), on figures that
/// hold for all of it.
/// </summary>
/// <param name="Requirements">What the mission's ranks ask.</param>
/// <param name="Squad">The squad's one score, which its rank is reckoned from.</param>
/// <param name="Members">Every player's part in it.</param>
/// <param name="SecondsLeft">The whole seconds before the squad goes on to what follows, as the server counts them.</param>
public sealed record SquadScore(MissionRankRequirements Requirements, MissionScore Squad, IReadOnlyList<SquadScoreMember> Members, int SecondsLeft)
{
    /// <summary>The seconds a co-op squad's score stays up before it goes on by itself.</summary>
    public const int AutoAdvanceSeconds = 10;

    /// <summary>
    /// Scores a squad as one: the requirements must hold for all of it.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The time is the squad's: the longest clock among the host's player and the squad's, which is the mission's own from its
    /// first player's start -- a player who joined late takes no less, and a dedicated server's nobody counts too.</item>
    /// <item>The times spotted are every sighting of any of them, so an unseen squad is one none of whose players was seen; a world
    /// that counts sightings for the squad as a whole says so instead (<paramref name="squadSightings"/>).</item>
    /// <item>The health is the lowest any player ends with.</item>
    /// <item>The accuracy is the squad's hits over its rounds, all of them pooled: one player's stray round weighs as one round.</item>
    /// <item>The kills are the squad's together; the loads and alarms the most any player had.</item>
    /// </list>
    /// </remarks>
    /// <param name="sheet">The mission as the host's player played it: the squad's clock, difficulty and requirements.</param>
    /// <param name="players">Every player's own score, with their entity and name.</param>
    /// <param name="squadSightings">
    /// The times the squad as a whole was spotted, where the world counts sightings so (Project I.G.I.'s soldiers fight every player
    /// as one target); <see langword="null"/> to add up each player's.
    /// </param>
    /// <param name="secondsLeft">The seconds before the squad goes on.</param>
    /// <returns>The squad's score.</returns>
    public static SquadScore Of(MissionScoreSheet sheet, IEnumerable<SquadScoreMember> players, int? squadSightings = null, int secondsLeft = AutoAdvanceSeconds)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(players);
        MissionScore host = sheet.Score;
        SquadScoreMember[] played = [.. players];
        uint ticks = played.Select(static member => member.Score.Ticks).Append(host.Ticks).Max();
        SquadScoreMember[] members =
        [
            .. played.Select(member => member with
            {
                Score = member.Score with { Ticks = ticks, Difficulty = host.Difficulty, MissionId = host.MissionId },
            }),
        ];

        MissionScore squad = members.Length == 0
            ? host with { Ticks = ticks }
            : new MissionScore(
                ticks,
                squadSightings ?? members.Sum(static m => m.Score.TimesSpotted),
                members.Min(static m => m.Score.Health),
                (uint)members.Sum(static m => (long)m.Score.Shots),
                (uint)members.Sum(static m => (long)m.Score.Hits))
            {
                Difficulty = host.Difficulty,
                MissionId = host.MissionId,
                Kills = members.Sum(static m => m.Score.Kills),
                GameLoads = members.Max(static m => m.Score.GameLoads),
                TimesAlarmTriggered = members.Max(static m => m.Score.TimesAlarmTriggered),
            };
        return new SquadScore(sheet.Requirements, squad, members, secondsLeft);
    }
}
