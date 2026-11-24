using OpenIGI.Core.Titles;
using OpenIGI.Releases.Accounts;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>A game a leaderboard can show: its title id, as the master names it, and its name as a page says it.</summary>
/// <param name="Id">The title id, such as <c>igi2</c>.</param>
/// <param name="Name">The name, such as <c>I.G.I. 2</c>.</param>
public sealed record LeaderboardGame(string Id, string Name)
{
    /// <summary>The games the official servers keep boards of, as the master names them (stats.js TITLES).</summary>
    public static readonly IReadOnlyList<LeaderboardGame> Official = [Of(GameTitle.Igi1), Of(GameTitle.Igi2)];

    /// <summary>A title's board.</summary>
    /// <param name="title">The title.</param>
    /// <returns>Its board, by its id and short name.</returns>
    public static LeaderboardGame Of(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return new LeaderboardGame(title.Id, title.ShortName);
    }

    /// <summary>A match's map as the game's menus name it: <c>Trainyard</c> for mission 2 of Project I.G.I.; as given otherwise.</summary>
    /// <param name="title">The game, by title id.</param>
    /// <param name="map">The map, as a match names it: a retail mission's number, or a community map's name.</param>
    /// <returns>The name.</returns>
    public static string MapName(string title, string map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return int.TryParse(map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mission)
            && GameTitle.All.FirstOrDefault(game => game.Id == title)?.MissionName(mission) is { } name
                ? name
                : map;
    }
}

/// <summary>
/// What a <see cref="LeaderboardView"/> is shown from, by the launcher and by the game's menu alike: the boards and records
/// themselves (<see cref="LeaderboardFeed"/>), the games there are, the players' pictures as they come down, the maps by name,
/// and the way out to the site.
/// </summary>
public interface ILeaderboardSource
{
    /// <summary>Gets the boards and records, and what is picked.</summary>
    LeaderboardFeed Feed { get; }

    /// <summary>Gets the games whose boards can be shown, in the order they are offered.</summary>
    IReadOnlyList<LeaderboardGame> Games { get; }

    /// <summary>Gets a line saying how to be on the board under one's name, while the player isn't; <see langword="null"/> once they are.</summary>
    string? SignInHint { get; }

    /// <summary>Gets whether a page can be opened in the player's browser.</summary>
    bool CanOpenLinks { get; }

    /// <summary>A player's picture by its address, once it has come down; asked for until then.</summary>
    /// <param name="url">The address, or <see langword="null"/> for none.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    UiPicture? Avatar(string? url);

    /// <summary>A map as the game's menus name it: <c>Trainyard</c> for mission 2; the map as given where it is not known.</summary>
    /// <param name="title">The game, by title id.</param>
    /// <param name="map">The map, as a match names it.</param>
    /// <returns>The name.</returns>
    string MapName(string title, string map);

    /// <summary>Opens a page in the player's browser.</summary>
    /// <param name="url">The page.</param>
    void OpenLink(string url);
}
