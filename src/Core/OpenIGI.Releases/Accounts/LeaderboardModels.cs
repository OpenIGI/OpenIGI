using System.Globalization;

namespace OpenIGI.Releases.Accounts;

/// <summary>The Discord account a board's row is gathered under.</summary>
/// <param name="Id">The account's id, the player's Discord user id.</param>
/// <param name="Name">The name shown, masked where it holds a slur.</param>
/// <param name="Avatar">The avatar's address, or <see langword="null"/>.</param>
public sealed record BoardAccount(string Id, string Name, string? Avatar = null);

/// <summary>One player's row on a game's leaderboard.</summary>
/// <param name="Rank">Their place, from 1.</param>
/// <param name="Player">Who: the account's id where the row is a linked account's, else the key's.</param>
/// <param name="Account">The linked account, or <see langword="null"/> for a key on its own.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Matches">The matches played.</param>
/// <param name="Wins">The matches won.</param>
/// <param name="Kills">The kills.</param>
/// <param name="Deaths">The deaths.</param>
/// <param name="Assists">The assists.</param>
/// <param name="Objectives">The objectives won.</param>
/// <param name="Seconds">The time played.</param>
/// <param name="Score">The points.</param>
/// <param name="Kd">Kills a death, to two places.</param>
public sealed record BoardRow(
    int Rank,
    string Player,
    BoardAccount? Account,
    string Name,
    int Matches,
    int Wins,
    int Kills,
    int Deaths,
    int Assists = 0,
    int Objectives = 0,
    int Seconds = 0,
    int Score = 0,
    double Kd = 0)
{
    /// <summary>Gets the share of matches won, nought to one.</summary>
    public double WinRate => Matches > 0 ? (double)Wins / Matches : 0;

    /// <summary>Gets the id the row's record is read by: the account's where it is linked, else the key's.</summary>
    public string RecordId => Account?.Id ?? Player;
}

/// <summary>A game's leaderboard in a period, ranked by a metric.</summary>
/// <param name="Title">The game, by title id.</param>
/// <param name="Period"><c>season</c> or <c>all</c>.</param>
/// <param name="Season">The season shown (<c>yyyy-MM</c>), or <c>all</c>.</param>
/// <param name="Metric">The metric ranked by, as <see cref="LeaderboardMetric.Id"/> names it.</param>
/// <param name="Rows">The best fifty, best first.</param>
/// <param name="You">The player asked about, wherever they stand; <see langword="null"/> unranked or not asked.</param>
/// <param name="Players">How many are ranked; nought from a master too old to say.</param>
/// <param name="Seasons">The seasons the game has had, newest (this one) first; <see langword="null"/> from an older master.</param>
public sealed record Leaderboard(
    string Title,
    string Period,
    string Season,
    string Metric,
    IReadOnlyList<BoardRow> Rows,
    BoardRow? You = null,
    int Players = 0,
    IReadOnlyList<string>? Seasons = null);

/// <summary>One player's line in a kept match, as their record shows it.</summary>
/// <param name="Player">The key they played under, or <see langword="null"/>.</param>
/// <param name="Name">The name they played under.</param>
/// <param name="Team">Their team, or their slot in a free-for-all.</param>
/// <param name="Kills">The kills.</param>
/// <param name="Deaths">The deaths.</param>
/// <param name="Assists">The assists.</param>
/// <param name="Objectives">The objectives won.</param>
/// <param name="Score">The points the match gave them.</param>
/// <param name="Won">Whether their side won.</param>
public sealed record RecordLine(string? Player, string Name, int Team, int Kills, int Deaths, int Assists = 0, int Objectives = 0, int Score = 0, bool Won = false);

/// <summary>A kept match a record lists among its latest.</summary>
/// <param name="Id">The match's id.</param>
/// <param name="At">When it was counted, in Unix milliseconds.</param>
/// <param name="Server">The official server it was played on, by name.</param>
/// <param name="Mode">The mode, such as <c>tdm</c>.</param>
/// <param name="Map">The map, as the server names it (a mission's number for a retail map).</param>
/// <param name="Seconds">How long it lasted.</param>
/// <param name="Winner">The winning team, or -1 for a draw.</param>
/// <param name="You">The record's own lines in it (one, or more where an account played on several keys).</param>
public sealed record RecordMatch(string Id, long At, string? Server, string Mode, string Map, int Seconds, int Winner, IReadOnlyList<RecordLine>? You = null)
{
    /// <summary>Gets the record's line in it, or <see langword="null"/>.</summary>
    public RecordLine? Line => You is { Count: > 0 } lines ? lines[0] : null;
}

/// <summary>A metric a leaderboard ranks by: its id as the master names it, and its name as a page says it.</summary>
/// <param name="Id">The id, such as <c>kd</c>.</param>
/// <param name="Label">The name, such as <c>K/D</c>.</param>
public sealed record LeaderboardMetric(string Id, string Label)
{
    /// <summary>The metrics, in the order a page offers them (as openigi.com's leaderboard does).</summary>
    public static readonly IReadOnlyList<LeaderboardMetric> All =
    [
        new("score", "Score"),
        new("kills", "Kills"),
        new("assists", "Assists"),
        new("kd", "K/D"),
        new("wins", "Wins"),
        new("matches", "Matches"),
    ];

    /// <summary>Gets the metric a board ranks by when it opens.</summary>
    public static LeaderboardMetric Default => All[0];

    /// <summary>A metric by its id, or <see cref="Default"/>.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The metric.</returns>
    public static LeaderboardMetric Of(string? id) => All.FirstOrDefault(metric => metric.Id == id) ?? Default;

    /// <summary>A row's figure in this metric, as said: <c>12,480</c>, or <c>2.35</c> for K/D.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The figure.</returns>
    public string Figure(BoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Id switch
        {
            "kd" => row.Kd.ToString("0.00", CultureInfo.InvariantCulture),
            "kills" => Count(row.Kills),
            "assists" => Count(row.Assists),
            "wins" => Count(row.Wins),
            "matches" => Count(row.Matches),
            _ => Count(row.Score),
        };
    }

    /// <summary>A count as said: <c>12,480</c>.</summary>
    /// <param name="value">The count.</param>
    /// <returns>The text.</returns>
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>The seasons the official servers count by: a calendar month, UTC.</summary>
public static class LeaderboardSeasons
{
    /// <summary>The period a board asks for this season.</summary>
    public const string Current = "season";

    /// <summary>The period a board asks for all time.</summary>
    public const string AllTime = "all";

    /// <summary>Gets whether a period is a named season gone by (<c>yyyy-MM</c>).</summary>
    /// <param name="period">The period.</param>
    /// <returns><see langword="true"/> for a month.</returns>
    public static bool IsMonth(string? period) =>
        period is { Length: 7 } && DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>A season as said: <c>2026-11</c> is <c>November 2026</c>; anything else as it is.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The name.</returns>
    public static string Name(string season) =>
        DateTime.TryParseExact(season, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime month)
            ? month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            : season;

    /// <summary>The days left in this season, the day it is counted on included.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>At least one.</returns>
    public static int DaysLeft(DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        var end = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        return Math.Max(1, (int)Math.Ceiling((end - utc).TotalDays));
    }
}

/// <summary>The leaderboard's pages on openigi.com, for a board or a record to be opened (and shared) in a browser.</summary>
public static class LeaderboardLinks
{
    /// <summary>The site's leaderboard.</summary>
    public const string Site = "https://openigi.com/leaderboard/";

    /// <summary>A game's board on the site.</summary>
    /// <param name="title">The game, by title id.</param>
    /// <returns>The address.</returns>
    public static string Board(string title) => $"{Site}?game={Uri.EscapeDataString(title)}";

    /// <summary>A player's record in a game on the site.</summary>
    /// <param name="player">Their account's id or key's.</param>
    /// <param name="title">The game, by title id.</param>
    /// <returns>The address.</returns>
    public static string Record(string player, string title) => $"{Site}?player={Uri.EscapeDataString(player)}&game={Uri.EscapeDataString(title)}";
}
