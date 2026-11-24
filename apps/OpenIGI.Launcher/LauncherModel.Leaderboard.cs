using OpenIGI.Core.Configuration;
using OpenIGI.Releases.Accounts;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Launcher;

/// <summary>
/// The official servers' leaderboards, from the launcher: the same boards and records the site and the game's menu show, read
/// from the master only while the Leaderboard page is up, with the players' pictures kept in the settings folder the game
/// shares. "You" on a board is the signed-in account, else the key this computer plays under.
/// </summary>
internal sealed partial class LauncherModel
{
    private LeaderboardFeed? _leaderboard;
    private AvatarFiles? _boardAvatars;

    /// <summary>Gets the boards and records, made the first time they are wanted.</summary>
    public LeaderboardFeed Leaderboard => _leaderboard ??= new LeaderboardFeed(new MasterLeaderboardReader(AccountHttp, MapService.Master), RecordTitle);

    /// <summary>Gets the players' pictures on the boards, as files once they have come down.</summary>
    public AvatarFiles BoardAvatars => _boardAvatars ??= new AvatarFiles(AccountHttp, SettingsPaths.GetUserDirectory(), "player-avatar");

    /// <summary>Gets the game picked in the header, by title id: the board the page opens on.</summary>
    public string PickedTitle => RecordTitle;

    /// <summary>Gets whether the player is on the boards under their Discord name.</summary>
    public bool SignedInForBoard => _account is { State: AccountLinkState.Linked, Account: not null };

    /// <summary>Opens a page in the player's browser.</summary>
    /// <param name="url">The page.</param>
    public void OpenLink(string url) => Open(url);

    // Who "you" are on a board: the account once signed in, else this computer's key (made the first time it is needed).
    private void TickLeaderboard()
    {
        if (_leaderboard is null)
        {
            return;
        }

        _leaderboard.You = SignedInForBoard ? _account!.Account!.Id : (_identity ??= LoadIdentity())?.Id;
    }
}
