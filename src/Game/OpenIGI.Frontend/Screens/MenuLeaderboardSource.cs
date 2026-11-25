using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Leaderboards;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The boards as the menu's Leaderboard section shows them: the host's boards and records, and what only the menu can do -- take
/// the player to a match or to their profile, start a mission to set a time, show a mission's picture -- and the player's name.
/// </summary>
/// <param name="boards">The host's boards.</param>
/// <param name="host">The shell's host, which starts missions and holds their pictures.</param>
/// <param name="activeTitle">The game being played, by title id: its missions are the ones the menu can start.</param>
/// <param name="yourName">The player's name.</param>
/// <param name="goTo">Takes the menu to one of its sections.</param>
internal sealed class MenuLeaderboardSource(ILeaderboardSource boards, IShellHost host, string activeTitle, Func<string> yourName, Action<ShellTab> goTo) : ILeaderboardSource
{
    public LeaderboardFeed Feed => boards.Feed;

    public IReadOnlyList<LeaderboardGame> Games => boards.Games;

    public string? SignInHint => boards.SignInHint;

    public bool CanOpenLinks => boards.CanOpenLinks;

    public MissionBoardFeed? Missions => boards.Missions;

    public bool CanReport => boards.CanReport;

    public string YourName => yourName();

    public bool SignedIn => boards.SignedIn;

    public bool CanFindMatch => true;

    public bool CanOpenProfile => true;

    public bool CanPlay => true;

    public UiPicture? Avatar(string? url) => boards.Avatar(url);

    public string MapName(string title, string map) => boards.MapName(title, map);

    public void OpenLink(string url) => boards.OpenLink(url);

    public IReadOnlyList<(int Id, string Name)> MissionsOf(string title) => boards.MissionsOf(title);

    public void Watch(string run) => boards.Watch(run);

    public void Race(string run) => boards.Race(run);

    public void Report(string run) => boards.Report(run);

    public void FindMatch() => goTo(ShellTab.Coop);

    public void OpenProfile() => goTo(ShellTab.Profile);

    /// <inheritdoc/>
    /// <remarks>Only the game being played has its missions to start; its rules are the ones its settings make.</remarks>
    public void PlayMission(string title, int mission, int difficulty, string ruleset)
    {
        if (title == activeTitle)
        {
            host.StartMission(mission, difficulty);
        }
    }

    public UiPicture? MissionPicture(string title, int mission) =>
        title == activeTitle ? host.MissionPicture(mission) ?? boards.MissionPicture(title, mission) : boards.MissionPicture(title, mission);
}
