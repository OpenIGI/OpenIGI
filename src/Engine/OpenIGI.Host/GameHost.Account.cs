using OpenIGI.UI.Kit;
using System.Text.Json;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend;
using OpenIGI.Game.Config;
using OpenIGI.Host.Configuration;
using OpenIGI.Net.Identity;
using OpenIGI.Releases.Accounts;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Host;

/// <summary>The player's online account, from the profile page: signed into with Discord, optional.</summary>
public sealed partial class GameHost
{
    // One client for every account request: the requests are few and small, and a browser has one connection anyway.
    private static readonly HttpClient AccountHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private AccountLink? _account;
    private Menu.AccountAvatar? _avatar;
    private Menu.PlayerAvatars? _playerAvatars;
    private Menu.MenuPictureLoader? _menuPictures;

    // What makes the menu's pictures, the missions' and the avatar alike.
    private Menu.MenuPictureLoader MenuPictures => _menuPictures ??= new Menu.MenuPictureLoader(_device);
    private ProfileSync? _profileSync;
    private StatsService? _stats;
    private Task<PlayerRecord>? _recordAsked;
    private (string Account, PlayerRecord Record, DateTimeOffset At)? _record;
    private DateTimeOffset _recordAskedAt = DateTimeOffset.MinValue;

    // The game the kept record is of, and the one last asked about: each game's record is its own.
    private string _recordFor = string.Empty;
    private string _recordAskedFor = string.Empty;

    // Whether the profile page has looked at the account: until it has, nothing is asked of the servers.
    private bool _accountSeen;

    /// <summary>Gets or sets what opens a page in the player's browser, or <see langword="null"/> where this build can't.</summary>
    /// <remarks>
    /// Set by the composition root before <see cref="Start"/>: the desktop hands the address to the system, the browser
    /// build opens a tab. Without it the profile page shows the sign-in address to copy instead.
    /// </remarks>
    public Action<string>? OpenLink { get; set; }

    // The session's players' avatars, where this run reaches the servers at all: a server, a benchmark or a capture never asks.
    private Menu.PlayerAvatars? PlayerAvatarStore =>
        _options.SimulationOnly || !_options.SavesUserFiles ? null : _playerAvatars ??= new Menu.PlayerAvatars(MenuPictures, AccountHttp);

    // A player's avatar as the scoreboard draws it, by its line's reference: this player's own the one its account already has.
    private UI.Kit.UiPicture? BoardAvatar(string reference)
    {
        if (reference.Length == 0)
        {
            return null;
        }

        string own = Game.Multiplayer.DiscordAvatar.Reference(_account is { IsSignedIn: true } signedIn ? signedIn.Account?.Avatar : null);
        return own.Length > 0 && reference == own && _avatar?.Picture is { } mine ? mine : PlayerAvatarStore?.Of(reference);
    }

    // Each frame, once the page has looked: requests collected, a waiting sign-in asked about.
    private void UpdateAccount()
    {
        if (_accountSeen)
        {
            _account?.Update();
            _profileSync?.Update();
            _avatar?.Update(_account is { IsSignedIn: true } signedIn ? signedIn.Account : null);
            UpdateRecord();
        }
    }

    // The menu's account panel, where this run can reach the servers at all. A server, a benchmark or a capture never asks.
    private AccountPanel? AccountPanelFor()
    {
        if (_options.SimulationOnly || !_options.SavesUserFiles)
        {
            return null;
        }

        if (_account is null)
        {
            AccountService service = new(AccountHttp, MapService.Master);
            _account = new AccountLink(
                service,
                PlayerSigner,
                url => OpenLink?.Invoke(url),
                // What the account lists this install as; never the computer's own name, which is the player's business.
                OperatingSystem.IsBrowser() ? "Browser" : "Desktop",
                rememberPath: SettingsPaths.GetFilePath("account.json"));
            _avatar = new Menu.AccountAvatar(MenuPictures, AccountHttp);
            _stats = new StatsService(AccountHttp, MapService.Master);
            _profileSync = new ProfileSync(service, PlayerSigner, new SyncedProfile(this), SettingsPaths.GetFilePath("account-sync.json"));
            _account.AccountChanged += account =>
            {
                if (account is null)
                {
                    _profileSync.SignedOut();
                }
                else
                {
                    _profileSync.SignedIn(account.Id);
                }
            };

            // A computer signed in when the game last ran asks at once, so its profile is in step before it is played.
            _accountSeen = _profileSync.WasSignedIn;
        }

        return new AccountPanel(_account, this);
    }

    // The record on the official servers in the game being played: read once signed in, again every two minutes while the page
    // looks, and at once when another game is chosen.
    private void UpdateRecord()
    {
        if (_recordAsked is { IsCompleted: true } asked)
        {
            _recordAsked = null;
            if (asked.IsCompletedSuccessfully && _account?.Account is { } account)
            {
                _record = (account.Id, asked.Result, DateTimeOffset.UtcNow);
                _recordFor = _recordAskedFor;
            }
        }

        if (_recordAsked is not null || _stats is null || _account is not { State: AccountLinkState.Linked, Account: { } signedIn })
        {
            return;
        }

        string title = _titleRuntime.Title.Id;
        bool stale = _record is not { } kept || kept.Account != signedIn.Id || _recordFor != title;
        if (stale || DateTimeOffset.UtcNow - _recordAskedAt > TimeSpan.FromMinutes(2))
        {
            _recordAskedAt = DateTimeOffset.UtcNow;
            _recordAskedFor = title;
            _recordAsked = _stats.RecordAsync(signedIn.Id, title);
        }
    }

    private static PlayerRequestSigner? PlayerSigner() =>
        PlayerIdentities.Default is { IsReady: true } identity ? new PlayerRequestSigner(identity) : null;

    // The name the player plays under: their Discord name once signed in (or while offline after), the profile's until then.
    private string PlayerName() =>
        _account is { IsSignedIn: true, Account: { } account } ? account.Name
        : _configuration.Active?.Name is { Length: > 0 } name ? name : "Jones";

    // The profile was kept: what follows the player goes up to their account shortly, when signed in.
    private void ProfileSaved() => _profileSync?.Changed();

    // The active profile as the account keeps it in step (ProfileDocument), and kept here once merged.
    private sealed class SyncedProfile(GameHost host) : ISyncedProfile
    {
        public string? Fingerprint() => host._configuration.Active is { } profile ? ProfileDocument.From(profile).Fingerprint() : null;

        public JsonElement? Take() => host._configuration.Active is { } profile ? ProfileDocument.From(profile).ToJson() : null;

        public JsonElement? Adopt(JsonElement remote, bool localIsNewer)
        {
            if (host._configuration.Active is not { } profile || ProfileDocument.FromJson(remote) is not { } theirs)
            {
                return null;
            }

            ProfileDocument.Merge(ProfileDocument.From(profile), theirs, localIsNewer).ApplyTo(profile);
            host.RestoreTitleInputBindings();
            if (host._options.SavesUserFiles)
            {
                HostConfiguration.Save(host._configuration, host._logger);
            }

            return ProfileDocument.From(profile).ToJson();
        }
    }

    // The account as the profile page sees it. Nothing is asked of the servers until the page first looks, so a run that
    // never opens the profile -- a test, a straight-to-mission launch -- never reaches out.
    private sealed class AccountPanel(AccountLink link, GameHost host) : IAccountPanel
    {
        public AccountStatus Status
        {
            get
            {
                host._accountSeen = true;
                return link.State switch
                {
                    // Signed in when the game last ran: shown so from the first frame, until the master says otherwise.
                    AccountLinkState.Checking when link.Account is not null => AccountStatus.SignedIn,
                    AccountLinkState.Unlinked => AccountStatus.SignedOut,
                    AccountLinkState.Starting => AccountStatus.Starting,
                    AccountLinkState.Waiting => AccountStatus.WaitingForBrowser,
                    AccountLinkState.Linked => AccountStatus.SignedIn,
                    AccountLinkState.Offline => AccountStatus.Offline,
                    _ => AccountStatus.Checking,
                };
            }
        }

        public AccountSummary? Account => link.Account is { } account ? new AccountSummary(account.Id, account.Name, account.Avatar) : null;

        public string? SignInUrl => link.LinkUrl;

        public UiPicture? Avatar => host._avatar?.Picture;

        public string? Problem => link.Error;

        public AccountRecord? Record => host._record is { } kept && link.Account?.Id == kept.Account && host._recordFor == host._titleRuntime.Title.Id
            ? new AccountRecord(kept.Record.Season, Totals(kept.Record.SeasonTotals), Totals(kept.Record.AllTime), [.. (kept.Record.Medals ?? []).Select(static medal => medal.Name)])
            : null;

        public string? SyncNote => host._profileSync?.State switch
        {
            ProfileSyncState.Syncing => "Syncing your profile…",
            ProfileSyncState.Synced => "Your progress, difficulty and controls are synced to your account.",
            ProfileSyncState.Failed => "Couldn't sync your profile just now; it will try again.",
            _ => null,
        };

        public bool IsBusy => link.IsBusy;

        private static RecordTotals Totals(StatTotals totals) =>
            new(totals.Matches, totals.Wins, totals.Kills, totals.Deaths, totals.Kd, totals.Score, totals.Assists);

        public bool CanOpenLinks => host.OpenLink is not null;

        public void SignIn() => link.Link();

        public void OpenSignInPage() => link.OpenLinkAgain();

        public string? CopySignInLink()
        {
            if (link.LinkUrl is not { } url)
            {
                return null;
            }

            host._window.ClipboardText = url;
            return url;
        }

        public void CancelSignIn() => link.CancelLink();

        public void SignOut() => link.Unlink();

        public void DeleteAccount() => link.DeleteAccount();
    }
}
