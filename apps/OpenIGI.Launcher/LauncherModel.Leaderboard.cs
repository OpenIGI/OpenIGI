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
    private RunsService? _runs;
    private AvatarFiles? _boardAvatars;

    /// <summary>Gets the single-player mission boards, made the first time they are wanted.</summary>
    public MissionBoardFeed Missions => _missions ??= new MissionBoardFeed(new MasterRunBoardReader(Runs), RecordTitle);

    private RunsService Runs => _runs ??= new RunsService(AccountHttp, MapService.Master);

    /// <summary>
    /// Watches or races a board's run in the game: its replay, with the ghost the server saw, brought down to the settings folder
    /// the game shares, and the game started on it (<c>-playreplay</c> or <c>-race</c>).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="race">Whether to race it rather than watch it.</param>
    public void PlayBoardRun(string run, bool race)
    {
        ArgumentException.ThrowIfNullOrEmpty(run);
        string file = Path.Combine(SettingsPaths.GetUserDirectory(), "replays", "boards", string.Concat(run.Split(Path.GetInvalidFileNameChars())) + ".oigr");
        _ = Task.Run(async () =>
        {
            try
            {
                byte[]? bytes = await Runs.ReplayAsync(run).ConfigureAwait(false);
                if (bytes is null)
                {
                    _results.Enqueue(() => Report("That run's replay is no longer kept."));
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
                _results.Enqueue(() => Play(Current, [race ? "-race" : "-playreplay", file]));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or AccountRefusedException)
            {
                _results.Enqueue(() => Report($"The run could not be brought down: {ex.Message}", ex));
            }
        });
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
