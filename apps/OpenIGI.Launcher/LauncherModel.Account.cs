using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net.Identity;
using OpenIGI.Releases.Accounts;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Launcher;

/// <summary>
/// The player's online account, from the launcher: signed into with Discord, as from the game's profile page. Both sign in
/// with the same key -- the one the game plays under, kept in the settings folder -- and an account is the keys linked to
/// it, so signing in here signs the game in too, and signing out of either signs out of both.
/// </summary>
internal sealed partial class LauncherModel
{
    private static readonly HttpClient AccountHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private AccountLink? _account;
    private EcdsaPlayerIdentity? _identity;
    private int _accountShown;

    /// <summary>Gets the account, made the first time it is asked for: the master is asked at once.</summary>
    public AccountLink Account => _account ??= new AccountLink(
        new AccountService(AccountHttp, MapService.Master),
        () => (_identity ??= LoadIdentity()) is { } identity ? new PlayerRequestSigner(identity) : null,
        Open,
        "Desktop");

    // The game's own key, read (or made, the first time anything needs it) where the game keeps it.
    private EcdsaPlayerIdentity? LoadIdentity()
    {
        try
        {
            return EcdsaPlayerIdentity.LoadOrCreate(SettingsPaths.GetFilePath("identity.key"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _logger.Warning(LogCategory, $"The player's key could not be read: {ex.Message}");
            return null;
        }
    }

    // Each frame: the account's requests collected, a waiting sign-in asked about; the page told when it changed.
    private void TickAccount()
    {
        if (_account is null)
        {
            return;
        }

        _account.Update();
        int shown = HashCode.Combine(_account.State, _account.Account?.Id, _account.Account?.Name, _account.Error, _account.IsBusy);
        if (shown != _accountShown)
        {
            _accountShown = shown;
            _logger.Information(LogCategory, _account.State switch
            {
                AccountLinkState.Linked => $"Account: signed in with Discord as {_account.Account?.Name}.",
                AccountLinkState.Unlinked => "Account: not signed in.",
                AccountLinkState.Waiting => "Account: waiting for the Discord sign-in in the browser.",
                AccountLinkState.Offline => $"Account: the master can't be reached ({_account.Error}).",
                _ => $"Account: {_account.State}{(_account.Error is { } error ? $" ({error})" : string.Empty)}.",
            });
            Changed();
        }
    }

    private void DisposeAccount() => _identity?.Dispose();
}
