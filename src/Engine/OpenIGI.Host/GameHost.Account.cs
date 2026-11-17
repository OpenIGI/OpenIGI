using OpenIGI.Frontend;
using OpenIGI.Host.Accounts;
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

    // Whether the profile page has looked at the account: until it has, nothing is asked of the servers.
    private bool _accountSeen;

    /// <summary>Gets or sets what opens a page in the player's browser, or <see langword="null"/> where this build can't.</summary>
    /// <remarks>
    /// Set by the composition root before <see cref="Start"/>: the desktop hands the address to the system, the browser
    /// build opens a tab. Without it the profile page shows the sign-in address to copy instead.
    /// </remarks>
    public Action<string>? OpenLink { get; set; }

    // Each frame, once the page has looked: requests collected, a waiting sign-in asked about.
    private void UpdateAccount()
    {
        if (_accountSeen)
        {
            _account?.Update();
        }
    }

    // The menu's account panel, where this run can reach the servers at all. A server, a benchmark or a capture never asks.
    private AccountPanel? AccountPanelFor()
    {
        if (_options.SimulationOnly || !_options.SavesUserFiles)
        {
            return null;
        }

        _account ??= new AccountLink(
            new AccountService(AccountHttp, MapService.Master),
            static () => PlayerIdentities.Default is { IsReady: true } identity ? new PlayerRequestSigner(identity) : null,
            url => OpenLink?.Invoke(url),
            // What the account lists this install as; never the computer's own name, which is the player's business.
            OperatingSystem.IsBrowser() ? "Browser" : "Desktop");
        return new AccountPanel(_account, this);
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

        public string? Problem => link.Error;

        public bool IsBusy => link.IsBusy;

        public bool CanOpenLinks => host.OpenLink is not null;

        public void SignIn() => link.Link();

        public void OpenSignInPage()
        {
            if (link.LinkUrl is { } url)
            {
                host.OpenLink?.Invoke(url);
            }
        }

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
