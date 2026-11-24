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

    // The menu's leaderboards, where this run reaches the servers at all: a server, a benchmark or a capture never asks.
    private LeaderboardSource? LeaderboardSourceFor() =>
        _options.SimulationOnly || !_options.SavesUserFiles ? null : new LeaderboardSource(this);

    // The boards, opened on the game being played.
    private LeaderboardFeed LeaderboardFeed => _leaderboardFeed ??= new LeaderboardFeed(new MasterLeaderboardReader(AccountHttp, MapService.Master), _titleRuntime.Title.Id);

    // Who "you" are on a board: the account once signed in, else this computer's key.
    private string? LeaderboardYou() =>
        _account is { State: AccountLinkState.Linked, Account: { } account } ? account.Id
        : PlayerIdentities.Default is { IsReady: true } identity ? PlayerIdentities.IdOf(identity.PublicKey) : null;

    // The boards as the page sees them: the account's name and pictures, and the browser.
    private sealed class LeaderboardSource(GameHost host) : ILeaderboardSource
    {
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
