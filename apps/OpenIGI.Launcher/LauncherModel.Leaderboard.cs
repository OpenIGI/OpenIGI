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
    private MissionBoardFeed? _missions;
    private AvatarFiles? _boardAvatars;

    /// <summary>Gets the single-player mission boards, made the first time they are wanted.</summary>
    public MissionBoardFeed Missions => _missions ??= new MissionBoardFeed(new MasterRunBoardReader(new RunsService(AccountHttp, MapService.Master)), RecordTitle);

    /// <summary>
    /// Watches or races a board's run in the game: the game is started on the run's id (<c>-playreplay</c> or <c>-race</c>) and
    /// brings its replay, with the ghost the server saw, down from the master itself.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="race">Whether to race it rather than watch it.</param>
    public void PlayBoardRun(string run, bool race)
    {
        if (RunsService.IsRunId(run))
        {
            Play(Current, [race ? "-race" : "-playreplay", run]);
        }
    }

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
        _missions?.You = SignedInForBoard ? _account!.Account!.Id : null;
    }
}
