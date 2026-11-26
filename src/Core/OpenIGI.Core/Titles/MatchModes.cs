namespace OpenIGI.Core.Titles;

/// <summary>
/// The modes a server can run, by the ids servers are listed with: what the game, the dedicated server, the launcher and the
/// menus all name a mode by. How a mode plays is the game's (<c>OpenIGI.Game.Multiplayer.Modes</c>); this is only its name.
/// </summary>
public static class MatchModes
{
    /// <summary>Everyone together against the level.</summary>
    public const string Coop = "coop";

    /// <summary>Two teams taking the map's objectives in order (I.G.I. 2's own game).</summary>
    public const string Objective = "objective";

    /// <summary>Two teams, a point a kill.</summary>
    public const string TeamDeathmatch = "tdm";

    /// <summary>Everyone for themselves, a point a kill.</summary>
    public const string FreeForAll = "ffa";

    /// <summary>Two teams, rounds of one life each, won by the team left standing.</summary>
    public const string Elimination = "elim";

    /// <summary>Two teams, rounds of one life each: one plants a bomb at the map's sites, the other stops it.</summary>
    public const string SearchAndDestroy = "sd";

    /// <summary>Gets every mode id, in the order a menu lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Coop, Objective, TeamDeathmatch, FreeForAll, Elimination, SearchAndDestroy];

    /// <summary>Gets whether a mode plays players against each other on a level made an arena.</summary>
    /// <param name="id">The mode's id.</param>
    /// <returns><see langword="true"/> for the versus modes.</returns>
    public static bool IsVersus(string? id) => id is TeamDeathmatch or FreeForAll or Elimination or SearchAndDestroy;

    /// <summary>Gets whether a versus mode plays two teams, rather than everyone for themselves.</summary>
    /// <param name="id">The mode's id.</param>
    /// <returns><see langword="true"/> for the team modes.</returns>
    public static bool HasTeams(string? id) => id is not FreeForAll;

    /// <summary>Gets the name a mode is shown by, in capitals: <c>SEARCH &amp; DESTROY</c>.</summary>
    /// <param name="id">The mode's id.</param>
    /// <returns>Its name; co-op's is <c>CO-OP</c>.</returns>
    public static string Title(string? id) => id switch
    {
        null or "" or Coop => "CO-OP",
        TeamDeathmatch => "TEAM DEATHMATCH",
        FreeForAll => "FREE FOR ALL",
        Elimination => "ELIMINATION",
        SearchAndDestroy => "SEARCH & DESTROY",
        Objective => "OBJECTIVE",
        _ => id.ToUpperInvariant(),
    };
}
