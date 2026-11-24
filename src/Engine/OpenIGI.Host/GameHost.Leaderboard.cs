using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Replays;
using OpenIGI.Net.Identity;
using OpenIGI.Releases.Accounts;
using OpenIGI.Releases.Maps;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Leaderboards;

namespace OpenIGI.Host;

/// <summary>
/// The official servers' leaderboards, from the menu's Leaderboard page: the same boards and records the site and the launcher
/// show, read from the master only while the page is up. "You" on a board is the signed-in account, else the key this
/// computer plays under; the players' pictures come down as the scoreboard's do.
/// </summary>
public sealed partial class GameHost
{
    private LeaderboardFeed? _leaderboardFeed;
    private MissionBoardFeed? _missionBoardFeed;

    // A board's run asked to be watched or raced, its replay coming down; taken up on the frame it arrives.
    private (Task<byte[]?> Replay, bool Race)? _boardReplay;

    // The menu's leaderboards, where this run reaches the servers at all: a server, a benchmark or a capture never asks.
    private LeaderboardSource? LeaderboardSourceFor() =>
        _options.SimulationOnly || !_options.SavesUserFiles ? null : new LeaderboardSource(this);

    // The single-player boards, opened on the game being played.
    private MissionBoardFeed MissionBoardFeed => _missionBoardFeed ??= new MissionBoardFeed(new MasterRunBoardReader(RunsService), _titleRuntime.Title.Id);

    // A board's run asked for: its replay brought down, to be watched or raced once it is here.
    private void AskBoardReplay(string run, bool race)
    {
        if (_boardReplay is null)
        {
            _boardReplay = (RunsService.ReplayAsync(run), race);
        }
    }

    /// <summary>A board's replay come down: watched or raced, the menu put away for it; or said why not.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void CollectBoardReplay(FrameLoopState state)
    {
        if (_boardReplay is not { Replay.IsCompleted: true } asked)
        {
            return;
        }

        _boardReplay = null;
        string? problem;
        try
        {
            problem = asked.Replay.Result is { } bytes ? FromMenu(state, ReplayFile.Read(bytes), asked.Race) : "its replay is no longer kept";
        }
        catch (Exception ex) when (ex is AggregateException or InvalidDataException)
        {
            problem = (ex.InnerException ?? ex).Message.TrimEnd('.');
        }

        if (problem is not null)
        {
            _logger.Warning("Replays", $"A board's run was not {(asked.Race ? "raced" : "watched")}: {problem}.");
        }
    }

    // The boards, opened on the game being played.
    private LeaderboardFeed LeaderboardFeed => _leaderboardFeed ??= new LeaderboardFeed(new MasterLeaderboardReader(AccountHttp, MapService.Master), _titleRuntime.Title.Id);

    // Who "you" are on a board: the account once signed in, else this computer's key.
    private string? LeaderboardYou() =>
        _account is { State: AccountLinkState.Linked, Account: { } account } ? account.Id
        : PlayerIdentities.Default is { IsReady: true } identity ? PlayerIdentities.IdOf(identity.PublicKey) : null;

    // The boards as the page sees them: the account's name and pictures, and the browser.
    private sealed class LeaderboardSource(GameHost host) : ILeaderboardSource
    {
        public MissionBoardFeed Missions
        {
            get
            {
                MissionBoardFeed feed = host.MissionBoardFeed;
                feed.You = host._account is { State: AccountLinkState.Linked, Account: { } account } ? account.Id : null;
                return feed;
            }
        }

        public IReadOnlyList<(int Id, string Name)> MissionsOf(string title) =>
            title == host._titleRuntime.Title.Id
                ? [.. host.ShellMissions().Where(static m => m.Campaign is null).Select(static m => (m.Id, m.Name))]
                : [.. (Core.Titles.GameTitle.All.FirstOrDefault(game => game.Id == title)?.MissionNames ?? []).Select(static (name, i) => (i + 1, name))];

        public void Watch(string run) => host.AskBoardReplay(run, race: false);

        public void Race(string run) => host.AskBoardReplay(run, race: true);

        public LeaderboardFeed Feed
        {
            get
            {
                LeaderboardFeed feed = host.LeaderboardFeed;
                feed.You = host.LeaderboardYou();
                return feed;
            }
        }

        public IReadOnlyList<LeaderboardGame> Games => LeaderboardGame.Official;

        public string? SignInHint => host._account is { State: AccountLinkState.Linked }
            ? null
            : "Sign in with Discord on the Profile page to be on the board under your name.";

        public bool CanOpenLinks => host.OpenLink is not null;

        public UiPicture? Avatar(string? url) => host.BoardAvatar(Game.Multiplayer.DiscordAvatar.Reference(url));

        public string MapName(string title, string map) => LeaderboardGame.MapName(title, map);

        public void OpenLink(string url) => host.OpenLink?.Invoke(url);
    }
}
